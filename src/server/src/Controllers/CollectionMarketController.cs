/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using MTGOSDK.Core.Logging;

using Tracker.Controllers.Base;
using Tracker.Controllers.Models.Collection;
using Tracker.Services.Videre;


namespace Tracker.Controllers;

[ApiController]
[Route("api/collection")]
public sealed class CollectionMarketController(
  VidereAPIClient videreAPIClient) : APIController
{
  private static readonly object s_collectionPriceHistoryCacheSync = new();
  private static readonly Dictionary<string, CollectionPriceHistoryDTO> s_collectionPriceHistoryCache = new();
  private static DateTimeOffset s_collectionPriceHistoryCacheExpiresAtUtc;
  private static readonly ConcurrentDictionary<int, CollectionCardDetailDTO> s_collectionCardDetailCache = new();
  [HttpGet("prices/{id:int}/history")]
  [ProducesResponseType(typeof(CollectionPriceHistoryDTO), StatusCodes.Status200OK)]
  public async Task<ActionResult<CollectionPriceHistoryDTO>> GetCollectionPriceHistory(
    int id,
    [FromQuery] string? from,
    [FromQuery] string? to,
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
    var cacheKey = $"{id}|{from}|{to}|{boundedLimit}|{boundedOffset}";
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

    if (s_collectionCardDetailCache.TryGetValue(id, out var cached))
    {
      return Ok(cached);
    }

    try
    {
      var detail = await FetchCollectionCardDetailAsync(id, cancellationToken);
      if (detail is null)
      {
        return NotFound(new { error = $"Card metadata for catalog ID {id} was not found." });
      }

      s_collectionCardDetailCache[id] = detail;
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
    string? from,
    string? to,
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
    if (card is null)
    {
      return null;
    }

    return new CollectionCardDetailDTO
    {
      CatalogId = card.Id,
      Name = string.IsNullOrWhiteSpace(card.DisplayName)
        ? card.Name
        : card.DisplayName,
      CanonicalName = card.Name,
      PrintedName = card.PrintedName,
      SetCode = card.SetCode ?? "",
      SetName = card.SetName,
      CollectorNumber = card.CollectorNumber,
      Rarity = card.Rarity,
      ManaCost = card.ManaCost,
      ManaValue = card.ManaValue,
      TypeLine = card.TypeLine ?? "Card",
      OracleText = card.OracleText ?? "",
      FlavorText = card.FlavorText,
      Colors = card.Colors?.ToList() ?? [],
      ImageUrl = string.IsNullOrWhiteSpace(card.ImageUrl)
        ? $"https://r2.videreproject.com/cards/{card.Id}-300px.png"
        : card.ImageUrl,
      Power = card.Power,
      Toughness = card.Toughness,
      Loyalty = card.Loyalty,
      Defense = card.Defense,
      Artist = card.Artist,
      PromoLabel = card.PromoLabel
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
