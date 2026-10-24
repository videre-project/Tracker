/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using MTGOSDK.Core.Logging;

using MTGOTracker.Controllers.Base;
using MTGOTracker.Controllers.Models.Collection;
using MTGOTracker.Services.Videre;


namespace MTGOTracker.Controllers;

[ApiController]
[Route("api/collection")]
public sealed class CollectionMarketController(
  VidereAPIClient videreAPIClient) : APIController
{
  private static readonly object s_collectionPriceHistoryCacheSync = new();
  private static readonly Dictionary<string, CollectionPriceHistoryDTO> s_collectionPriceHistoryCache = new();
  private static DateTimeOffset s_collectionPriceHistoryCacheExpiresAtUtc;

  [HttpGet("prices/{id:int}/history")]
  [ProducesResponseType(typeof(CollectionPriceHistoryDTO), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  public async Task<ActionResult<CollectionPriceHistoryDTO>> GetCollectionPriceHistory(
    int id,
    [FromQuery] DateTimeOffset? from,
    [FromQuery] DateTimeOffset? to,
    [FromQuery] int? limit,
    [FromQuery] int? offset,
    CancellationToken cancellationToken)
  {
    if (id <= 0)
    {
      return BadRequest(new { error = "Catalog ID must be positive." });
    }

    var boundedLimit = Math.Clamp(limit ?? 120, 1, 365);
    var boundedOffset = Math.Max(0, offset ?? 0);
    var cacheKey = $"{id}|{from:yyyy-MM-dd}|{to:yyyy-MM-dd}|{boundedLimit}|{boundedOffset}";
    var cached = GetCachedCollectionPriceHistory(cacheKey);
    if (cached is not null)
    {
      return Ok(cached);
    }

    try
    {
      var history = await FetchCollectionPriceHistoryAsync(
        id,
        from,
        to,
        boundedLimit,
        boundedOffset,
        cancellationToken);
      SetCachedCollectionPriceHistory(cacheKey, history);
      return Ok(history);
    }
    catch (VidereAPIException ex)
    {
      Log.Warning(ex, "Failed to fetch collection price history for catalog ID {CatalogId}", id);
      return StatusCode(StatusCodes.Status502BadGateway, new
      {
        error = "Videre prices API request failed",
        message = ex.Message
      });
    }
  }

  /// <summary>
  /// Get deckbuilding/search metadata for one MTGO card catalog ID.
  /// </summary>
  [HttpGet("cards/{id:int}/details")]
  [ProducesResponseType(typeof(CollectionCardDetailDTO), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<CollectionCardDetailDTO>> GetCollectionCardDetails(
    int id,
    CancellationToken cancellationToken)
  {
    if (id <= 0)
    {
      return BadRequest(new { error = "Catalog ID must be positive." });
    }

    try
    {
      var detail = await FetchCollectionCardDetailAsync(id, cancellationToken);
      if (detail is null)
      {
        return NotFound(new { error = $"Card metadata for catalog ID {id} was not found." });
      }

      return Ok(detail);
    }
    catch (VidereAPIException ex)
    {
      Log.Warning(ex, "Failed to fetch collection card details for catalog ID {CatalogId}", id);
      return StatusCode(StatusCodes.Status502BadGateway, new
      {
        error = "Videre cards API request failed",
        message = ex.Message
      });
    }
  }

  private async Task<CollectionPriceHistoryDTO> FetchCollectionPriceHistoryAsync(
    int catalogId,
    DateTimeOffset? from,
    DateTimeOffset? to,
    int limit,
    int offset,
    CancellationToken cancellationToken)
  {
    var priceHistory = await videreAPIClient.GetPriceHistoryAsync(
      catalogId,
      from,
      to,
      limit,
      offset,
      cancellationToken);
    var prices = priceHistory
      .Select(price => new CollectionPricePointDTO
      {
        Date = price.PriceDate ?? "",
        Price = price.SellPrice,
        Source = price.Source
      })
      .ToArray();

    return new CollectionPriceHistoryDTO
    {
      CatalogId = catalogId,
      PriceCacheExpiresAt = VidereAPIClient.GetPriceCacheExpiration(DateTimeOffset.UtcNow),
      Prices = prices
    };
  }

  private async Task<CollectionCardDetailDTO?> FetchCollectionCardDetailAsync(
    int catalogId,
    CancellationToken cancellationToken)
  {
    var card = await videreAPIClient.GetCardDetailsAsync(catalogId, cancellationToken);
    if (card is null) return null;
    var canonicalName = card.Name ?? card.Display_name ?? "";
    var otherFace = card.Faces?.FirstOrDefault(face =>
      face.Source_catalog_id is > 0 && face.Source_catalog_id != card.Id);

    return new CollectionCardDetailDTO
    {
      CatalogId = card.Id,
      Name = string.IsNullOrWhiteSpace(card.Display_name) ? canonicalName : card.Display_name,
      CanonicalName = canonicalName,
      PrintedName = card.Printed_name,
      SetCode = card.Set_code?.Trim().ToUpperInvariant() ?? "",
      SetName = card.Set_name,
      CollectorNumber = card.Collector_number,
      Rarity = card.Rarity.HasValue ? VidereAPIClient.GetEnumWireValue(card.Rarity.Value) : null,
      ManaCost = VidereAPIClient.NormalizeOptionalText(card.Mana_cost),
      ManaValue = card.Mana_value,
      TypeLine = VidereAPIClient.NormalizeOptionalText(card.Type_line) ?? "Card",
      OracleText = VidereAPIClient.NormalizeCardText(card.Oracle_text ?? card.Flavor_text),
      FlavorText = VidereAPIClient.NormalizeCardText(card.Flavor_text),
      Colors = VidereAPIClient.NormalizeColors(card.Colors),
      ImageUrl = card.Image_url?.ToString() ?? $"https://r2.videreproject.com/cards/{card.Id}-300px.png",
      Power = VidereAPIClient.NormalizeOptionalText(card.Power),
      Toughness = VidereAPIClient.NormalizeOptionalText(card.Toughness),
      Loyalty = VidereAPIClient.NormalizeOptionalText(card.Loyalty),
      Defense = VidereAPIClient.NormalizeOptionalText(card.Defense),
      Artist = VidereAPIClient.NormalizeOptionalText(card.Artist),
      PromoLabel = VidereAPIClient.NormalizeOptionalText(card.Promo_label),
      OtherFaceCatalogId = otherFace?.Source_catalog_id
    };
  }

  private static CollectionPriceHistoryDTO? GetCachedCollectionPriceHistory(string cacheKey)
  {
    var now = DateTimeOffset.UtcNow;
    lock (s_collectionPriceHistoryCacheSync)
    {
      if (now >= s_collectionPriceHistoryCacheExpiresAtUtc)
      {
        s_collectionPriceHistoryCache.Clear();
        s_collectionPriceHistoryCacheExpiresAtUtc = VidereAPIClient.GetPriceCacheExpiration(now);
        return null;
      }

      return s_collectionPriceHistoryCache.TryGetValue(cacheKey, out var cached)
        ? cached
        : null;
    }
  }

  private static void SetCachedCollectionPriceHistory(
    string cacheKey,
    CollectionPriceHistoryDTO history)
  {
    lock (s_collectionPriceHistoryCacheSync)
    {
      var now = DateTimeOffset.UtcNow;
      if (now >= s_collectionPriceHistoryCacheExpiresAtUtc)
      {
        s_collectionPriceHistoryCache.Clear();
        s_collectionPriceHistoryCacheExpiresAtUtc = VidereAPIClient.GetPriceCacheExpiration(now);
      }

      s_collectionPriceHistoryCache[cacheKey] = history;
    }
  }
}
