/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MTGOTracker.Controllers.Models.Games;

namespace MTGOTracker.Controllers.Models.Events;

/// <summary>
/// An event participant's results from MTGO and Tracker recordings.
/// </summary>
public sealed record EventPlayerHistoryDTO(
  string Player,
  IReadOnlyList<EventMatchDTO> Matches,
  string? EventName = null, string? Format = null, DateTime? StartTime = null, DateTime? EndTime = null,
  PersonalActivityDTO? Activity = null, int? TotalMatches = null);

/// <summary>
/// A pairing viewed from the selected player's perspective. Record is absent
/// until game results are available; round is absent in older recordings; byes do not imply a played game score.
/// </summary>
public sealed record EventMatchDTO(
  int? Id,
  int? Round,
  string? Opponent,
  string? Record,
  EventMatchResult Result,
  bool IsRecorded = false,
  IReadOnlyList<GameDetailsDTO>? Games = null,
  string? OpponentDeckArchetype = null,
  IReadOnlyList<string>? OpponentDeckColors = null,
  bool CanEditOpponent = false);

/// <summary>
/// The selected player's result, including pairings awaiting a final result.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EventMatchResult>))]
public enum EventMatchResult
{
  [JsonStringEnumMemberName("win")] Win,
  [JsonStringEnumMemberName("loss")] Loss,
  [JsonStringEnumMemberName("draw")] Draw,
  [JsonStringEnumMemberName("bye")] Bye,
  [JsonStringEnumMemberName("in-progress")] InProgress,
  [JsonStringEnumMemberName("pending")] Pending,
  [JsonStringEnumMemberName("nullified")] Nullified
}
