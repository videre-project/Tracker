/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Serialization;

using Tracker.Controllers;

using TournamentMatch = MTGOSDK.API.Play.Match;
using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace Tracker.Services.MTGO.Events;

internal static class TournamentSerialization
{
  internal static IEnumerable<object> SerializeStandings(IList<StandingRecord> standings)
  {
    var serialized = standings
      .SerializeAs<EventsController.IStandingResult>()
      .ToList();

    return serialized.Zip(standings, (dto, standing) => (object)new
    {
      dto.Rank,
      Player = standing.Player.Name,
      dto.Points,
      dto.Record,
      dto.OpponentMatchWinPercentage,
      dto.GameWinPercentage,
      dto.OpponentGameWinPercentage,
    }).ToList();
  }

  internal static object SerializeTournamentState(
    Tournament tournament,
    int? roundNumberOverride = null)
  {
    var dto = tournament.SerializeAs<EventsController.ITournamentStateCore>();
    int roundNumber = ResolveRoundNumber(dto.RoundNumber, roundNumberOverride);

    return new
    {
      dto.Id,
      dto.State,
      RoundNumber = roundNumber,
      dto.RoundEndTime,
      dto.InPlayoffs,
      ActivePlayerNames = GetActivePlayerNames(tournament),
      PlayerNamesWithMatchesInProgress =
        GetPlayerNamesWithMatchesInProgress(tournament, roundNumber),
    };
  }

  private static string[] GetActivePlayerNames(Tournament tournament)
  {
    return tournament.ActivePlayers
      .Select(player => player.Name)
      .OrderBy(name => name, StringComparer.Ordinal)
      .ToArray();
  }

  private static string[] GetPlayerNamesWithMatchesInProgress(
    Tournament tournament,
    int roundNumber)
  {
    if (tournament.State != TournamentState.RoundInProgress)
    {
      return [];
    }

    TournamentRound? currentRound = GetCurrentRound(tournament, roundNumber);
    if (currentRound == null) return [];

    return Try(
      () => currentRound.PlayerNamesWithMatchesInProgress.ToArray(),
      () => ComputePlayerNamesWithMatchesInProgress(tournament, roundNumber));
  }

  private static string[] ComputePlayerNamesWithMatchesInProgress(
    Tournament tournament,
    int roundNumber)
  {
    if (tournament.State != TournamentState.RoundInProgress ||
        roundNumber <= 0)
    {
      return [];
    }

    TournamentRound? currentRound = GetCurrentRound(tournament, roundNumber);
    if (currentRound == null) return [];

    TournamentMatch[] currentRoundMatches = Try(
      () => ((IEnumerable<TournamentMatch>)((dynamic)currentRound)
          .MatchesInProgress).ToArray(),
      () => currentRound.Matches
        .Where(match => !match.IsComplete)
        .ToArray(),
      () => Array.Empty<TournamentMatch>());

    return currentRoundMatches
      .SelectMany(match => match.Players)
      .Select(player => player.Name)
      .Where(name => !string.IsNullOrEmpty(name))
      .Distinct(StringComparer.Ordinal)
      .OrderBy(name => name, StringComparer.Ordinal)
      .ToArray();
  }

  private static TournamentRound? GetCurrentRound(
    Tournament tournament,
    int? roundNumberOverride = null)
  {
    int roundNumber = ResolveRoundNumber(
      tournament.RoundNumber,
      roundNumberOverride);
    if (roundNumber <= 0) return null;

    TournamentRound? currentRound = TryGetCurrentRound(tournament);
    if (currentRound?.Number == roundNumber)
    {
      return currentRound;
    }

    TournamentRound[] rounds = Try(
      () => tournament.Rounds.ToArray(),
      () => Array.Empty<TournamentRound>());

    int roundIndex = roundNumber - 1;
    if (roundIndex >= 0 &&
        roundIndex < rounds.Length &&
        rounds[roundIndex].Number == roundNumber)
    {
      return rounds[roundIndex];
    }

    return rounds.FirstOrDefault(round => round.Number == roundNumber);
  }

  private static TournamentRound? TryGetCurrentRound(Tournament tournament)
  {
    try
    {
      return tournament.CurrentRound;
    }
    catch
    {
      return null;
    }
  }

  internal static object? SerializeTournamentForEventsList(
    Tournament tournament,
    int tournamentId,
    int? roundNumberOverride = null)
  {
    try
    {
      var dto = tournament.SerializeAs<EventsController.ITournament>();
      int roundNumber = ResolveRoundNumber(dto.RoundNumber, roundNumberOverride);

      return new
      {
        dto.Id,
        dto.Description,
        Format = NormalizeFormatName(dto.Format),
        dto.MinimumPlayers,
        dto.TotalPlayers,
        dto.TotalRounds,
        dto.EventStructure,
        dto.StartTime,
        dto.EndTime,
        dto.State,
        RoundNumber = roundNumber,
        dto.RoundEndTime,
        dto.InPlayoffs,
      };
    }
    catch (Exception ex)
    {
      if (GameAPIService.DiscoveredTournaments.TryGetValue(tournamentId, out var cachedTournament) &&
          ReferenceEquals(cachedTournament, tournament))
      {
        GameAPIService.DiscoveredTournaments.TryRemove(tournamentId, out _);
      }

      Log.Debug(
        ex,
        "Skipped loaded tournament while serializing events list tournament={TournamentId}",
        tournamentId);
      return null;
    }
  }

  private static string NormalizeFormatName(string format)
  {
    if (string.IsNullOrWhiteSpace(format))
    {
      return format;
    }

    string trimmed = format.TrimEnd('\0').Trim();
    string withoutFalseMultiplierSuffix = Regex.Replace(
      trimmed,
      @"(x[36])\s*[0o]$",
      "$1",
      RegexOptions.IgnoreCase);

    if (Regex.IsMatch(trimmed, @"^[^\d]*[A-Za-z]0$"))
    {
      return trimmed[..^1];
    }

    return withoutFalseMultiplierSuffix;
  }

  private static int ResolveRoundNumber(int serializedRoundNumber, int? roundNumberOverride)
  {
    return roundNumberOverride.HasValue
      ? Math.Max(serializedRoundNumber, roundNumberOverride.Value)
      : serializedRoundNumber;
  }

}
