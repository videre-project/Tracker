/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

using Tracker.Services.MTGO.Events;


namespace Tracker.Models.API.Games;

public class GameLogDTO
{
  public int Id { get; set; }
  public int GameId { get; set; }
  public DateTime Timestamp { get; set; }
  public required GameLogType GameLogType { get; set; }
  public required string Data { get; set; }
  public int Nonce { get; set; }
}

public class GameDetailsDTO
{
  public int Id { get; set; }
  public int GameNumber { get; set; }
  public required string Result { get; set; }
  public required string Duration { get; set; }
  public required string PlayDraw { get; set; }
  public List<GameLogDTO> Logs { get; set; } = new();
}

public class MatchDetailsDTO
{
  public int Id { get; set; }
  public int EventId { get; set; }
  public required string EventName { get; set; }
  public required string Format { get; set; }
  public DateTime StartTime { get; set; }
  public required string Result { get; set; }
  public required string Record { get; set; }
  public required string Duration { get; set; }
  public string? DeckHash { get; set; }
  public string? DeckName { get; set; }
  public string? DeckArchetype { get; set; }
  public List<string>? DeckColors { get; set; }
  public string? OpponentName { get; set; }
  public string? OpponentDeckName { get; set; }
  public string? OpponentDeckArchetype { get; set; }
  public List<string>? OpponentDeckColors { get; set; }
  public bool IsActive { get; set; }

  public List<GameDetailsDTO> Games { get; set; } = new();
}

public class PaginatedMatchesDTO
{
  public List<MatchHistoryDTO> Items { get; set; } = new();
  public int TotalCount { get; set; }
  public int Page { get; set; }
  public int PageSize { get; set; }
  public int TotalPages { get; set; }
}

public class MatchHistoryDTO
{
  public int Id { get; set; }
  public int EventId { get; set; }
  public required string EventName { get; set; }
  public required string Format { get; set; }
  public DateTime StartTime { get; set; }
  public required string Result { get; set; }
  public required string Record { get; set; }
  public required string Duration { get; set; }
  public string? DeckHash { get; set; }
  public string? DeckName { get; set; }
  public List<string>? DeckColors { get; set; }
  public string? OpponentName { get; set; }
  public string? OpponentDeckName { get; set; }
  public string? OpponentDeckArchetype { get; set; }
  public List<string>? OpponentDeckColors { get; set; }
  public bool IsActive { get; set; }
  public bool IsEvent { get; set; }
  public List<MatchHistoryDTO>? Matches { get; set; }
}

public class DashboardStatsDTO
{
  public double OverallWinrate { get; set; }
  public int TotalMatches { get; set; }
  public int Wins { get; set; }
  public int Losses { get; set; }
  public int Ties { get; set; }
  public double PlayWinrate { get; set; }
  public int PlayMatches { get; set; }
  public double DrawWinrate { get; set; }
  public int DrawMatches { get; set; }
  public string AverageDuration { get; set; } = "0m 0s";
  public string DurationTwoGames { get; set; } = "0m 0s";
  public string DurationThreeGames { get; set; } = "0m 0s";
}

public class PerformanceTrendDTO
{
  public required string Date { get; set; }
  public required DateTime RawDate { get; set; }
  public double? Winrate { get; set; }
  public int Matches { get; set; }
  public double? RollingAvg { get; set; }
  public double[]? Ci95 { get; set; }
  public double[]? Ci80 { get; set; }
  public double[]? Ci50 { get; set; }
}

//
// Replay DTOs
//

public class ReplayDataDTO
{
  public int GameId { get; set; }
  public List<ReplayPlayerDTO> Players { get; set; } = new();
  public List<ReplayCardDTO> Cards { get; set; } = new();
  public List<ReplaySnapshotDTO> Snapshots { get; set; } = new();
}

public class ReplayPlayerDTO
{
  public int PlayerIndex { get; set; }
  public required string Name { get; set; }
  public string? PlayDraw { get; set; }
  public int InitialLife { get; set; }
  public int InitialHandCount { get; set; }
  public int InitialLibraryCount { get; set; }
  public int InitialGraveyardCount { get; set; }
  public string? InitialManaPool { get; set; }
  public bool IsActivePlayer { get; set; }
  public double ClockRemaining { get; set; }
  public int UserId { get; set; }
  public int AvatarId { get; set; }
}

public class ReplayCardDTO
{
  public int CardId { get; set; }
  public required string Name { get; set; }
  public string? RulesText { get; set; }
  public string? ManaCost { get; set; }
  public int? CatalogId { get; set; }
  public required string InitialZone { get; set; }
  public string? InitialPower { get; set; }
  public string? InitialToughness { get; set; }
  public int OwnerId { get; set; }
  public int? SourceId { get; set; }
  public bool IsTapped { get; set; }
  public bool IsToken { get; set; }
  public bool IsLand { get; set; }
  public bool IsActivatedAbility { get; set; }
  public bool IsTriggeredAbility { get; set; }
  public int FirstSeenSnapshotIndex { get; set; }
}

public class ReplaySnapshotDTO
{
  public int Index { get; set; }
  public int Nonce { get; set; }
  public DateTime Timestamp { get; set; }
  public int TurnNumber { get; set; }
  public required string CurrentPhase { get; set; }
  public int PromptedPlayer { get; set; }
  public required string PromptText { get; set; }
  /// <summary>
  /// JSON array of available prompt actions, e.g. [{"type":"ChooseOption","name":"OK"}].
  /// </summary>
  public string? PromptOptions { get; set; }
  public List<ZoneTransferData> ZoneTransfers { get; set; } = new();
  public List<CardChangeData> CardChanges { get; set; } = new();
  public List<PlayerChangeData> PlayerChanges { get; set; } = new();
  public List<ReplayActionDTO> Actions { get; set; } = new();
  public List<ReplayLogDTO> Logs { get; set; } = new();
}

public class ReplayActionDTO
{
  public required string ActionType { get; set; }
  public string? ActionName { get; set; }
  public int? CardId { get; set; }
  public string? CardName { get; set; }
  public string? Targets { get; set; }
  public required string Data { get; set; }
  public DateTime ClientTimestamp { get; set; }
  public int Nonce { get; set; }
}

public class ReplayLogDTO
{
  public DateTime Timestamp { get; set; }
  public required string Data { get; set; }
}
