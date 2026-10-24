/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace MTGOTracker.Services.MTGO.Events;

public enum GameLogType
{
  GameState,
  GameAction,
  ZoneChange,
  CardChange,
  PlayerChange,
  LogMessage,
  DamageAssignment,
  Reveal,
  Interaction
}
