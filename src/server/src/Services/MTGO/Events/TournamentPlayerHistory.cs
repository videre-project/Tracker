/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Play.Tournaments;

using MTGOTracker.Controllers.Models.Events;
using static MTGOTracker.Controllers.Models.Events.TournamentMatchResult;

namespace MTGOTracker.Services.MTGO.Events;

/// <summary>
/// Reads tournament pairings and results without inspecting players' decks.
/// </summary>
internal static class TournamentPlayerHistory
{
  private static TournamentMatchResult GetResult(
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

  internal static IReadOnlyList<TournamentMatchDTO> Read(
    Tournament tournament,
    StandingRecord standing)
  {
    int playerId = standing.Player.Id;
    var matches = new List<TournamentMatchDTO>();
    foreach (var match in standing.PreviousMatches)
    {
      bool bye = match.HasBye;
      var games = match.GameStandingRecords
        .Where(game => game.GameStatus == GameStatus.Finished).ToArray();
      var gameWinners = games.Select(game => game.WinnerIds.ToHashSet()).ToArray();
      int wins = gameWinners.Count(winners => winners.Contains(playerId));
      int draws = gameWinners.Count(winners => winners.Count == 0);
      int losses = games.Length - wins - draws;
      TournamentMatchResult result = GetResult(match, playerId, wins, losses, games.Length);

      matches.Add(new TournamentMatchDTO(
        bye ? null : match.Id,
        match.Round,
        bye ? null : match.Players.FirstOrDefault(player => player.Id != playerId)?.Name,
        games.Length == 0 ? null : $"{wins}-{losses}-{draws}",
        result));
    }

    // MTGO can publish a current pairing before it enters PreviousMatches.
    var round = tournament.CurrentRound;
    if (round != null && !matches.Any(match => match.Round == round.Number))
    {
      if (round.UsersWithByes.Any(player => player.Id == playerId))
      {
        matches.Add(new TournamentMatchDTO(null, round.Number, null, null, Bye));
      }
      else
      {
        var pairing = round.Matches.FirstOrDefault(match =>
          match.Players.Any(player => player.Id == playerId));
        if (pairing != null)
        {
          // A completed pairing may precede its standing record. Do not infer a
          // draw or an unfinished match while waiting for its result snapshot.
          matches.Add(new TournamentMatchDTO(
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
