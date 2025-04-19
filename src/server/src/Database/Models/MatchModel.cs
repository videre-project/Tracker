/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable CS8618

using System.Collections.Generic;

using MTGOSDK.API.Play;


namespace Tracker.Database.Models;

public class MatchModel
{
  public int Id { get; set; }

  public int EventId { get; set; }
  public EventModel Event { get; set; }

  public List<PlayerResult> PlayerResults { get; set; } = new();
  public Dictionary<int, List<CardEntry>> SideboardChanges { get; set; } = new();

  public List<GameModel> Games { get; set; } = new();
}
