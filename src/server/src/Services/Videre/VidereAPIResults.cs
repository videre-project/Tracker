/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;


namespace Tracker.Services.Videre;

public sealed record VidereProductResult(
  int Id,
  string? SetCode,
  string? SetName,
  string? Name,
  string? Description,
  string? ObjectType,
  string? ImageUrl,
  bool? IsTradable
);

public sealed record ViderePriceResult(
  int Id,
  string? PriceDate,
  decimal SellPrice,
  string? Source
);

public sealed record ViderePricesResult(
  IReadOnlyDictionary<int, ViderePriceResult> Prices,
  IReadOnlyCollection<int> MissingIds
);

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
  string? PromoLabel
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
