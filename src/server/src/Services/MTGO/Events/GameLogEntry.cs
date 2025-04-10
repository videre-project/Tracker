/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core.Reflection;
using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace Tracker.Services.MTGO.Events;

public struct GameLogEntry : IComparable<GameLogEntry>
{
  public int GameId;
  public DateTime Timestamp;
  public GameLogType Type;
  public string Data;

  public GameLogEntry(int gameId, DateTime timestamp, GameLogType type, string data)
  {
    GameId = gameId;
    Timestamp = timestamp;
    Type = type;
    Data = data;
  }

  public GameLogEntry(int gameId, DLRWrapper args, GameLogType type, string data)
  {
    GameId = gameId;
    Timestamp = Unbind(args).__timestamp;
    Type = type;
    Data = data;
  }

  public int CompareTo(GameLogEntry other)
  {
    int timestampComparison = Timestamp.CompareTo(other.Timestamp);
    if (timestampComparison != 0) return timestampComparison;

    return GameId.CompareTo(other.GameId);
  }
}
