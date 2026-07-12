/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.Models.API.Collection;

public class CollectionPricePointDTO
{
  public required string Date { get; set; }
  public required decimal Price { get; set; }
  public string? Source { get; set; }
}
