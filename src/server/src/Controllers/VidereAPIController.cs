/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Tracker.Controllers.Base;
using Tracker.Models.API.Decks;


namespace Tracker.Services.Videre;

public sealed class VidereAPIClient(
  HttpClient httpClient,
  ApplicationOptions appOptions) : IVidereAPIClient
{
  public async Task<List<CardSearchResultDTO>> SearchCardsAsync(
    string query,
    int limit,
    CancellationToken cancellationToken = default)
  {
    var boundedLimit = Math.Clamp(limit, 1, 50);
    var apiLimit = Math.Min(boundedLimit * 2, 100);
    var requestUri = BuildUri(
      $"cards?q={Uri.EscapeDataString(query.Trim())}" +
      $"&unique=cards&order=rank&limit={apiLimit}");

    using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    request.Headers.UserAgent.ParseAdd($"VidereTracker/{ProductInfo.Version}");

    try
    {
      using var response = await httpClient.SendAsync(request, cancellationToken);
      var content = await response.Content.ReadAsStringAsync(cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
        if (response.StatusCode == HttpStatusCode.BadRequest && IsNoResults(content))
        {
          return [];
        }

        throw new VidereAPIException(
          "Videre cards API request failed",
          (int)response.StatusCode,
          content);
      }

      var payload = DeserializeResponse<CardsResponse>(content);
      return MapCards(payload?.Data, boundedLimit);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (VidereAPIException)
    {
      throw;
    }
    catch (Exception ex) when (
      ex is HttpRequestException or TaskCanceledException or UriFormatException or JsonException)
    {
      throw new VidereAPIException("Videre cards API request failed", innerException: ex);
    }
  }

  public async Task<IReadOnlyDictionary<int, VidereProductResult>> GetCollectionProductsAsync(
    IReadOnlyCollection<int> collectionIds,
    CancellationToken cancellationToken = default)
  {
    const int pageSize = 500;
    var products = new Dictionary<int, VidereProductResult>();
    if (collectionIds.Count == 0) return products;

    var requestBody = JsonSerializer.Serialize(new VidereCardsSearchRequest
    {
      Collection = CreateCollection(collectionIds)
    });
    var offset = 0;

    while (true)
    {
      var uri = BuildUri($"cards/search?q={Uri.EscapeDataString("is:product")}&limit={pageSize}&offset={offset}");
      var responseContent = await SendAsync(HttpMethod.Post, uri, requestBody, cancellationToken);
      if (responseContent is null) break;

      var response = DeserializeResponse<VidereProductsResponse>(responseContent);
      var page = response?.Data ?? [];
      foreach (var product in page.Where(product => product.Id > 0))
      {
        products[product.Id] = new VidereProductResult(
          product.Id, product.SetCode, product.SetName, product.Name, product.Description,
          product.ObjectType, product.ImageUrl, product.IsTradable);
      }

      if (response?.Meta?.HasMore != true || page.Count == 0 || response.Meta.NextOffset is null) break;
      offset = response.Meta.NextOffset.Value;
    }

    return products;
  }

  public async Task<IReadOnlyCollection<int>> SearchCollectionAsync(
    IReadOnlyCollection<int> collectionIds,
    string query,
    CancellationToken cancellationToken = default)
  {
    const int pageSize = 500;
    var ids = new HashSet<int>();
    if (collectionIds.Count == 0) return ids;

    var requestBody = JsonSerializer.Serialize(new VidereCardsSearchRequest
    {
      Collection = CreateCollection(collectionIds)
    });
    var offset = 0;

    while (true)
    {
      var uri = BuildUri($"cards/search?q={Uri.EscapeDataString(query)}&limit={pageSize}&offset={offset}");
      var responseContent = await SendAsync(HttpMethod.Post, uri, requestBody, cancellationToken);
      if (responseContent is null) break;

      var response = DeserializeResponse<VidereCollectionSearchResponse>(responseContent);
      var page = response?.Data ?? [];
      foreach (var result in page.Where(result => result.Id > 0)) ids.Add(result.Id);

      if (response?.Meta?.HasMore != true || page.Count == 0 || response.Meta.NextOffset is null) break;
      offset = response.Meta.NextOffset.Value;
    }

    return ids;
  }

  public async Task<ViderePricesResult> GetLatestPricesAsync(
    IReadOnlyCollection<int> collectionIds,
    CancellationToken cancellationToken = default)
  {
    if (collectionIds.Count == 0)
    {
      return new ViderePricesResult(new Dictionary<int, ViderePriceResult>(), []);
    }

    var requestBody = JsonSerializer.Serialize(new ViderePricesRequest
    {
      Collection = new ViderePricesCollection { Ids = collectionIds.ToArray() },
      Date = "latest"
    });
    var responseContent = await SendAsync(HttpMethod.Post, BuildUri("prices"), requestBody, cancellationToken);
    if (responseContent is null)
    {
      return new ViderePricesResult(new Dictionary<int, ViderePriceResult>(), collectionIds);
    }

    var response = DeserializeResponse<ViderePricesResponse>(responseContent);
    var prices = (response?.Data ?? [])
      .Where(price => price.Id > 0)
      .ToDictionary(
        price => price.Id,
        price => new ViderePriceResult(price.Id, price.PriceDate, price.SellPrice, price.Source));
    var missing = response?.Meta?.MissingIds ?? collectionIds.Where(id => !prices.ContainsKey(id)).ToArray();
    return new ViderePricesResult(prices, missing);
  }

  public async Task<IReadOnlyList<ViderePriceResult>> GetPriceHistoryAsync(
    int catalogId,
    string? from,
    string? to,
    int limit,
    int offset,
    CancellationToken cancellationToken = default)
  {
    var query = new List<string> { $"limit={limit}", $"offset={offset}" };
    if (!string.IsNullOrWhiteSpace(from)) query.Add($"from={Uri.EscapeDataString(from.Trim())}");
    if (!string.IsNullOrWhiteSpace(to)) query.Add($"to={Uri.EscapeDataString(to.Trim())}");

    var responseContent = await SendAsync(
      HttpMethod.Get,
      BuildUri($"prices/{catalogId}/history?{string.Join("&", query)}"),
      null,
      cancellationToken);
    if (responseContent is null) return [];

    var response = DeserializeResponse<ViderePricesResponse>(responseContent);
    return (response?.Data ?? [])
      .Where(price => price.Id == catalogId)
      .OrderBy(price => price.PriceDate, StringComparer.Ordinal)
      .Select(price => new ViderePriceResult(price.Id, price.PriceDate, price.SellPrice, price.Source))
      .ToArray();
  }

  public async Task<VidereCardDetailResult?> GetCardDetailsAsync(
    int catalogId,
    CancellationToken cancellationToken = default)
  {
    var responseContent = await SendAsync(HttpMethod.Get, BuildUri($"cards/{catalogId}"), null, cancellationToken);
    if (responseContent is null) return null;

    var response = DeserializeResponse<VidereCardDetailsResponse>(responseContent);
    var card = response?.Data?.FirstOrDefault(row => row.Id == catalogId) ?? response?.Data?.FirstOrDefault();
    return card is null || card.Id <= 0
      ? null
      : new VidereCardDetailResult(
        card.Id,
        card.Name,
        card.PrintedName,
        card.DisplayName,
        NormalizeOptionalText(card.SetCode)?.ToUpperInvariant(),
        card.SetName,
        card.CollectorNumber,
        NormalizeOptionalText(card.Rarity),
        NormalizeOptionalText(card.ManaCost),
        card.ManaValue,
        NormalizeOptionalText(card.TypeLine),
        NormalizeCardText(card.OracleText ?? card.FlavorText),
        NormalizeCardText(card.FlavorText),
        NormalizeColors(card.Colors),
        card.ImageUrl,
        NormalizeOptionalText(card.Power),
        NormalizeOptionalText(card.Toughness),
        NormalizeOptionalText(card.Loyalty),
        NormalizeOptionalText(card.Defense),
        NormalizeOptionalText(card.Artist),
        NormalizeOptionalText(card.PromoLabel));
  }

  private VidereCardsSearchCollection CreateCollection(IReadOnlyCollection<int> ids) => new()
  {
    Ids = ids.ToArray(),
    Mode = "only",
    Match = "prints"
  };

  private Uri BuildUri(string path)
  {
    try
    {
      return new Uri($"{appOptions.VidereAPIUrl.TrimEnd('/')}/{path}");
    }
    catch (UriFormatException ex)
    {
      throw new VidereAPIException("Videre API URL is invalid", innerException: ex);
    }
  }

  private static T? DeserializeResponse<T>(string content)
  {
    try
    {
      return JsonSerializer.Deserialize<T>(content, JsonSerializerOptions.Web);
    }
    catch (JsonException ex)
    {
      throw new VidereAPIException("Videre API returned malformed JSON", response: content, innerException: ex);
    }
  }

  private async Task<string?> SendAsync(
    HttpMethod method,
    Uri uri,
    string? body,
    CancellationToken cancellationToken)
  {
    using var request = new HttpRequestMessage(method, uri);
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    request.Headers.UserAgent.ParseAdd($"VidereTracker/{ProductInfo.Version}");
    if (body is not null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");

    try
    {
      using var response = await httpClient.SendAsync(request, cancellationToken);
      var content = await response.Content.ReadAsStringAsync(cancellationToken);
      if (response.IsSuccessStatusCode) return content;
      if (response.StatusCode == HttpStatusCode.BadRequest && IsNoResults(content)) return null;
      throw new VidereAPIException("Videre API request failed", (int)response.StatusCode, content);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (VidereAPIException)
    {
      throw;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException or JsonException)
    {
      throw new VidereAPIException("Videre API request failed", innerException: ex);
    }
  }

  private static bool IsNoResults(string content)
  {
    try
    {
      using var document = JsonDocument.Parse(content);
      return document.RootElement.TryGetProperty("object", out var objectProperty) &&
        objectProperty.GetString() == "error" &&
        document.RootElement.TryGetProperty("message", out var messageProperty) &&
        string.Equals(messageProperty.GetString(), "No results found.", StringComparison.OrdinalIgnoreCase);
    }
    catch (JsonException)
    {
      return false;
    }
  }

  private static List<CardSearchResultDTO> MapCards(
    IReadOnlyList<CardSearchResult>? cards,
    int limit)
  {
    if (cards is null || cards.Count == 0) return [];

    var results = new List<CardSearchResultDTO>(Math.Min(limit, cards.Count));
    var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var card in cards)
    {
      if (card.Id <= 0 || string.IsNullOrWhiteSpace(card.Name) || !seenNames.Add(card.Name))
      {
        continue;
      }

      results.Add(new CardSearchResultDTO
      {
        Id = $"CARD_{card.Id}",
        MtgoId = card.Id,
        SetCode = NormalizeOptionalText(card.SetCode)?.ToUpperInvariant() ?? "",
        Name = card.Name,
        Type = string.IsNullOrWhiteSpace(card.TypeLine) ? "Card" : card.TypeLine,
        Text = NormalizeCardText(card.OracleText ?? card.FlavorText),
        Power = NormalizeOptionalText(card.Power),
        Toughness = NormalizeOptionalText(card.Toughness),
        Defense = NormalizeOptionalText(card.Defense),
        Loyalty = NormalizeOptionalText(card.Loyalty),
        Colors = NormalizeColors(card.Colors),
        ImageUrl = $"https://r2.videreproject.com/cards/{card.Id}-300px.png"
      });

      if (results.Count >= limit) break;
    }
    return results;
  }

  private static string NormalizeCardText(string? value) => string.IsNullOrWhiteSpace(value)
    ? ""
    : value
      .Replace("@i", "", StringComparison.Ordinal)
      .Replace("@-", "", StringComparison.Ordinal)
      .Replace("\r\n", "\n", StringComparison.Ordinal)
      .Replace("\r", "\n", StringComparison.Ordinal)
      .Trim();

  private static string? NormalizeOptionalText(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value.Trim();

  private static List<string> NormalizeColors(IReadOnlyList<string>? colors) => colors is null
    ? []
    : colors
      .Where(color => !string.IsNullOrWhiteSpace(color))
      .Select(color => color.Trim().ToUpperInvariant())
      .Where(color => color is "W" or "U" or "B" or "R" or "G" or "C")
      .Distinct()
      .OrderBy(color => "WUBRGC".IndexOf(color, StringComparison.Ordinal))
      .ToList();

  private sealed class CardsResponse
  {
    [JsonPropertyName("data")]
    public List<CardSearchResult>? Data { get; set; }
  }

  private sealed class CardSearchResult
  {
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("set_code")]
    public string? SetCode { get; set; }

    [JsonPropertyName("type_line")]
    public string? TypeLine { get; set; }

    [JsonPropertyName("oracle_text")]
    public string? OracleText { get; set; }

    [JsonPropertyName("flavor_text")]
    public string? FlavorText { get; set; }

    [JsonPropertyName("colors")]
    public List<string>? Colors { get; set; }

    [JsonPropertyName("power")]
    public string? Power { get; set; }

    [JsonPropertyName("toughness")]
    public string? Toughness { get; set; }

    [JsonPropertyName("loyalty")]
    public string? Loyalty { get; set; }

    [JsonPropertyName("defense")]
    public string? Defense { get; set; }
  }
}

public interface IVidereAPIClient
{
  Task<List<CardSearchResultDTO>> SearchCardsAsync(
    string query,
    int limit,
    CancellationToken cancellationToken = default);

  Task<IReadOnlyDictionary<int, VidereProductResult>> GetCollectionProductsAsync(
    IReadOnlyCollection<int> collectionIds,
    CancellationToken cancellationToken = default);

  Task<IReadOnlyCollection<int>> SearchCollectionAsync(
    IReadOnlyCollection<int> collectionIds,
    string query,
    CancellationToken cancellationToken = default);

  Task<ViderePricesResult> GetLatestPricesAsync(
    IReadOnlyCollection<int> collectionIds,
    CancellationToken cancellationToken = default);

  Task<IReadOnlyList<ViderePriceResult>> GetPriceHistoryAsync(
    int catalogId,
    string? from,
    string? to,
    int limit,
    int offset,
    CancellationToken cancellationToken = default);

  Task<VidereCardDetailResult?> GetCardDetailsAsync(
    int catalogId,
    CancellationToken cancellationToken = default);
}

public sealed record VidereProductResult(
  int Id,
  string? SetCode,
  string? SetName,
  string? Name,
  string? Description,
  string? ObjectType,
  string? ImageUrl,
  bool? IsTradable);

public sealed record ViderePriceResult(
  int Id,
  string? PriceDate,
  decimal SellPrice,
  string? Source);

public sealed record ViderePricesResult(
  IReadOnlyDictionary<int, ViderePriceResult> Prices,
  IReadOnlyCollection<int> MissingIds);

public sealed record VidereCardDetailResult(
  int Id,
  string Name,
  string? PrintedName,
  string? DisplayName,
  string? SetCode,
  string? SetName,
  string? CollectorNumber,
  string? Rarity,
  string? ManaCost,
  double? ManaValue,
  string? TypeLine,
  string? OracleText,
  string? FlavorText,
  IReadOnlyList<string>? Colors,
  string? ImageUrl,
  string? Power,
  string? Toughness,
  string? Loyalty,
  string? Defense,
  string? Artist,
  string? PromoLabel);

public sealed class VidereAPIException(
  string message,
  int? statusCode = null,
  string? response = null,
  Exception? innerException = null) : Exception(message, innerException)
{
  public int? StatusCode { get; } = statusCode;
  public string? Response { get; } = response;
}

public interface INBACArchetypeClient
{
  Task<JsonElement> DetectArchetypeAsync(
    IReadOnlyCollection<NBACDeckCard> cards,
    string format,
    CancellationToken cancellationToken = default);
}

public sealed record NBACDeckCard(
  [property: JsonPropertyName("name")] string Name,
  [property: JsonPropertyName("quantity")] int Quantity);

public sealed class NBACAPIException(
  string message,
  int? statusCode = null,
  string? response = null,
  Exception? innerException = null) : Exception(message, innerException)
{
  public int? StatusCode { get; } = statusCode;
  public string? Response { get; } = response;
}

public sealed class NBACArchetypeClient(
  HttpClient httpClient,
  ApplicationOptions appOptions) : INBACArchetypeClient
{
  public async Task<JsonElement> DetectArchetypeAsync(
    IReadOnlyCollection<NBACDeckCard> cards,
    string format,
    CancellationToken cancellationToken = default)
  {
    var requestBody = JsonSerializer.Serialize(cards);
    using var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

    try
    {
      using var response = await httpClient.PostAsync(
        $"{appOptions.NbacApiUrl}?format={Uri.EscapeDataString(format.ToLowerInvariant())}&explain=1",
        content,
        cancellationToken);
      var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
      if (!response.IsSuccessStatusCode)
      {
        throw new NBACAPIException(
          "NBAC API request failed",
          (int)response.StatusCode,
          responseContent);
      }

      return JsonSerializer.Deserialize<JsonElement>(responseContent);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (NBACAPIException)
    {
      throw;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
    {
      throw new NBACAPIException("Failed to connect to NBAC API", innerException: ex);
    }
  }
}

[ApiController]
public sealed class VidereAPIController(IVidereAPIClient videreAPI) : APIController
{
  [HttpGet("/api/decks/search-cards")]
  [ProducesResponseType(typeof(List<CardSearchResultDTO>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status502BadGateway)]
  public async Task<ActionResult<List<CardSearchResultDTO>>> SearchCards(
    [FromQuery] string? q = null,
    [FromQuery] int limit = 24,
    CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(q)) return Ok(new List<CardSearchResultDTO>());

    try
    {
      return Ok(await videreAPI.SearchCardsAsync(q, limit, cancellationToken));
    }
    catch (VidereAPIException ex)
    {
      return StatusCode(StatusCodes.Status502BadGateway, new
      {
        error = ex.Message,
        statusCode = ex.StatusCode,
        response = ex.Response,
        message = ex.InnerException?.Message
      });
    }
  }
}

internal static class PriceAPISchedule
{
  internal static DateTimeOffset GetCacheExpiration(DateTimeOffset now)
  {
    var timeZone = GetTimeZone();
    var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
    var localExpiration = new DateTime(
      localNow.Year, localNow.Month, localNow.Day, 5, 40, 0, DateTimeKind.Unspecified);

    if (localNow.DateTime >= localExpiration)
    {
      localExpiration = localExpiration.AddDays(1);
    }

    return new DateTimeOffset(
      localExpiration,
      timeZone.GetUtcOffset(localExpiration)).ToUniversalTime();
  }

  private static TimeZoneInfo GetTimeZone()
  {
    foreach (var id in new[] { "Central European Standard Time", "Europe/Berlin" })
    {
      try
      {
        return TimeZoneInfo.FindSystemTimeZoneById(id);
      }
      catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
      {
      }
    }

    return TimeZoneInfo.Utc;
  }
}
