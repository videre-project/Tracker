/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace Tracker.Models.API.Collection;

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
