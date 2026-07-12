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

using Tracker.Controllers.Base;
using Tracker.Controllers.Models.Collection;
using Tracker.Services.MTGO;
using Tracker.Services.Videre;


namespace Tracker.Controllers;

/// <summary>
/// Collection management API for rendering and streaming card images
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CollectionController(
  IClientAPIProvider clientProvider,
  VidereAPIClient videreAPIClient) : APIController
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
    catch (Exception ex)
    {
      Log.Error(ex, "Failed to serialize MTGO collection");
      return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
    }
  }

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

      const int maxInlineCollectionIds = 10_000;
      var catalogIdSet = new HashSet<int>();
      foreach (var chunk in collectionIds.Chunk(maxInlineCollectionIds))
      {
        var chunkCatalogIds = await FetchCollectionSearchIdsAsync(
          chunk,
          query,
          cancellationToken);

        foreach (var catalogId in chunkCatalogIds)
        {
          catalogIdSet.Add(catalogId);
        }
      }

      return Ok(new CollectionSearchResultDTO
      {
        Query = query,
        CatalogIds = catalogIdSet
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
        products.Add(new CollectionProductDTO
        {
          CatalogId = item.Id,
          Name = string.IsNullOrWhiteSpace(product.Name)
            ? GetItemName(item.Id, item.Name)
            : product.Name,
          Quantity = item.Quantity,
          Description = product.Description,
          SetCode = product.SetCode,
          SetName = product.SetName,
          ObjectType = product.ObjectType,
          ImageUrl = string.IsNullOrWhiteSpace(product.ImageUrl)
            ? $"https://r2.videreproject.com/products/{item.Id}-300px.png"
            : product.ImageUrl,
          IsTradable = product.IsTradable,
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

  private async Task<IReadOnlyDictionary<int, VidereProductResult>> GetCollectionProductsAsync(
    IReadOnlyCollection<CardQuantityPair> collectionItems,
    CancellationToken cancellationToken)
  {
    const int maxInlineCollectionIds = 10_000;
    var collectionIds = collectionItems
      .Select(item => item.Id)
      .Where(id => id > 0)
      .Distinct()
      .ToArray();

    if (collectionIds.Length == 0)
    {
      return new Dictionary<int, VidereProductResult>();
    }

    try
    {
      var products = new Dictionary<int, VidereProductResult>();
      foreach (var chunk in collectionIds.Chunk(maxInlineCollectionIds))
      {
        var chunkProducts = await FetchCollectionProductsAsync(chunk, cancellationToken);
        foreach (var product in chunkProducts)
        {
          products[product.Key] = product.Value;
        }
      }

      return products;
    }
    catch (VidereAPIException ex)
    {
      Log.Warning(ex, "Failed to classify collection products with Videre cards search");
      return new Dictionary<int, VidereProductResult>();
    }
  }

  private async Task<IReadOnlyDictionary<int, VidereProductResult>> FetchCollectionProductsAsync(
    int[] collectionIds,
    CancellationToken cancellationToken)
  {
    return await videreAPIClient.GetCollectionProductsAsync(collectionIds, cancellationToken);
  }

  private async Task<int[]> FetchCollectionSearchIdsAsync(
    int[] collectionIds,
    string query,
    CancellationToken cancellationToken)
  {
    var matchingIds = await videreAPIClient.SearchCollectionAsync(
      collectionIds,
      query,
      cancellationToken);
    return matchingIds
      .OrderBy(id => id)
      .ToArray();
  }

  /// <summary>
  /// Get daily price history for one MTGO catalog ID.
  /// </summary>
  private async Task<IReadOnlyDictionary<int, ViderePriceResult>> GetCollectionPricesAsync(
    IReadOnlyCollection<CardQuantityPair> collectionItems,
    CancellationToken cancellationToken)
  {
    const int maxInlineCollectionIds = 10_000;
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
      var fetchedPrices = new Dictionary<int, ViderePriceResult>();
      var missingIds = new HashSet<int>();

      foreach (var chunk in idsToFetch.Chunk(maxInlineCollectionIds))
      {
        var chunkPrices = await FetchCollectionPricesAsync(chunk, cancellationToken);
        foreach (var price in chunkPrices.Prices)
        {
          fetchedPrices[price.Key] = price.Value;
        }

        foreach (var missingId in chunkPrices.MissingIds)
        {
          missingIds.Add(missingId);
        }
      }

      lock (s_collectionPriceCacheSync)
      {
        if (DateTimeOffset.UtcNow >= s_collectionPriceCacheExpiresAtUtc)
        {
          s_collectionPriceCache.Clear();
          s_collectionPriceMissingCache.Clear();
          s_collectionPriceCacheExpiresAtUtc = VidereAPIClient.GetPriceCacheExpiration(DateTimeOffset.UtcNow);
        }

        foreach (var price in fetchedPrices)
        {
          s_collectionPriceCache[price.Key] = price.Value;
        }

        foreach (var missingId in missingIds)
        {
          s_collectionPriceMissingCache.Add(missingId);
        }
      }

      foreach (var price in fetchedPrices)
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

  private async Task<(IReadOnlyDictionary<int, ViderePriceResult> Prices, IReadOnlyCollection<int> MissingIds)>
    FetchCollectionPricesAsync(
      int[] collectionIds,
      CancellationToken cancellationToken)
  {
    var result = await videreAPIClient.GetLatestPricesAsync(collectionIds, cancellationToken);
    return (result.Prices, result.MissingIds);
  }

  private static string GetItemName(int catalogId, string? name) =>
    string.IsNullOrWhiteSpace(name) ? $"Catalog {catalogId}" : name;
}
