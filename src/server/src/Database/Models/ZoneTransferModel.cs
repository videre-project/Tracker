/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable CS8618

using System;


namespace Tracker.Database.Models;

public class ZoneTransferModel
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
  /// Source zone name. Null if the card entered play from an unknown origin.
  /// </summary>
  public string? FromZone { get; set; }

  /// <summary>
  /// Destination zone name. Null if the card left play.
  /// </summary>
  public string? ToZone { get; set; }

  /// <summary>
  /// Server chain source ThingID for correlation.
  /// </summary>
  public int? SourceId { get; set; }

  /// <summary>
  /// "Arrived", "Departed", or "Moved".
  /// </summary>
  public string Type { get; set; }
}
