/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable CS8618

using System;


namespace Tracker.Database.Models;

public class GameLogModel
{
  public int Id { get; set; }
  public DateTime Timestamp { get; set; }
  public string GameLogType { get; set; }
  public string Data { get; set; }

  public int GameId { get; set; }
  public GameModel Game { get; set; }
}
