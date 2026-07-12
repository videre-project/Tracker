/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.Models.API.Collection;

public class CollectionSearchResultDTO
{
  public required string Query { get; set; }
  public required int[] CatalogIds { get; set; }
}
