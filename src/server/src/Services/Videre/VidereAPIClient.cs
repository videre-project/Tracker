/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;

using MTGOSDK.API.Collection;

using MTGOTracker.Controllers.Models.Decks;


namespace MTGOTracker.Services.Videre;

public sealed class VidereAPIClient
{
  private const int MaxCollectionIds = 10_000;
  private static readonly ConcurrentDictionary<int, Generated.Card> s_cards = new();
  private readonly Generated.VidereOpenAPIClient client;

  internal VidereAPIClient(Generated.VidereOpenAPIClient client) => this.client = client;

  internal async Task<List<CardSearchResultDTO>> SearchCardsAsync(
    string query, int limit, CancellationToken cancellationToken = default)
  {
    int boundedLimit = Math.Clamp(limit, 1, 50);
    var response = await RequestAsync(() => client.SearchCardsAsync(
      q: query.Trim(), unique: Generated.Unique.Cards, order: Generated.Order.Rank,
      limit: Math.Min(boundedLimit * 2, 100), cancellationToken: cancellationToken), cancellationToken);
    var results = MapCards(response?.Data, boundedLimit);
    var multifaceIds = response?.Data?.Where(card => card.Is_multiface && !card.Is_split)
      .Select(card => card.Id).ToHashSet() ?? [];
    await Task.WhenAll(results.Where(card => multifaceIds.Contains(card.MtgoId)).Select(async card =>
    {
      var face = await GetOtherFaceCatalogIdAsync(card.MtgoId, cancellationToken);
      card.OtherFaceCatalogId = face?.catalogId;
    }));
    return results;
  }

