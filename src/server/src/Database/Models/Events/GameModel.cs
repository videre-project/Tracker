/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable CS8618

using System.Collections.Generic;

using MTGOSDK.API.Play.Games;


namespace Tracker.Database.Models.Events;

public class GameModel
{
  public int Id { get; set; }

  public int MatchId { get; set; }
  public MatchModel Match { get; set; }

  public List<GamePlayerResult> GamePlayerResults { get; set; } = new();

  // Structured game state tables
  public List<GameCardModel> Cards { get; set; } = new();
  public List<GamePlayerModel> Players { get; set; } = new();
  public List<GameStateModel> States { get; set; } = new();
}
