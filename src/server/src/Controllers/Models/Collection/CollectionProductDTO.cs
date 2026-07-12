/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.Controllers.Models.Collection;

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
