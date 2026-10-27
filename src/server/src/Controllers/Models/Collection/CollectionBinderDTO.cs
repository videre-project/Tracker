/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

namespace MTGOTracker.Controllers.Models.Collection;

public sealed class CollectionBinderDTO
{
  public required int Id { get; set; }
  public required string Name { get; set; }
  public required bool IsWishList { get; set; }
}
