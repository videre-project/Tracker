/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable CS8618

using System;


namespace Tracker.Database.Models;

public class PlayerStateChangeModel
{
  public int Id { get; set; }

  public int GameStateId { get; set; }
  public GameStateModel GameState { get; set; }

  public int PlayerIndex { get; set; }
  public string PlayerName { get; set; }

  /// <summary>
  /// Property that changed, e.g., "Life", "HandCount", "LibraryCount", "ManaPool".
  /// </summary>
  public string Property { get; set; }

  public string? OldValue { get; set; }
  public string NewValue { get; set; }
}
