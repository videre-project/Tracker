/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;


namespace Tracker.Models.API.Collection;

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
