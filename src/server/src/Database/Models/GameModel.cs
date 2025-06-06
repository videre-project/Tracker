/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable CS8618

using System.Collections.Generic;

using MTGOSDK.API.Play.Games;


namespace Tracker.Database.Models;

public class GameModel
{
  public int Id { get; set; }

  public int MatchId { get; set; }
  public MatchModel Match { get; set; }

  public List<GamePlayerResult> GamePlayerResults { get; set; } = new();
  public List<GameLogModel> GameLogs { get; set; } = new();
}
