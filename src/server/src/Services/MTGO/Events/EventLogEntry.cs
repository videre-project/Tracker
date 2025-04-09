/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core.Reflection;
using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace Tracker.Services.MTGO.Events;

public struct EventLogEntry : IComparable<EventLogEntry>
{
  public int GameId;
  public DateTime Timestamp;
  public EventType Type;
  public string Data;

  public EventLogEntry(int gameId, DateTime timestamp, EventType type, string data)
  {
    GameId = gameId;
    Timestamp = timestamp;
    Type = type;
    Data = data;
  }

  public EventLogEntry(int gameId, DLRWrapper args, EventType type, string data)
  {
    GameId = gameId;
    Timestamp = Unbind(args).__timestamp;
    Type = type;
    Data = data;
  }

  public int CompareTo(EventLogEntry other)
  {
    int timestampComparison = Timestamp.CompareTo(other.Timestamp);
    if (timestampComparison != 0) return timestampComparison;

    return GameId.CompareTo(other.GameId);
  }
}
