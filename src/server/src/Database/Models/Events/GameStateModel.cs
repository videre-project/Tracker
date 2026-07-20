/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable CS8618

using System;
using System.Collections.Generic;


namespace Tracker.Database.Models.Events;

public class GameStateModel
{
  public int Id { get; set; }

  public int GameId { get; set; }
  public GameModel Game { get; set; }

  /// <summary>
  /// Deterministic hash from (InteractionTimestamp, PromptedPlayer, PromptText).
  /// </summary>
  public int Nonce { get; set; }

  /// <summary>
  /// Server game timestamp (monotonically increasing).
  /// </summary>
  public long Timestamp { get; set; }

  /// <summary>
  /// When the server logged this action.
  /// </summary>
  public DateTime ActionTimestamp { get; set; }

  /// <summary>
  /// When the client received this game state update.
  /// </summary>
  public DateTime ClientTimestamp { get; set; }

  public int TurnNumber { get; set; }
  public string CurrentPhase { get; set; }
  public int PromptedPlayer { get; set; }
  public string PromptText { get; set; }

  /// <summary>
  /// JSON-serialized array of available prompt actions (button labels).
  /// Each entry has { type, name } from GamePrompt.Options.
  /// </summary>
  public string? PromptOptions { get; set; }

  // Child collections
  public List<GameActionModel> Actions { get; set; } = new();
  public List<CardStateChangeModel> CardChanges { get; set; } = new();
  public List<ZoneTransferModel> ZoneTransfers { get; set; } = new();
  public List<PlayerStateChangeModel> PlayerChanges { get; set; } = new();
  public List<GameLogModel> Logs { get; set; } = new();
}
