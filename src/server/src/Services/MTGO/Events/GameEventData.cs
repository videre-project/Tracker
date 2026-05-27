/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.Services.MTGO.Events;

/// <summary>
/// Data payload for <see cref="GameLogType.GameState"/> events.
/// Emitted when the turn number or phase changes between snapshots.
/// </summary>
public class GameStateData
{
  public int Turn { get; set; }
  public required string Phase { get; set; }
  public int PreviousTurn { get; set; }
  public string? PreviousPhase { get; set; }
}

/// <summary>
/// Data payload item for <see cref="GameLogType.ZoneChange"/> events.
/// Each event contains an array of these representing correlated zone transfers.
/// </summary>
public class ZoneTransferData
{
  public int CardId { get; set; }
  public required string CardName { get; set; }
  public string? FromZone { get; set; }
  public string? ToZone { get; set; }
  public int? SourceId { get; set; }
  public required string Type { get; set; }
}

/// <summary>
/// Data payload item for <see cref="GameLogType.CardChange"/> events.
/// Each event contains an array of these representing property diffs.
/// </summary>
public class CardChangeData
{
  public int CardId { get; set; }
  public required string CardName { get; set; }
  public required string Property { get; set; }
  public string? OldValue { get; set; }
  public string? NewValue { get; set; }
}

/// <summary>
/// Data payload item for <see cref="GameLogType.PlayerChange"/> events.
/// Each event contains an array of these representing property diffs.
/// </summary>
public class PlayerChangeData
{
  public int PlayerIndex { get; set; }
  public required string PlayerName { get; set; }
  public required string Property { get; set; }
  public string? OldValue { get; set; }
  public string? NewValue { get; set; }
}
