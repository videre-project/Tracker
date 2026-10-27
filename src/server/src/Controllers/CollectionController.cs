/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using MTGOSDK.API.Collection;
using MTGOSDK.Core.Logging;

using MTGOTracker.Controllers.Base;
using MTGOTracker.Controllers.Models.Collection;
using MTGOTracker.Services.MTGO;
using MTGOTracker.Services.MTGO.Collection;
using MTGOTracker.Services.Videre;


using CatalogSearchItem = MTGOTracker.Services.Videre.Generated.CatalogSearchItem;

namespace MTGOTracker.Controllers;

/// <summary>
/// Collection holdings, binders, and catalog search.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CollectionController(
  IClientAPIProvider clientProvider,
  VidereAPIClient videreAPIClient,
  CollectionStateFeed stateFeed,
  ClientStateMonitor clientMonitor) : APIController
{
  private static readonly object s_collectionSnapshotCacheSync = new();
  private static readonly SemaphoreSlim s_collectionSnapshotCacheLock = new(1, 1);
  private static string? s_collectionSnapshotCacheKey;
  private static CollectionSnapshotDTO? s_collectionSnapshotCache;
  private static DateTimeOffset s_collectionSnapshotCacheExpiresAtUtc;
  private static readonly object s_collectionPriceCacheSync = new();
  private static readonly Dictionary<int, ViderePriceResult> s_collectionPriceCache = new();
  private static readonly HashSet<int> s_collectionPriceMissingCache = [];
  private static DateTimeOffset s_collectionPriceCacheExpiresAtUtc;

  /// <summary>
  /// Get the user's MTGO collection as a compact card/quantity snapshot.
  /// </summary>
  [HttpGet("cards")] // GET /api/collection/cards
  [ProducesResponseType(typeof(CollectionSnapshotDTO), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<ActionResult<CollectionSnapshotDTO>> GetCollectionCards(
    CancellationToken cancellationToken)
  {
    if (!clientProvider.IsReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new
      {
        error = "MTGO client not ready",
        hint = "Please wait for the client to fully initialize and log in"
      });
    }

    try
    {
      var collection = CollectionManager.Collection;
      if (collection == null)
      {
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
          error = "Collection not loaded",
          hint = "Please wait for MTGO to finish loading your collection"
        });
      }

      var collectionHash = TryGetCollectionHash(collection);
      var cachedSnapshot = GetCachedCollectionSnapshot(collectionHash);
      if (cachedSnapshot is not null)
      {
        return Ok(cachedSnapshot);
      }

      var stopwatch = Stopwatch.StartNew();
      var frozenCollection = collection.GetFrozenCollection;
      stopwatch.Stop();

      var collectionItems = frozenCollection
        .Where(card => card.Id > 0 && card.Quantity > 0)
        .ToArray();
      var cacheKey = string.IsNullOrWhiteSpace(collectionHash)
        ? BuildCollectionItemsCacheKey(collectionItems)
        : collectionHash;

      cachedSnapshot = GetCachedCollectionSnapshot(cacheKey);
      if (cachedSnapshot is not null)
      {
        return Ok(cachedSnapshot);
      }

      await s_collectionSnapshotCacheLock.WaitAsync(cancellationToken);
      try
      {
        cachedSnapshot = GetCachedCollectionSnapshot(cacheKey);
        if (cachedSnapshot is not null)
        {
          return Ok(cachedSnapshot);
        }

        var snapshot = await BuildCollectionSnapshotAsync(
          collection,
          collectionItems,
          cacheKey,
          stopwatch.Elapsed.TotalMilliseconds,
          cancellationToken);
        SetCachedCollectionSnapshot(cacheKey, snapshot);
        return Ok(snapshot);
      }
      finally
      {
        s_collectionSnapshotCacheLock.Release();
      }
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Failed to serialize MTGO collection");
      return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
    }
  }

  /// <summary>
  /// Streams holdings versions to invalidate collection snapshots independently
  /// of price expiry.
  /// </summary>
  [HttpGet("watch")]
  [Produces("application/x-ndjson")]
  public async Task<IActionResult> Watch()
  {
    if (!clientProvider.IsReady || clientProvider.CurrentUser is not { } user)
      return StatusCode(StatusCodes.Status503ServiceUnavailable);
    using var cancellation = BeginNDJSONStream(clientMonitor.Token);
    long version = 0;
    try
    {
      while (!cancellation.IsCancellationRequested)
      {
        version = await stateFeed.WaitForChangeAsync(user.Id, version, cancellation.Token);
        await StreamResponse(new[] { version }, cancellation.Token);
      }
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    return new EmptyResult();
  }

  /// <summary>
  /// List the user's binders and wish list.
  /// </summary>
  [HttpGet("binders")]
  [ProducesResponseType(typeof(CollectionBinderDTO[]), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public ActionResult<CollectionBinderDTO[]> GetBinders()
  {
    if (!clientProvider.IsReady)
      return StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = "Connect MTGO to view your binders." });

    return Ok(ReadBinders().Select(binder => new CollectionBinderDTO
    {
      Id = binder.NetDeckId,
      Name = binder.Name,
      IsWishList = binder.IsWishList,
    }).OrderBy(binder => binder.IsWishList).ThenBy(binder => binder.Name).ToArray());
  }

  /// <summary>
  /// Read the cards and quantities in an existing binder.
  /// </summary>
  [HttpGet("binders/{id:int}/cards")]
  [ProducesResponseType(typeof(CollectionCardDTO[]), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public ActionResult<CollectionCardDTO[]> GetBinderCards(int id)
  {
    if (!clientProvider.IsReady)
      return StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = "Connect MTGO to view your binders." });

    Binder? binder = ReadBinders().FirstOrDefault(binder => binder.NetDeckId == id);
    if (binder == null) return NotFound(new { error = "Binder not found." });

    return Ok(binder.GetFrozenCollection
      .Where(card => card.Id > 0 && card.Quantity > 0)
      .GroupBy(card => card.Id)
      .Select(cards => new CollectionCardDTO
      {
        CatalogId = cards.Key,
        Name = cards.First().Name,
        Quantity = cards.Sum(card => card.Quantity),
      }).OrderBy(card => card.Name).ToArray());
  }

  private static IEnumerable<Binder> ReadBinders() => CollectionManager.Binders
    .Append(CollectionManager.WishList)
    .Where(binder => !binder.IsMegaBinder && binder.NetDeckId > 0)
    .DistinctBy(binder => binder.NetDeckId);

  /// <summary>
  /// Search the user's MTGO collection with Videre card query syntax.
  /// </summary>
  [HttpPost("cards/search")] // POST /api/collection/cards/search
  [ProducesResponseType(typeof(CollectionSearchResultDTO), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status502BadGateway)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<ActionResult<CollectionSearchResultDTO>> SearchCollectionCards(
    [FromBody] CollectionSearchRequestDTO? request,
    CancellationToken cancellationToken)
  {
    var query = request?.Query?.Trim();
    if (string.IsNullOrWhiteSpace(query))
    {
      return Ok(new CollectionSearchResultDTO
      {
        Query = "",
        CatalogIds = []
      });
    }

    if (!clientProvider.IsReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new
      {
        error = "MTGO client not ready",
        hint = "Please wait for the client to fully initialize and log in"
      });
    }

    try
    {
      var collection = CollectionManager.Collection;
      if (collection == null)
      {
        return StatusCode(StatusCodes.Status503ServiceUnavailable, new
        {
          error = "Collection not loaded",
          hint = "Please wait for MTGO to finish loading your collection"
        });
      }

      var collectionIds = collection.GetFrozenCollection
        .Where(item => item.Id > 0 && item.Quantity > 0)
        .Select(item => item.Id)
        .Distinct()
        .ToArray();

      if (collectionIds.Length == 0)
      {
        return Ok(new CollectionSearchResultDTO
        {
          Query = query,
          CatalogIds = []
        });
      }

      var results = await videreAPIClient.SearchCollectionAsync(collectionIds, query, cancellationToken);

      return Ok(new CollectionSearchResultDTO
      {
        Query = query,
        CatalogIds = results.Keys
          .OrderBy(id => id)
          .ToArray()
      });
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (VidereAPIException ex)
    {
      return StatusCode(StatusCodes.Status502BadGateway, new
      {
        error = "Videre collection search request failed",
        message = ex.Message
      });
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Failed to search MTGO collection");
      return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
    }
  }

  private async Task<CollectionSnapshotDTO> BuildCollectionSnapshotAsync(
    MTGOSDK.API.Collection.Collection collection,
    IReadOnlyCollection<CardQuantityPair> collectionItems,
    string cacheKey,
    double elapsedMilliseconds,
    CancellationToken cancellationToken)
  {
    var productsById = await GetCollectionProductsAsync(collectionItems, cancellationToken);
    var pricesById = await GetCollectionPricesAsync(collectionItems, cancellationToken);
    var cards = new List<CollectionCardDTO>(collectionItems.Count);
    var products = new List<CollectionProductDTO>();

    foreach (var item in collectionItems)
    {
      if (productsById.TryGetValue(item.Id, out var product))
      {
        pricesById.TryGetValue(item.Id, out var productPrice);
        string? imageUrl = product.Image_url?.ToString();
        products.Add(new CollectionProductDTO
        {
          CatalogId = item.Id,
          Name = string.IsNullOrWhiteSpace(product.Name)
            ? GetItemName(item.Id, item.Name)
            : product.Name,
          Quantity = item.Quantity,
          Description = product.Description,
          SetCode = product.Set_code,
          SetName = product.Set_name,
          ObjectType = product.Object_type,
          ImageUrl = string.IsNullOrWhiteSpace(imageUrl)
            ? $"https://r2.videreproject.com/products/{item.Id}-300px.png"
            : imageUrl,
          IsTradable = product.Is_tradable,
          Price = productPrice?.SellPrice,
          PriceDate = productPrice?.PriceDate,
          PriceSource = productPrice?.Source
        });
        continue;
      }

      pricesById.TryGetValue(item.Id, out var cardPrice);
      cards.Add(new CollectionCardDTO
      {
        CatalogId = item.Id,
        Name = GetItemName(item.Id, item.Name),
        Quantity = item.Quantity,
        Price = cardPrice?.SellPrice,
        PriceDate = cardPrice?.PriceDate,
        PriceSource = cardPrice?.Source
      });
    }

    cards = cards
      .OrderBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
      .ThenBy(card => card.CatalogId)
      .ToList();

    products = products
      .OrderBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
      .ThenBy(product => product.CatalogId)
      .ToList();

    return new CollectionSnapshotDTO
    {
      Hash = cacheKey,
      ItemCount = collection.ItemCount,
      UniqueCount = cards.Count + products.Count,
      TotalQuantity = cards.Sum(card => (long)card.Quantity) +
        products.Sum(product => (long)product.Quantity),
      Timestamp = collection.Timestamp,
      PriceCacheExpiresAt = VidereAPIClient.GetPriceCacheExpiration(DateTimeOffset.UtcNow),
      ElapsedMilliseconds = elapsedMilliseconds,
      Cards = cards.ToArray(),
      Products = products.ToArray()
    };
  }

  private static CollectionSnapshotDTO? GetCachedCollectionSnapshot(string? cacheKey)
  {
    if (string.IsNullOrWhiteSpace(cacheKey))
    {
      return null;
    }

    lock (s_collectionSnapshotCacheSync)
    {
      if (s_collectionSnapshotCache is not null &&
        DateTimeOffset.UtcNow >= s_collectionSnapshotCacheExpiresAtUtc)
      {
        s_collectionSnapshotCacheKey = null;
        s_collectionSnapshotCache = null;
        s_collectionSnapshotCacheExpiresAtUtc = DateTimeOffset.MinValue;
        return null;
      }

      return string.Equals(s_collectionSnapshotCacheKey, cacheKey, StringComparison.Ordinal)
        ? s_collectionSnapshotCache
        : null;
    }
  }

  private static void SetCachedCollectionSnapshot(string cacheKey, CollectionSnapshotDTO snapshot)
  {
    lock (s_collectionSnapshotCacheSync)
    {
      s_collectionSnapshotCacheKey = cacheKey;
      s_collectionSnapshotCache = snapshot;
      s_collectionSnapshotCacheExpiresAtUtc = snapshot.PriceCacheExpiresAt;
    }
  }

  private static string? TryGetCollectionHash(MTGOSDK.API.Collection.Collection collection)
  {
    try
    {
      return string.IsNullOrWhiteSpace(collection.Hash)
        ? null
        : collection.Hash;
    }
    catch (Exception ex)
    {
      Log.Warning(ex, "Failed to read MTGO collection hash for snapshot cache");
      return null;
    }
  }

  private static string BuildCollectionItemsCacheKey(IEnumerable<CardQuantityPair> collectionItems)
  {
    return string.Join(
      "|",
      collectionItems
        .OrderBy(item => item.Id)
        .ThenBy(item => item.Quantity)
        .Select(item => $"{item.Id}:{item.Quantity}"));
  }

  private async Task<IReadOnlyDictionary<int, CatalogSearchItem>> GetCollectionProductsAsync(
    IReadOnlyCollection<CardQuantityPair> collectionItems,
    CancellationToken cancellationToken)
  {
    try
    {
      return await videreAPIClient.SearchCollectionAsync(
        collectionItems.Select(item => item.Id).ToArray(), "is:product", cancellationToken);
    }
    catch (VidereAPIException ex)
    {
      Log.Warning(ex, "Failed to classify collection products with Videre cards search");
      return new Dictionary<int, CatalogSearchItem>();
    }
  }

  /// <summary>
  /// Get cached daily prices for the collection's catalog IDs.
  /// </summary>
  private async Task<IReadOnlyDictionary<int, ViderePriceResult>> GetCollectionPricesAsync(
    IReadOnlyCollection<CardQuantityPair> collectionItems,
    CancellationToken cancellationToken)
  {
    var collectionIds = collectionItems
      .Select(item => item.Id)
      .Where(id => id > 0)
      .Distinct()
      .ToArray();

    if (collectionIds.Length == 0)
    {
      return new Dictionary<int, ViderePriceResult>();
    }

    var now = DateTimeOffset.UtcNow;
    var expiresAtUtc = VidereAPIClient.GetPriceCacheExpiration(now);
    List<int> idsToFetch;
    Dictionary<int, ViderePriceResult> cachedPrices;

    lock (s_collectionPriceCacheSync)
    {
      if (now >= s_collectionPriceCacheExpiresAtUtc)
      {
        s_collectionPriceCache.Clear();
        s_collectionPriceMissingCache.Clear();
        s_collectionPriceCacheExpiresAtUtc = expiresAtUtc;
      }

      cachedPrices = collectionIds
        .Where(s_collectionPriceCache.ContainsKey)
        .ToDictionary(id => id, id => s_collectionPriceCache[id]);
      idsToFetch = collectionIds
        .Where(id => !s_collectionPriceCache.ContainsKey(id) &&
          !s_collectionPriceMissingCache.Contains(id))
        .ToList();
    }

    if (idsToFetch.Count == 0)
    {
      return cachedPrices;
    }

    try
    {
      var fetched = await videreAPIClient.GetLatestPricesAsync(idsToFetch, cancellationToken);

      lock (s_collectionPriceCacheSync)
      {
        if (DateTimeOffset.UtcNow >= s_collectionPriceCacheExpiresAtUtc)
        {
          s_collectionPriceCache.Clear();
          s_collectionPriceMissingCache.Clear();
          s_collectionPriceCacheExpiresAtUtc = VidereAPIClient.GetPriceCacheExpiration(DateTimeOffset.UtcNow);
        }

        foreach (var price in fetched.Prices)
        {
          s_collectionPriceCache[price.Key] = price.Value;
        }

        foreach (var missingId in fetched.MissingIds)
        {
          s_collectionPriceMissingCache.Add(missingId);
        }
      }

      foreach (var price in fetched.Prices)
      {
        cachedPrices[price.Key] = price.Value;
      }

      return cachedPrices;
    }
    catch (VidereAPIException ex)
    {
      Log.Warning(ex, "Failed to fetch collection prices with Videre prices API");
      return cachedPrices;
    }
  }

  private static string GetItemName(int catalogId, string? name) =>
    string.IsNullOrWhiteSpace(name) ? $"Catalog {catalogId}" : name;
}
