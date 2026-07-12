/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using Tracker.Services.MTGO.Events;


namespace Tracker.Models.API.Games;

public class GameLogDTO
{
  public int Id { get; set; }
  public int GameId { get; set; }
  public DateTime Timestamp { get; set; }
  public required GameLogType GameLogType { get; set; }
  public required string Data { get; set; }
  public int Nonce { get; set; }
}
