/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable CS8618

using System;


namespace Tracker.Database.Models.Events;

public class GamePlayerModel
{
  public int Id { get; set; }

  public int GameId { get; set; }
  public GameModel Game { get; set; }

  /// <summary>
  /// The snapshot where this player first appeared.
  /// </summary>
  public int FirstSeenStateId { get; set; }
  public GameStateModel FirstSeenState { get; set; }

  public int PlayerIndex { get; set; }
  public string Name { get; set; }

  /// <summary>
  /// "Play" or "Draw".
  /// </summary>
  public string? PlayDraw { get; set; }

  public int InitialLife { get; set; }
  public int InitialHandCount { get; set; }
  public int InitialLibraryCount { get; set; }
  public int InitialGraveyardCount { get; set; }

  /// <summary>
  /// JSON of Mana list.
  /// </summary>
  public string? InitialManaPool { get; set; }

  public bool IsActivePlayer { get; set; }

  /// <summary>
  /// The user's Login ID.
  /// </summary>
  public int UserId { get; set; }

  /// <summary>
  /// Catalog ID of the player's avatar image (resolved from User.Avatar).
  /// </summary>
  public int AvatarId { get; set; }

  /// <summary>
  /// Chess clock time remaining at first observation (seconds).
  /// Updated per-snapshot via "ClockRemaining" diffs.
  /// </summary>
  public double ClockRemaining { get; set; }
}
