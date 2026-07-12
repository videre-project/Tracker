/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;


namespace Tracker.Models.API.Replays;

public class ReplayDataDTO
{
  public int GameId { get; set; }
  public List<ReplayPlayerDTO> Players { get; set; } = new();
  public List<ReplayCardDTO> Cards { get; set; } = new();
  public List<ReplaySnapshotDTO> Snapshots { get; set; } = new();
}
