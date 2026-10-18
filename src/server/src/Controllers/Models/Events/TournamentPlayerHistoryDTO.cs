/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MTGOTracker.Controllers.Models.Events;

/// <summary>
/// A selected tournament participant and the match results available from recordings and MTGO.
/// </summary>
public sealed record TournamentPlayerHistoryDTO(
  string Player,
  IReadOnlyList<TournamentMatchDTO> Matches,
  string? EventName = null, string? Format = null, DateTime? StartTime = null, DateTime? EndTime = null);

/// <summary>
/// A pairing viewed from the selected player's perspective. Record is absent
/// until game results are available; round is absent in older recordings; byes do not imply a played game score.
/// </summary>
public sealed record TournamentMatchDTO(
  int? Id,
  int? Round,
  string? Opponent,
  string? Record,
  TournamentMatchResult Result,
  bool IsRecorded = false);

/// <summary>
/// The selected player's result, including pairings awaiting a final result.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TournamentMatchResult>))]
public enum TournamentMatchResult
{
  [JsonStringEnumMemberName("win")] Win,
  [JsonStringEnumMemberName("loss")] Loss,
  [JsonStringEnumMemberName("draw")] Draw,
  [JsonStringEnumMemberName("bye")] Bye,
  [JsonStringEnumMemberName("in-progress")] InProgress,
  [JsonStringEnumMemberName("pending")] Pending
}
