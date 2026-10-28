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
  // Only joined events belong in the sidebar; exclude public listings and spectated matches.
  internal static PersonalActivityDTO[] Read(string username)
  {
    var activities = new Dictionary<string, PersonalActivityDTO>();
    var standaloneMatches = new List<Match>();
    var eventMatchIds = new HashSet<int>();
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
          standaloneMatches.Add(match);
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
        if (activity.Match != null) eventMatchIds.Add(activity.Match.Id);
      }
      catch (Exception exception)
      {
        // An event may disappear during enumeration as MTGO leaves or finishes it.
        Log.Debug(exception, "Could not read a joined event for the activity sidebar");
      }
    }
    foreach (var match in standaloneMatches)
    {
      try
      {
        if (eventMatchIds.Contains(match.Id)) continue;
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

  internal static PersonalActivityDTO? ReadTournament(Tournament tournament, string username,
    bool includeCompleted = false)
  {
    var state = tournament.State;
    bool completed = state == TournamentState.Finished || tournament.IsCompleted;
    if (completed && !includeCompleted) return null;
    bool registered = state is TournamentState.WaitingToStart or TournamentState.NotSet;
    string status = "active";
    if (completed) status = "completed";
    else if (registered) status = "registered";
    string label = state switch
    {
      TournamentState.Drafting => "Drafting",
      TournamentState.Deckbuilding => "Deckbuilding",
      TournamentState.DeckbuildingDeckSubmitted => "Deck submitted",
      TournamentState.WaitingForFirstRoundToStart => "Waiting for round 1",
      TournamentState.RoundInProgress => tournament.HasBye ? "Bye this round" : "Round in progress",
      TournamentState.BetweenRounds => "Between rounds",
      _ when completed => "Finished",
      _ when registered => "Registered",
      _ => "Starting",
    };
    int completedRounds = state switch
    {
      TournamentState.BetweenRounds => tournament.RoundNumber,
      TournamentState.RoundInProgress => Math.Max(0, tournament.RoundNumber - 1),
      _ => 0,
    };
    return new($"tournament-{tournament.Id}", tournament.Id, "tournament",
      tournament.Description, tournament.Format.ToString(), status,
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
      CompletedRounds: completedRounds,
      RoundInProgress: state == TournamentState.RoundInProgress,
      RoundClock: state == TournamentState.RoundInProgress ? ReadOptional(() => ReadRoundClock(tournament)) : null);
  }

  internal static PersonalActivityDTO? ReadLeague(League league, string username,
    bool includeCompleted = false)
  {
    if (league.CourseId <= 0 || league.IsCompleted && !includeCompleted) return null;
    var match = ReadMatch(league.ActiveMatch, username);
    bool completed = league.MatchesRemaining <= 0 && match == null;
    if (completed && !includeCompleted) return null;
    string label = "Ready for next match";
    if (completed) label = "Finished";
    else if (league.IsWaitingInMatchQueue) label = "Finding a match";
    else if (league.IsPaused) label = "Paused";
    return new($"league-{league.Id}-{league.CourseId}", league.Id, "league", league.Name,
      league.Format.ToString(), completed ? "completed" : "active", label,
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
    string status = "Waiting for game";
    if (state.HasFlag(MatchState.Sideboarding)) status = "Sideboarding";
    else if (state.HasFlag(MatchState.GameStarted) && !state.HasFlag(MatchState.GameCompleted))
      status = "Game in progress";
    int games = match.Games.Count;
    return new(match.Id, opponent, games > 0 ? games : null, status,
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
