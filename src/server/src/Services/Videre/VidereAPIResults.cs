/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;


namespace MTGOTracker.Services.Videre;

/// <summary>
/// Card and product metadata reconciled for deck synchronization and trade history.
/// </summary>
public sealed record VidereCatalogMetadataResult(
  int Id,
  string Name,
  string? SetCode,
  string? Rarity,
  string? ObjectType,
  string? ImageUrl,
  double? ManaValue = null,
  IReadOnlyList<string>? Colors = null,
  string? TypeLine = null,
  bool? HasSeparateFace = null
);

/// <summary>
/// Local price values use decimal amounts and ISO dates.
/// </summary>
public sealed record ViderePriceResult(
  int Id,
  string? PriceDate,
  decimal SellPrice,
  string? Source
);

/// <summary>
/// Latest prices indexed by catalog ID, including IDs with no available price.
/// </summary>
public sealed record ViderePricesResult(
  IReadOnlyDictionary<int, ViderePriceResult> Prices,
  IReadOnlyCollection<int> MissingIds
);

public sealed class VidereAPIException(
  string message,
  int? statusCode = null,
  string? response = null,
  Exception? innerException = null) : Exception(message, innerException)
{
  public int? StatusCode { get; } = statusCode;
  public string? Response { get; } = response;
}
