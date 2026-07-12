/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;


namespace Tracker.Models.API.Collection;

public class CardArtDTO
{
  public required int Index { get; set; }
  public required string Name { get; set; }
  public string? ImageData { get; set; }
}

public class CollectionSnapshotDTO
{
  public required string Hash { get; set; }
  public required int ItemCount { get; set; }
  public required int UniqueCount { get; set; }
  public required long TotalQuantity { get; set; }
  public required DateTime Timestamp { get; set; }
  public required DateTimeOffset PriceCacheExpiresAt { get; set; }
  public required double ElapsedMilliseconds { get; set; }
  public required CollectionCardDTO[] Cards { get; set; }
  public required CollectionProductDTO[] Products { get; set; }
}

public class CollectionSearchRequestDTO
{
  public string? Query { get; set; }
}

public class CollectionSearchResultDTO
{
  public required string Query { get; set; }
  public required int[] CatalogIds { get; set; }
}

public class CollectionCardDTO
{
  public required int CatalogId { get; set; }
  public required string Name { get; set; }
  public required int Quantity { get; set; }
  public decimal? Price { get; set; }
  public string? PriceDate { get; set; }
  public string? PriceSource { get; set; }
}

public class CollectionProductDTO
{
  public required int CatalogId { get; set; }
  public required string Name { get; set; }
  public required int Quantity { get; set; }
  public string? Description { get; set; }
  public string? SetCode { get; set; }
  public string? SetName { get; set; }
  public string? ObjectType { get; set; }
  public string? ImageUrl { get; set; }
  public bool? IsTradable { get; set; }
  public decimal? Price { get; set; }
  public string? PriceDate { get; set; }
  public string? PriceSource { get; set; }
}

public class CollectionPriceHistoryDTO
{
  public required int CatalogId { get; set; }
  public required DateTimeOffset PriceCacheExpiresAt { get; set; }
  public required CollectionPricePointDTO[] Prices { get; set; }
}

public class CollectionPricePointDTO
{
  public required string Date { get; set; }
  public required decimal Price { get; set; }
  public string? Source { get; set; }
}

public class CollectionCardDetailDTO
{
  public required int CatalogId { get; set; }
  public required string Name { get; set; }
  public required string CanonicalName { get; set; }
  public string? PrintedName { get; set; }
  public required string SetCode { get; set; }
  public string? SetName { get; set; }
  public string? CollectorNumber { get; set; }
  public string? Rarity { get; set; }
  public string? ManaCost { get; set; }
  public double? ManaValue { get; set; }
  public required string TypeLine { get; set; }
  public required string OracleText { get; set; }
  public string? FlavorText { get; set; }
  public required List<string> Colors { get; set; }
  public required string ImageUrl { get; set; }
  public string? Power { get; set; }
  public string? Toughness { get; set; }
  public string? Loyalty { get; set; }
  public string? Defense { get; set; }
  public string? Artist { get; set; }
  public string? PromoLabel { get; set; }
}
