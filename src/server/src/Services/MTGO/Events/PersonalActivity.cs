/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Play.Leagues;
using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.Core.Logging;
using MTGOTracker.Controllers.Models.Events;

namespace MTGOTracker.Services.MTGO.Events;

internal static class PersonalActivity
{
  // JoinedEvents is personal participation; the public tournament catalog and
  // spectated matches must never populate this list.
  internal static PersonalActivityDTO[] Read(string username)
  {
    var activities = new Dictionary<string, PersonalActivityDTO>();
    var standalone = new List<Match>();
    var nestedMatches = new HashSet<int>();
    var events = EventManager.JoinedEvents.Cast<Event>()
      .Concat(LeagueManager.OpenLeagues.Cast<Event>()).ToArray();
    foreach (var playerEvent in events)
    {
      try
      {
        if ((!playerEvent.HasJoined && !playerEvent.IsParticipant) ||
            playerEvent.IsRemoved || playerEvent.IsEliminated) continue;
        if (playerEvent is Match match)
        {
          standalone.Add(match);
          continue;
        }
        PersonalActivityDTO? activity = playerEvent switch
        {
          Tournament tournament => ReadTournament(tournament, username),
          League league => ReadLeague(league, username),
          Queue queue when !queue.IsCompleted => new(
            $"queue-{queue.Id}", queue.Id, "queue", queue.Description,
            queue.Format.ToString(), "registered", "Waiting for players",
            DeckName: ReadOptional(() => queue.RegisteredDeck?.Name),
            NetDeckId: ReadOptional(() => queue.RegisteredDeck?.NetDeckId),
            IsDraft: ReadOptional(() => queue.EventStructure.IsDraft)),
          _ => null,
        };
        if (activity == null) continue;
        activities[activity.Id] = activity;
        if (activity.Match != null) nestedMatches.Add(activity.Match.Id);
      }
      catch (Exception exception)
      {
        // An event may disappear during enumeration as MTGO leaves or finishes it.
        Log.Debug(exception, "Could not read a joined event for the activity sidebar");
      }
    }
    foreach (var match in standalone)
    {
      try
      {
        if (nestedMatches.Contains(match.Id)) continue;
        var currentMatch = ReadMatch(match, username);
        if (currentMatch == null) continue;
        string id = $"match-{match.Id}";
        activities[id] = new(id, match.Id, "match", match.Description,
          match.Format.ToString(), "active", currentMatch.Status, Match: currentMatch,
          DeckName: ReadOptional(() => match.RegisteredDeck?.Name),
          NetDeckId: ReadOptional(() => match.RegisteredDeck?.NetDeckId));
      }
      catch (Exception exception)
      {
        Log.Debug(exception, "Could not read a joined match for the activity sidebar");
      }
    }
    return activities.Values.OrderBy(activity => activity.Status)
      .ThenBy(activity => activity.StartTime).ThenBy(activity => activity.Id).ToArray();
  }

  private static PersonalActivityDTO? ReadTournament(Tournament tournament, string username)
  {
    var state = tournament.State;
    if (state == TournamentState.Finished || tournament.IsCompleted) return null;
    bool registered = state is TournamentState.WaitingToStart or TournamentState.NotSet;
    string label = state switch
    {
      TournamentState.Drafting => "Drafting",
      TournamentState.Deckbuilding => "Deckbuilding",
      TournamentState.DeckbuildingDeckSubmitted => "Deck submitted",
      TournamentState.WaitingForFirstRoundToStart => "Waiting for round 1",
      TournamentState.RoundInProgress => tournament.HasBye ? "Bye this round" : "Round in progress",
      TournamentState.BetweenRounds => "Between rounds",
      _ => registered ? "Registered" : "Starting",
    };
    return new($"tournament-{tournament.Id}", tournament.Id, "tournament",
      tournament.Description, tournament.Format.ToString(), registered ? "registered" : "active",
      label, tournament.StartTime,
      registered ? null : ReadOptional(() => (DateTime?)tournament.RoundEndTime),
      tournament.RoundNumber, tournament.TotalRounds,
      DeckName: ReadOptional(() => tournament.RegisteredDeck?.Name),
      Match: ReadMatch(tournament.ActiveMatch, username),
      NetDeckId: ReadOptional(() => tournament.RegisteredDeck?.NetDeckId),
      EndTime: ReadOptional(() => (DateTime?)tournament.EndTime),
      IsDraft: ReadOptional(() => tournament.EventStructure.IsDraft),
      HasPlayoffs: ReadOptional(() => tournament.HasPlayoffs),
      InPlayoffs: ReadOptional(() => tournament.InPlayoffs),
      CompletedRounds: state == TournamentState.BetweenRounds ? tournament.RoundNumber
        : state == TournamentState.RoundInProgress ? Math.Max(0, tournament.RoundNumber - 1) : 0,
      RoundInProgress: state == TournamentState.RoundInProgress,
      RoundClock: state == TournamentState.RoundInProgress ? ReadOptional(() => ReadRoundClock(tournament)) : null);
  }

