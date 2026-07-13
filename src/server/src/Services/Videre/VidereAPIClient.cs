/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Tracker.Controllers.Models.Decks;

using Generated = Tracker.Services.Videre.Generated;


namespace Tracker.Services.Videre;

public sealed class VidereAPIClient
{
  private readonly Generated.VidereOpenAPIClient videreOpenAPIClient;
  private readonly HttpClient httpClient;
  private readonly ApplicationOptions appOptions;

  internal VidereAPIClient(
    Generated.VidereOpenAPIClient videreOpenAPIClient,
    HttpClient httpClient,
    ApplicationOptions appOptions)
  {
    this.videreOpenAPIClient = videreOpenAPIClient;
    this.httpClient = httpClient;
    this.appOptions = appOptions;
  }

  internal async Task<List<CardSearchResultDTO>> SearchCardsAsync(
    string query,
    int limit,
    CancellationToken cancellationToken = default)
  {
    var boundedLimit = Math.Clamp(limit, 1, 50);
    var apiLimit = Math.Min(boundedLimit * 2, 100);
    try
    {
      var response = await videreOpenAPIClient.SearchCardsAsync(
        q: query.Trim(),
        unique: Generated.Unique.Cards,
        order: Generated.Order.Rank,
        limit: apiLimit,
        cancellationToken: cancellationToken);
      return MapCards(response.Data, boundedLimit);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (Generated.VidereOpenAPIException<Generated.Error> ex) when (IsNoResults(ex.Result))
    {
      return [];
    }
    catch (Generated.VidereOpenAPIException ex)
    {
      throw ToTrackerException("Videre cards API request failed", ex);
    }
    catch (Exception ex) when (
      ex is HttpRequestException or TaskCanceledException)
    {
      throw new VidereAPIException("Videre cards API request failed", innerException: ex);
    }
  }

  internal async Task<IReadOnlyDictionary<int, VidereProductResult>> GetCollectionProductsAsync(
    IReadOnlyCollection<int> collectionIds,
    CancellationToken cancellationToken = default)
  {
    const int pageSize = 500;
    var products = new Dictionary<int, VidereProductResult>();
    if (collectionIds.Count == 0) return products;

    var requestBody = JsonSerializer.Serialize(new Generated.CardSearchRequest
    {
      Collection = CreateCollection(collectionIds)
    }, JsonSerializerOptions.Web);
    var offset = 0;

    while (true)
    {
      var uri = BuildUri($"cards/search?q={Uri.EscapeDataString("is:product")}&limit={pageSize}&offset={offset}");
      var responseContent = await SendAsync(HttpMethod.Post, uri, requestBody, cancellationToken);
      if (responseContent is null) break;

      var response = DeserializeResponse<VidereProductSearchResponse>(responseContent);
      var page = response?.Data ?? [];
      foreach (var product in page.Where(product => product.Id > 0))
      {
        products[product.Id] = new VidereProductResult(
          product.Id, product.Set_code, product.Set_name, product.Name, product.Description,
          product.Object_type, product.Image_url?.ToString(), product.Is_tradable);
      }

      if (response?.Meta?.Has_more != true || page.Count == 0 || response.Meta.Next_offset is null) break;
      offset = response.Meta.Next_offset.Value;
    }

    return products;
  }

  internal async Task<IReadOnlyCollection<int>> SearchCollectionAsync(
    IReadOnlyCollection<int> collectionIds,
    string query,
    CancellationToken cancellationToken = default)
  {
    const int pageSize = 500;
    var ids = new HashSet<int>();
    if (collectionIds.Count == 0) return ids;

    var requestBody = new Generated.CardSearchRequest
    {
      Collection = CreateCollection(collectionIds)
    };
    var offset = 0;

    while (true)
    {
      Generated.Response2 response;
      try
      {
        response = await videreOpenAPIClient.SearchCardsWithCollectionAsync(
          q: query,
          limit: pageSize,
          offset: offset,
          body: requestBody,
          cancellationToken: cancellationToken);
      }
      catch (Generated.VidereOpenAPIException<Generated.Error> ex) when (IsNoResults(ex.Result))
      {
        break;
      }
      catch (Generated.VidereOpenAPIException ex)
      {
        throw ToTrackerException("Videre collection search request failed", ex);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
      {
        throw new VidereAPIException("Videre collection search request failed", innerException: ex);
      }

      var page = response.Data ?? [];
      foreach (var result in page.Where(result => result.Id > 0)) ids.Add(result.Id);

      if (!response.Meta.Has_more || page.Count == 0 || response.Meta.Next_offset is null) break;
      offset = response.Meta.Next_offset.Value;
    }

    return ids;
  }

  internal async Task<ViderePricesResult> GetLatestPricesAsync(
    IReadOnlyCollection<int> collectionIds,
    CancellationToken cancellationToken = default)
  {
    if (collectionIds.Count == 0)
    {
      return new ViderePricesResult(new Dictionary<int, ViderePriceResult>(), []);
    }

    try
    {
      var response = await videreOpenAPIClient.GetPricesAsync(
        new Generated.PriceRequest
        {
          Collection = new Generated.Collection { Ids = collectionIds.ToArray() },
          Date = Generated.Date.Latest
        },
        cancellationToken);
      var prices = (response.Data ?? [])
        .Where(price => price.Id > 0)
        .ToDictionary(
          price => price.Id,
          price => new ViderePriceResult(
            price.Id,
            price.Price_date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Convert.ToDecimal(price.Sell_price, CultureInfo.InvariantCulture),
            price.Source));
      var missing = response.Meta?.Missing_ids?.ToArray() ?? collectionIds.Where(id => !prices.ContainsKey(id)).ToArray();
      return new ViderePricesResult(prices, missing);
    }
    catch (Generated.VidereOpenAPIException<Generated.Error> ex) when (IsNoResults(ex.Result))
    {
      return new ViderePricesResult(new Dictionary<int, ViderePriceResult>(), collectionIds);
    }
    catch (Generated.VidereOpenAPIException ex)
    {
      throw ToTrackerException("Videre prices API request failed", ex);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
      throw new VidereAPIException("Videre prices API request failed", innerException: ex);
    }
  }

  internal async Task<IReadOnlyList<ViderePriceResult>> GetPriceHistoryAsync(
    int catalogId,
    string? from,
    string? to,
    int limit,
    int offset,
    CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await videreOpenAPIClient.GetPriceHistoryAsync(
        catalogId,
        ParseDate(from),
        ParseDate(to),
        limit,
        offset,
        cancellationToken);
      return (response.Data ?? [])
        .Where(price => price.Id == catalogId)
        .OrderBy(price => price.Price_date)
        .Select(price => new ViderePriceResult(
          price.Id,
          price.Price_date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
          Convert.ToDecimal(price.Sell_price, CultureInfo.InvariantCulture),
          price.Source))
        .ToArray();
    }
    catch (Generated.VidereOpenAPIException<Generated.Error> ex) when (IsNoResults(ex.Result))
    {
      return [];
    }
    catch (Generated.VidereOpenAPIException ex)
    {
      throw ToTrackerException("Videre price history request failed", ex);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
      throw new VidereAPIException("Videre price history request failed", innerException: ex);
    }
  }

  internal async Task<VidereCardDetailResult?> GetCardDetailsAsync(
    int catalogId,
    CancellationToken cancellationToken = default)
  {
    try
    {
      var response = await videreOpenAPIClient.GetCardAsync(catalogId, cancellationToken);
      var card = response.Data?.FirstOrDefault(row => row.Id == catalogId) ?? response.Data?.FirstOrDefault();
      return card is null || card.Id <= 0 ? null : new VidereCardDetailResult(
        card.Id,
        card.Name,
        card.Printed_name,
        card.Display_name,
        NormalizeOptionalText(card.Set_code)?.ToUpperInvariant(),
        card.Set_name,
        card.Collector_number,
        card.Rarity is null ? null : GetEnumWireValue(card.Rarity.Value),
        NormalizeOptionalText(card.Mana_cost),
        card.Mana_value,
        NormalizeOptionalText(card.Type_line),
        NormalizeCardText(card.Oracle_text ?? card.Flavor_text),
        NormalizeCardText(card.Flavor_text),
        NormalizeColors(card.Colors),
        card.Image_url?.ToString(),
        NormalizeOptionalText(card.Power),
        NormalizeOptionalText(card.Toughness),
        NormalizeOptionalText(card.Loyalty),
        NormalizeOptionalText(card.Defense),
        NormalizeOptionalText(card.Artist),
        NormalizeOptionalText(card.Promo_label));
    }
    catch (Generated.VidereOpenAPIException<Generated.Error> ex) when (IsNoResults(ex.Result))
    {
      return null;
    }
    catch (Generated.VidereOpenAPIException ex)
    {
      throw ToTrackerException("Videre card details request failed", ex);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
    {
      throw new VidereAPIException("Videre card details request failed", innerException: ex);
    }
  }

  private static Generated.CardCollection CreateCollection(IReadOnlyCollection<int> ids) => new()
  {
    Ids = ids.ToArray(),
    Mode = Generated.CardCollectionMode.Only,
    Match = Generated.CardCollectionMatch.Prints
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

  private static bool IsNoResults(Generated.Error error) =>
    error.Object == Generated.ErrorObject.Error &&
    string.Equals(error.Message, "No results found.", StringComparison.OrdinalIgnoreCase);

  private static VidereAPIException ToTrackerException(
    string message,
    Generated.VidereOpenAPIException exception) => new(
      message,
      exception.StatusCode,
      exception.Response,
      exception.InnerException ?? exception);

  private static List<CardSearchResultDTO> MapCards(
    ICollection<Generated.Card>? cards,
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
        SetCode = NormalizeOptionalText(card.Set_code)?.ToUpperInvariant() ?? "",
        Name = card.Name,
        Type = string.IsNullOrWhiteSpace(card.Type_line) ? "Card" : card.Type_line,
        Text = NormalizeCardText(card.Oracle_text ?? card.Flavor_text),
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

  private static List<string> NormalizeColors(
    ICollection<Generated.Colors2>? colors) => colors is null
    ? []
    : VidereCardColors.Normalize(colors.Select(GetEnumWireValue)).ToList();

  internal static string GetEnumWireValue<T>(T value) where T : struct, Enum
  {
    var member = typeof(T).GetMember(value.ToString()).Single();
    return member.GetCustomAttribute<EnumMemberAttribute>()?.Value ?? value.ToString();
  }

  private static DateTimeOffset? ParseDate(string? value)
  {
    if (string.IsNullOrWhiteSpace(value)) return null;
    if (DateTimeOffset.TryParseExact(
      value.Trim(),
      "yyyy-MM-dd",
      CultureInfo.InvariantCulture,
      DateTimeStyles.AssumeUniversal,
      out var parsed))
    {
      return parsed;
    }

    throw new VidereAPIException($"Invalid Videre API date '{value}'. Expected yyyy-MM-dd.");
  }

  // Product-filtered card searches return Product rows, although the upstream
  // operation currently documents Card rows for every search query.
  private sealed class VidereProductSearchResponse
  {
    [System.Text.Json.Serialization.JsonPropertyName("data")]
    public List<Generated.Product>? Data { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("meta")]
    public Generated.ListMeta? Meta { get; set; }
  }

  internal static DateTimeOffset GetPriceCacheExpiration(DateTimeOffset now)
  {
    var timeZone = GetPriceTimeZone();
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

  private static TimeZoneInfo GetPriceTimeZone()
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
