/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.Models.API.Collection;

public class CollectionCardDTO
{
  public required int CatalogId { get; set; }
  public required string Name { get; set; }
  public required int Quantity { get; set; }
  public decimal? Price { get; set; }
  public string? PriceDate { get; set; }
  public string? PriceSource { get; set; }
}
