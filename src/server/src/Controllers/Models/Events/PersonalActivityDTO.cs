/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOTracker.Controllers.Models.Decks;


namespace MTGOTracker.Controllers.Models.Events;

public sealed record ActivityClockDTO(DateTime SampledAt, double DurationSeconds,
  double RemainingSeconds, bool Running);

public sealed record PersonalMatchDTO(int Id, string? Opponent, int? GameNumber,
  string Status, ActivityClockDTO? Clock = null);

public sealed record PersonalActivityDTO(
  string Id, int EventId, string Kind, string Name, string Format, string Status,
  string StateLabel, DateTime? StartTime = null, DateTime? RoundEndTime = null,
  int? RoundNumber = null, int? TotalRounds = null, int? Wins = null,
  int? Losses = null, string? DeckName = null, PersonalMatchDTO? Match = null,
  int? NetDeckId = null, int? CourseId = null, DeckReferenceData? Deck = null,
  DateTime? EndTime = null, bool IsDraft = false, bool HasPlayoffs = false,
  bool InPlayoffs = false, int? CompletedRounds = null,
  bool RoundInProgress = false, ActivityClockDTO? RoundClock = null);