  internal async Task<(int catalogId, string name)?> GetOtherFaceCatalogIdAsync(
    int catalogId, CancellationToken cancellationToken = default)
  {
    if (catalogId <= 0) return null;
    try
    {
      var card = await GetCardDetailsAsync(catalogId, cancellationToken);
      if (card?.Is_split == true) return null;
      var face = card?.Faces?.FirstOrDefault(face =>
        face.Source_catalog_id is > 0 && face.Source_catalog_id != card.Id);
      return face?.Source_catalog_id is int id ? (id, face.Name ?? "") : null;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
    catch (Exception exception)
    {
      MTGOSDK.Core.Logging.Log.Warning(exception, "Failed to resolve other face catalog ID for catalogId={CatalogId}", catalogId);
    }
    return null;
  }

  internal async Task<IReadOnlyDictionary<int, VidereCatalogMetadataResult>> GetCatalogMetadataAsync(
    IReadOnlyCollection<int> catalogIds, CancellationToken cancellationToken = default)
  {
    var cardsRequest = SearchCollectionAsync(catalogIds, null, cancellationToken);
    var productsRequest = SearchCollectionAsync(catalogIds, "is:product", cancellationToken);
    await Task.WhenAll(cardsRequest, productsRequest);
    var metadata = new Dictionary<int, VidereCatalogMetadataResult>();
    foreach (var card in (await cardsRequest).Values)
    {
      string name = string.IsNullOrWhiteSpace(card.Display_name) ? card.Name ?? $"Catalog {card.Id}" : card.Display_name;
      // Split cards are multiface in the catalog, but both halves share one image.
      metadata[card.Id] = new(card.Id, name,
        NormalizeOptionalText(card.Set_code)?.ToUpperInvariant(),
        card.Rarity.HasValue ? GetEnumWireValue(card.Rarity.Value) : null,
        null, card.Image_url?.ToString(), card.Mana_value, NormalizeColors(card.Colors),
        NormalizeOptionalText(card.Type_line), card.Is_split == true ? false : card.Is_multiface);
    }
    foreach (var product in (await productsRequest).Values)
    {
      metadata[product.Id] = new(product.Id,
        string.IsNullOrWhiteSpace(product.Name) ? $"Catalog {product.Id}" : product.Name,
        NormalizeOptionalText(product.Set_code)?.ToUpperInvariant(), null,
        NormalizeOptionalText(product.Object_type), product.Image_url?.ToString());
    }
    return metadata;
  }

  internal async Task<IReadOnlyDictionary<int, Generated.CatalogSearchItem>> SearchCollectionAsync(
    IReadOnlyCollection<int> collectionIds, string? query, CancellationToken cancellationToken = default)
  {
    const int pageSize = 500;
    var results = new Dictionary<int, Generated.CatalogSearchItem>();
    foreach (int[] chunk in collectionIds.Where(id => id > 0).Distinct().Chunk(MaxCollectionIds))
    {
      var body = new Generated.CardSearchRequest
      {
        Collection = new Generated.CardCollection { Ids = chunk }
      };
      int offset = 0;
      while (true)
      {
        var response = await RequestAsync(() => client.SearchCardsWithCollectionAsync(
          q: query, unique: Generated.Unique2.Prints, limit: pageSize, offset: offset, body: body,
          cancellationToken: cancellationToken), cancellationToken);
        var page = response?.Data ?? [];
        foreach (var row in page)
          if (row.Id > 0) results[row.Id] = row;
        if (response?.Meta?.Has_more != true || page.Count == 0 ||
            response.Meta.Next_offset is not int next || next <= offset) break;
        offset = next;
      }
    }
    return results;
  }

  internal async Task<ViderePricesResult> GetLatestPricesAsync(
    IReadOnlyCollection<int> collectionIds, CancellationToken cancellationToken = default)
  {
    var prices = new Dictionary<int, ViderePriceResult>();
    var missing = new HashSet<int>();
    foreach (int[] chunk in collectionIds.Where(id => id > 0).Distinct().Chunk(MaxCollectionIds))
    {
      var response = await RequestAsync(() => client.GetPricesAsync(new Generated.PriceRequest
      {
        Collection = new Generated.Collection { Ids = chunk }
      }, cancellationToken), cancellationToken);
      foreach (var price in response?.Data ?? [])
        if (price.Id > 0) prices[price.Id] = new ViderePriceResult(price.Id,
          price.Price_date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
          Convert.ToDecimal(price.Sell_price, CultureInfo.InvariantCulture), price.Source);
      missing.UnionWith(response?.Meta?.Missing_ids ?? chunk.Where(id => !prices.ContainsKey(id)).ToArray());
    }
    return new(prices, missing);
  }

  internal async Task<IReadOnlyList<ViderePriceResult>> GetPriceHistoryAsync(
    int catalogId, DateTimeOffset? from, DateTimeOffset? to, int limit, int offset, CancellationToken cancellationToken = default)
  {
    var response = await RequestAsync(() => client.GetPriceHistoryAsync(
      catalogId, from, to, limit, offset, cancellationToken), cancellationToken);
    return (response?.Data ?? []).Where(price => price.Id == catalogId).OrderBy(price => price.Price_date)
      .Select(price => new ViderePriceResult(price.Id, price.Price_date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Convert.ToDecimal(price.Sell_price, CultureInfo.InvariantCulture), price.Source)).ToArray();
  }

  internal async Task<Generated.Card?> GetCardDetailsAsync(
    int catalogId, CancellationToken cancellationToken = default) =>
    await GetCardAsync(catalogId, cancellationToken) ??
      await FindNonFoilCardAsync(catalogId, null, cancellationToken);

  private async Task<Generated.Card?> GetCardAsync(
    int catalogId, CancellationToken cancellationToken = default)
  {
    if (catalogId <= 0) return null;
    if (s_cards.TryGetValue(catalogId, out var cached)) return cached;
    var response = await RequestAsync(() => client.GetCardAsync(catalogId, cancellationToken), cancellationToken);
    var card = response?.Data?.FirstOrDefault(row => row.Id == catalogId);
    // Unresolved back faces must be retried when the catalog is updated.
    if (card is not null && (!card.Is_multiface || card.Is_split ||
        card.Faces?.Any(face => face.Source_catalog_id is > 0 && face.Source_catalog_id != card.Id) == true))
      s_cards[catalogId] = card;
    return card;
  }

  /// <summary>
  /// Finds the non-foil catalog entry used by MTGO foil clones, when the
  /// preceding catalog ID is confirmed to represent the same print.
  /// </summary>
  internal async Task<int?> FindNonFoilCatalogIdAsync(
    int catalogId, CancellationToken cancellationToken = default) =>
    (await FindNonFoilCardAsync(catalogId,
      await GetCardAsync(catalogId, cancellationToken), cancellationToken))?.Id;

  private async Task<Generated.Card?> FindNonFoilCardAsync(
    int catalogId, Generated.Card? foil, CancellationToken cancellationToken)
  {
    if (catalogId <= 1) return null;

    var nonFoil = await GetCardAsync(catalogId - 1, cancellationToken);
    if (nonFoil is null) return null;

    if (foil is not null)
    {
      return IsSamePrint(foil, nonFoil) ? nonFoil : null;
    }

    try
    {
      var mtgoCard = CollectionManager.GetCard(catalogId);
      return HasSameValue(mtgoCard.Name, nonFoil.Name) &&
        HasSameValue(mtgoCard.Set?.Code, nonFoil.Set_code) &&
        HasSameValue(mtgoCard.CollectorNumber.ToString(CultureInfo.InvariantCulture), nonFoil.Collector_number)
          ? nonFoil : null;
    }
    catch (Exception exception)
    {
      MTGOSDK.Core.Logging.Log.Debug(exception, "Cannot verify foil printing for catalog ID {CatalogId}", catalogId);
      return null;
    }
  }

  private static bool IsSamePrint(Generated.Card first, Generated.Card second) =>
    first.Oracle_id != Guid.Empty && first.Oracle_id == second.Oracle_id &&
    HasSameValue(first.Set_code, second.Set_code) &&
    HasSameValue(first.Collector_number, second.Collector_number);

  private static bool HasSameValue(string? first, string? second) =>
    !string.IsNullOrWhiteSpace(first) &&
    !string.IsNullOrWhiteSpace(second) &&
    string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);

