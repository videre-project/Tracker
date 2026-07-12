/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace Tracker.Models.API.Events;

public interface ITournamentPlayerUpdate
{
  int Id { get; }
  int TotalPlayers { get; }
  int TotalRounds { get; }
  DateTime EndTime { get; }
}
