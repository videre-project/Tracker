/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Linq;
using System;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.API.Play.Leagues;

using MTGOTracker.Controllers.Models.Events;
using MTGOTracker.Controllers.Models.Games;
using static MTGOTracker.Services.MTGO.Events.MatchHistorySerialization;
using static MTGOTracker.Controllers.Models.Events.EventMatchResult;

namespace MTGOTracker.Services.MTGO.Events;

/// <summary>
/// Reads tournament pairings and league-course results without inspecting players' decks.
/// </summary>
internal static class EventPlayerHistory
{
  internal static IReadOnlyList<EventMatchDTO> Read(League league, string username)
  {
    var active = league.ActiveMatch;
    if (active?.IsComplete == true) active = null;
    int? activeMatchId = active?.Id;
    var matches = league.GameHistory.GroupBy(game => game.MatchId)
      .Select(group =>
      {
        var firstGame = group.First();
        var games = group.Select(game => new GameDetailsDTO
        {
          Id = game.GameId, GameNumber = game.GameIndex + 1, Result = game.Result,
          // League history reports clock consumption, not elapsed game time.
          Duration = "", PlayDraw = "Unknown",
        }).OrderBy(game => game.GameNumber).ThenBy(game => game.Id).ToArray();
        int wins = games.Count(game => game.Result == "Win");
        int losses = games.Count(game => game.Result == "Loss");
        int draws = games.Count(game => game.Result == "Draw");
        var result = activeMatchId == group.Key ? InProgress : firstGame.ResultOverride switch
        {
          MatchResultOverride.ForceWin => Win,
          MatchResultOverride.ForceLoss => Loss,
          MatchResultOverride.Nullify => Nullified,
          _ => firstGame.IsWin ? Win : Loss,
        };
        return new EventMatchDTO(group.Key, firstGame.MatchNumber, firstGame.Opponent.Name,
          $"{wins}-{losses}" + (draws > 0 ? $"-{draws}" : ""), result, Games: games);
      }).OrderBy(match => match.Round).ThenBy(match => match.Id).ToList();
    if (active != null && !matches.Any(match => match.Id == activeMatchId))
    {
      int number = league.MatchNumber;
      matches.Add(new EventMatchDTO(activeMatchId, number > 0 ? number : null,
        active.Players.FirstOrDefault(player => !string.Equals(player.Name, username, StringComparison.OrdinalIgnoreCase))?.Name,
        null, InProgress));
    }
    return matches;
  }

  private static EventMatchResult GetResult(
    MatchStandingRecord match, int playerId, int wins, int losses, int completedGames)
  {
    if (match.HasBye) return Bye;
    if (!match.State.HasFlag(MatchState.MatchCompleted)) return InProgress;

    var winners = match.WinningPlayerIds.ToHashSet();
    var losers = match.LosingPlayerIds.ToHashSet();
    // MTGO can append updates to both result lists. Resolve overlapping or
    // missing final places from completed games, rather than stale membership.
    if (winners.Overlaps(losers) || winners.Count == 0 && losers.Count == 0)
    {
      if (wins > losses) return Win;
      if (losses > wins) return Loss;
      return completedGames > 0 ? Draw : Pending;
    }

    if (winners.Contains(playerId)) return Win;
    if (losers.Contains(playerId)) return Loss;
    return Draw;
  }

  internal static IReadOnlyList<EventMatchDTO> Read(
    Tournament tournament,
    StandingRecord standing)
  {
    int playerId = standing.Player.Id;
    var matches = new List<EventMatchDTO>();
    foreach (var match in standing.PreviousMatches)
    {
      bool bye = match.HasBye;
      var games = match.GameStandingRecords
        .Where(game => game.GameStatus == GameStatus.Finished).ToArray();
      var gameWinners = games.Select(game => game.WinnerIds.ToHashSet()).ToArray();
      int wins = gameWinners.Count(winners => winners.Contains(playerId));
      int draws = gameWinners.Count(winners => winners.Count == 0);
      int losses = games.Length - wins - draws;
      EventMatchResult result = GetResult(match, playerId, wins, losses, games.Length);

      matches.Add(new EventMatchDTO(
        bye ? null : match.Id,
        match.Round,
        bye ? null : match.Players.FirstOrDefault(player => player.Id != playerId)?.Name,
        games.Length == 0 ? null : $"{wins}-{losses}-{draws}",
        result,
        Games: games.Select((game, index) => new GameDetailsDTO
        {
          Id = game.Id, GameNumber = index + 1,
          Result = gameWinners[index].Count == 0 ? "Draw"
            : gameWinners[index].Contains(playerId) ? "Win" : "Loss",
          Duration = game.CompletedDuration is { } duration ? FormatDuration(duration) : "",
          PlayDraw = "Unknown",
        }).ToArray()));
    }

    // MTGO can publish a current pairing before it enters PreviousMatches.
    var round = tournament.CurrentRound;
    if (round != null && !matches.Any(match => match.Round == round.Number))
    {
      if (round.UsersWithByes.Any(player => player.Id == playerId))
      {
        matches.Add(new EventMatchDTO(null, round.Number, null, null, Bye));
      }
      else
      {
        var pairing = round.Matches.FirstOrDefault(match =>
          match.Players.Any(player => player.Id == playerId));
        if (pairing != null)
        {
          // A completed pairing may precede its standing record. Do not infer a
          // draw or an unfinished match while waiting for its result snapshot.
          matches.Add(new EventMatchDTO(
            pairing.Id,
            round.Number,
            pairing.Players.FirstOrDefault(player => player.Id != playerId)?.Name,
            null,
            pairing.IsComplete ? Pending : InProgress));
        }
      }
    }

    return matches.OrderBy(match => match.Round).ToArray();
  }
}
