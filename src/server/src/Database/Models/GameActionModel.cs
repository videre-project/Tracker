/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable CS8618

using System;
using System.Linq;
using System.Text.Json;

using MTGOSDK.API.Play.Games;
using MTGOSDK.Core.Reflection.Serialization;


namespace Tracker.Database.Models;

public class GameActionModel
{
  public GameActionModel() { }

  public GameActionModel(GameAction action)
  {
    ActionType = action.GetType().Name;
    ActionName = action.Name;
    Timestamp = action.Timestamp;
    ClientTimestamp = action.ClientTimestamp;

    if (action is CardAction cardAction)
    {
      try { CardId = cardAction.Card?.Id; } catch { }
      try { CardName = cardAction.Card?.Name; } catch { }
      try
      {
        var targetSets = cardAction.Targets;
        if (targetSets?.Count > 0)
          Targets = JsonSerializer.Serialize(
            targetSets.Select(t => t.ToString()), JsonSerializerOptions.Web);
      }
      catch { }
    }

    try { Data = action.ToJSON(); } catch { Data = "{}"; }
  }

  public int Id { get; set; }

  public int GameStateId { get; set; }
  public GameStateModel GameState { get; set; }

  /// <summary>
  /// "CardAction", "PrimitiveAction", "UndoAction", etc.
  /// </summary>
  public string ActionType { get; set; }

  /// <summary>
  /// e.g., "Cast", "Activate", "Cancel".
  /// </summary>
  public string? ActionName { get; set; }

  /// <summary>
  /// Game action timestamp.
  /// </summary>
  public long Timestamp { get; set; }

  /// <summary>
  /// When the action was executed on the client (local time).
  /// Used to determine if the action predates its associated game state
  /// (i.e., the action caused the state transition).
  /// </summary>
  public DateTime ClientTimestamp { get; set; }

  /// <summary>
  /// Source card ThingID (if CardAction).
  /// </summary>
  public int? CardId { get; set; }

  /// <summary>
  /// Source card name (if CardAction).
  /// </summary>
  public string? CardName { get; set; }

  /// <summary>
  /// JSON array of target descriptions.
  /// </summary>
  public string? Targets { get; set; }

  /// <summary>
  /// Full action.ToJSON() for detailed replay.
  /// </summary>
  public string Data { get; set; }
}
