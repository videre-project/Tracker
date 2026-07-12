/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.Controllers.Models.Collection;

public class CardArtDTO
{
  public required int Index { get; set; }
  public required string Name { get; set; }
  public string? ImageData { get; set; }
}
