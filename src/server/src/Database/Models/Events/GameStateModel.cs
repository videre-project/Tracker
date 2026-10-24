/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable CS8618

using System;
using System.Collections.Generic;

using MTGOTracker.Services.MTGO.Events;

namespace MTGOTracker.Database.Models.Events;

public class GameStateModel
{
  public int Id { get; set; }

  public int GameId { get; set; }
  public GameModel Game { get; set; }

  /// <summary>
  /// SDK publication identity, including client-local interaction changes.
  /// </summary>
  public int Nonce { get; set; }

  /// <summary>
  /// The originating server prompt nonce. Null for older recordings.
  /// </summary>
  public int? ServerNonce { get; set; }

  /// <summary>
  /// Server gameplay timestamp. Local interaction updates can share this value.
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
  /// Captured native prompt actions and local prompt controls.
  /// Stored as JSON by the database mapping; null for uncaptured prompts.
  /// </summary>
  public IReadOnlyList<PromptOptionData>? PromptOptions { get; set; }

  /// <summary>
  /// Complete visual flag lists and pending abilities for this update.
  /// Null for recordings made before interaction capture was supported.
  /// </summary>
  public GameInteractionData? Interaction { get; set; }

  // Child collections
  public List<GameActionModel> Actions { get; set; } = new();
  public List<CardStateChangeModel> CardChanges { get; set; } = new();
  public List<ZoneTransferModel> ZoneTransfers { get; set; } = new();
  public List<PlayerStateChangeModel> PlayerChanges { get; set; } = new();
  public List<GameLogModel> Logs { get; set; } = new();
}
