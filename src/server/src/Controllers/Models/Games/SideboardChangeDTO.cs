/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.Controllers.Models.Games;

public class SideboardChangeDTO
{
  public int CatalogId { get; set; }
  public required string Name { get; set; }
  public int Quantity { get; set; }
}
