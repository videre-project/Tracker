/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.Services.MTGO.Events;

public enum GameLogType
{
  PhaseChange,
  TurnChange,
  ZoneChange,
  GameAction,
  LifeChange,
  LogMessage
}
