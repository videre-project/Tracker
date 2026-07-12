/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;


namespace Tracker.Models.API.Games;

public class GameDetailsDTO
{
  public int Id { get; set; }
  public int GameNumber { get; set; }
  public required string Result { get; set; }
  public required string Duration { get; set; }
  public required string PlayDraw { get; set; }
  public List<GameLogDTO> Logs { get; set; } = new();
}
