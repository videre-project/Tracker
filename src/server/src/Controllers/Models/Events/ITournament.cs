/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.API.Play.Tournaments;


namespace Tracker.Controllers.Models.Events;

public interface ITournament
{
  int Id { get; }
  string Description { get; }
  string Format { get; }
  int MinimumPlayers { get; }
  int TotalPlayers { get; }
  int TotalRounds { get; }

  IEventStructure EventStructure { get; }
  DateTime StartTime { get; }
  DateTime EndTime { get; }

  // ITournamentStateUpdate
  TournamentState State { get; }
  int RoundNumber { get; }
  DateTime RoundEndTime { get; }
  bool InPlayoffs { get; }
}
