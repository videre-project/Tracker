/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace Tracker.Models.API.Collection;

public class CollectionPriceHistoryDTO
{
  public required int CatalogId { get; set; }
  public required DateTimeOffset PriceCacheExpiresAt { get; set; }
  public required CollectionPricePointDTO[] Prices { get; set; }
}