  // Normalize API failures once; only an explicit no-results response is an empty result.
  private static async Task<T?> RequestAsync<T>(Func<Task<T>> request, CancellationToken cancellationToken) where T : class
  {
    try { return await request(); }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
    catch (Generated.VidereOpenAPIException<Generated.Error> exception) when (
      exception.StatusCode == 400 &&
      exception.Result.Object == Generated.ErrorObject.Error &&
      string.Equals(exception.Result.Message, "No results found.", StringComparison.OrdinalIgnoreCase))
    {
      return null;
    }
    catch (Generated.VidereOpenAPIException exception)
    {
      throw new VidereAPIException("Videre API request failed", exception.StatusCode, exception.Response, exception);
    }
    catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
    {
      throw new VidereAPIException("Videre API request failed", innerException: exception);
    }
  }

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
        Cmc = Math.Max(0, (int)Math.Floor(card.Mana_value ?? 0)),
        Rarity = card.Rarity.HasValue ? GetEnumWireValue(card.Rarity.Value) : "common",
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

  internal static string NormalizeCardText(string? value) => string.IsNullOrWhiteSpace(value)
    ? ""
    : value
      .Replace("@i", "", StringComparison.Ordinal)
      .Replace("@-", "", StringComparison.Ordinal)
      .Replace("\r\n", "\n", StringComparison.Ordinal)
      .Replace("\r", "\n", StringComparison.Ordinal)
      .Trim();

  internal static string? NormalizeOptionalText(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value.Trim();

  internal static List<string> NormalizeColors(
    ICollection<Generated.CardColor>? colors) => colors is null
    ? []
    : VidereCardColors.Normalize(colors.Select(GetEnumWireValue)).ToList();

  internal static string GetEnumWireValue<T>(T value) where T : struct, Enum
  {
    var member = typeof(T).GetMember(value.ToString()).Single();
    return member.GetCustomAttribute<EnumMemberAttribute>()?.Value ?? value.ToString();
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
