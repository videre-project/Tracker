/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.API.Play.Tournaments;


namespace Tracker.Models.API.Events;

public interface ITournamentStateCore
{
  int Id { get; }
  TournamentState State { get; }
  int RoundNumber { get; }
  DateTime RoundEndTime { get; }
  bool InPlayoffs { get; }
}