  private static PersonalActivityDTO? ReadLeague(League league, string username)
  {
    if (league.CourseId <= 0 || league.IsCompleted) return null;
    var match = ReadMatch(league.ActiveMatch, username);
    if (league.MatchesRemaining <= 0 && match == null) return null;
    return new($"league-{league.Id}-{league.CourseId}", league.Id, "league", league.Name,
      league.Format.ToString(), "active", league.IsWaitingInMatchQueue ? "Finding a match"
        : league.IsPaused ? "Paused" : "Ready for next match",
      TotalRounds: league.TotalMatches, Wins: league.Wins, Losses: league.Losses,
      DeckName: ReadOptional(() => league.ActiveDeck?.Name),
      Match: match, NetDeckId: ReadOptional(() => league.ActiveDeck?.NetDeckId), CourseId: league.CourseId);
  }

  private static PersonalMatchDTO? ReadMatch(Match? match, string username)
  {
    if (match == null || match.IsComplete) return null;
    var state = match.State;
    if ((state & (MatchState.WatchRequested | MatchState.Watching |
        MatchState.GameReplayRequested | MatchState.GameReplaying)) != 0 && !match.IsParticipant)
      return null;
    string? opponent = match.Players.Select(player => player.Name)
      .FirstOrDefault(name => !string.Equals(name, username, StringComparison.OrdinalIgnoreCase));
    int games = match.Games.Count;
    return new(match.Id, opponent, games > 0 ? games : null,
      state.HasFlag(MatchState.Sideboarding) ? "Sideboarding"
        : state.HasFlag(MatchState.GameStarted) && !state.HasFlag(MatchState.GameCompleted)
          ? "Game in progress" : "Waiting for game",
      ReadOptional(() => ReadMatchClock(match, state)));
  }

  private static ActivityClockDTO? ReadRoundClock(Tournament tournament)
  {
    double duration = tournament.TotalRoundDuration.TotalSeconds;
    if (duration <= 0) return null;
    var roundEndTime = tournament.RoundEndTime;
    if (roundEndTime == default) return null;
    var end = roundEndTime.ToUniversalTime();
    var sampledAt = DateTime.UtcNow;
    double remaining = Math.Clamp((end - sampledAt).TotalSeconds, 0, duration);
    return new(sampledAt, duration, remaining, remaining > 0);
  }

  private static ActivityClockDTO? ReadMatchClock(Match match, MatchState state)
  {
    var game = match.CurrentGame ?? match.Games.LastOrDefault();
    int minutesPerPlayer = match.MinutesPerPlayer;
    if (game == null || minutesPerPlayer <= 0) return null;
    var gameStatus = game.Status;
    if (gameStatus is GameStatus.Invalid or GameStatus.NotStarted) return null;
    var players = game.Players.ToArray();
    if (players.Length == 0) return null;
    double duration = minutesPerPlayer * 60.0 * players.Length;
    double remaining = Math.Clamp(players.Sum(player => player.ChessClock.TotalSeconds), 0, duration);
    bool running = gameStatus == GameStatus.Started && state.HasFlag(MatchState.GameStarted) &&
      !state.HasFlag(MatchState.GameCompleted) && !state.HasFlag(MatchState.Sideboarding);
    return new(DateTime.UtcNow, duration, remaining, running);
  }

  private static T? ReadOptional<T>(Func<T> read)
  {
    try { return read(); }
    catch { return default; }
  }
}
