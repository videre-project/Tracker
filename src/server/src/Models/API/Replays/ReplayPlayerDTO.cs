/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.Models.API.Replays;

public class ReplayPlayerDTO
{
  public int PlayerIndex { get; set; }
  public required string Name { get; set; }
  public string? PlayDraw { get; set; }
  public int InitialLife { get; set; }
  public int InitialHandCount { get; set; }
  public int InitialLibraryCount { get; set; }
  public int InitialGraveyardCount { get; set; }
  public string? InitialManaPool { get; set; }
  public bool IsActivePlayer { get; set; }
  public double ClockRemaining { get; set; }
  public int UserId { get; set; }
  public int AvatarId { get; set; }
}
