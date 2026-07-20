/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable CS8618

using System;


namespace Tracker.Database.Models.Events;

public class CardStateChangeModel
{
  public int Id { get; set; }

  public int GameStateId { get; set; }
  public GameStateModel GameState { get; set; }

  /// <summary>
  /// MTGO ThingID for the card.
  /// </summary>
  public int CardId { get; set; }

  public string CardName { get; set; }

  /// <summary>
  /// Property that changed, e.g., "Power", "Toughness", "IsTapped", "Counters".
  /// Zone-related properties (Id, SourceId, FromZone, ToZone) are excluded when
  /// they match a ZoneTransfer in the same snapshot.
  /// </summary>
  public string Property { get; set; }

  public string? OldValue { get; set; }
  public string NewValue { get; set; }
}
