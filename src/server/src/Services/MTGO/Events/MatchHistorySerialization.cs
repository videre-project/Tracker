/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;

using MTGOSDK.API.Chat;
using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;

using MTGOTracker.Services.MTGO.Collection;
using MTGOTracker.Database;
using MTGOTracker.Database.Models.Events;
using MTGOTracker.Controllers.Models.Games;


namespace MTGOTracker.Services.MTGO.Events;

internal static class MatchHistorySerialization
{
  private const string SkipDraw = " skips their draw step";
  private const string PlayFirst = " chooses to play first";
  private const string DrawFirst = " chooses to draw first";
  private const string PlaySecond = " chooses to play second";

  /// <summary>
  /// Loads relevant log states for games without recorded play/draw metadata.
  /// </summary>
  internal static async Task ReadPlayDrawAsync(EventContext context, IEnumerable<GameModel> source,
    string username, CancellationToken cancellationToken = default)
  {
    var games = source.Where(game => ResolvePlayDraw(game, username) == "Unknown").ToList();
    if (games.Count == 0) return;
    var ids = games.Select(game => game.Id).ToArray();
    var states = await context.GameStates.AsNoTracking()
      .Where(state => ids.Contains(state.GameId) && state.Logs.Any(log =>
        EF.Functions.Like(log.Data, "%" + SkipDraw + "%") ||
        EF.Functions.Like(log.Data, "%" + PlayFirst + "%") ||
        EF.Functions.Like(log.Data, "%" + DrawFirst + "%") ||
        EF.Functions.Like(log.Data, "%" + PlaySecond + "%")))
      .Include(state => state.Logs).AsSplitQuery().ToListAsync(cancellationToken);
    var byGame = states.ToLookup(state => state.GameId);
    foreach (var game in games) game.States = byGame[game.Id].ToList();
  }

  internal static string ResolvePlayDraw(
    GameModel game,
    string? playerUsername,
    PlayDrawResult? existingResult = null)
  {
    if (string.IsNullOrWhiteSpace(playerUsername))
    {
      playerUsername = game.GamePlayerResults.FirstOrDefault()?.Player
        ?? game.Players.FirstOrDefault()?.Name;
    }

    // Saved SDK results are authoritative; legacy logs fill missing metadata.
    existingResult ??= game.GamePlayerResults.FirstOrDefault(result =>
      string.Equals(result.Player, playerUsername, StringComparison.OrdinalIgnoreCase))?.PlayDraw;
    if (existingResult is PlayDrawResult.Play or PlayDrawResult.Draw) return existingResult.ToString()!;
    var recorded = game.Players.FirstOrDefault(player =>
      string.Equals(player.Name, playerUsername, StringComparison.OrdinalIgnoreCase))?.PlayDraw;
    if (recorded is "Play" or "Draw") return recorded;

    if (game.States != null && !string.IsNullOrWhiteSpace(playerUsername))
    {
      var players = game.GamePlayerResults.Select(result => result.Player)
        .Concat(game.Players.Select(player => player.Name))
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Append(playerUsername)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
      (string Phrase, bool ActorPlays)[] actions = [
        (SkipDraw, true), (PlayFirst, true), (DrawFirst, false), (PlaySecond, false)
      ];
      // Prefer the latest saved decision over earlier log entries.
      var statesReversed = game.States.OrderByDescending(s => s.Id).ToList();

      foreach (var state in statesReversed)
      {
        if (state.Logs == null) continue;
        foreach (var log in state.Logs.OrderByDescending(l => l.Id))
        {
          if (string.IsNullOrEmpty(log.Data)) continue;
          foreach (var (action, actorPlays) in actions)
          {
            int index = log.Data.IndexOf(action, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            string actor = log.Data[..index].Trim();
            // MTGO prefixes player names in log messages with its @P markup.
            if (actor.StartsWith("@P", StringComparison.Ordinal)) actor = actor[2..];
            bool isPlayer = string.Equals(actor, playerUsername, StringComparison.OrdinalIgnoreCase);
            if (!isPlayer && (players.Count != 2 || !players.Contains(actor))) continue;
            return actorPlays == isPlayer ? "Play" : "Draw";
          }
        }
      }
    }

    // Winning the die roll only determines who chooses, not the choice itself.
    return "Unknown";
  }

  internal static WinLossRecord GameRecord(IEnumerable<GameModel> games, string username)
  {
    var results = games.SelectMany(game => game.GamePlayerResults)
      .Where(result => string.Equals(result.Player, username, StringComparison.OrdinalIgnoreCase)).ToList();
    return new(results.Count(result => result.Result == GameResult.Win),
      results.Count(result => result.Result == GameResult.Loss), 0);
  }

  internal static MatchHistoryDTO ToMatchHistory(MatchModel match, string username, DeckRevisionView? deck,
    bool isActive, IReadOnlyDictionary<int, TimeSpan> durations)
  {
    var record = GameRecord(match.Games, username);
    var duration = match.Games.Aggregate(TimeSpan.Zero, (total, game) => total + GetGameDuration(game,
      game.GamePlayerResults.FirstOrDefault(result => string.Equals(result.Player, username, StringComparison.OrdinalIgnoreCase)), durations));
    return new()
    {
      Id = match.Id, EventId = match.EventId, EventName = match.Event.Description, Format = match.Event.Format,
      StartTime = match.Event.StartTime, Result = GetMatchResult(match, username, isActive),
      Record = $"{record.Wins}-{record.Losses}", Duration = FormatDuration(duration),
      DeckRevisionId = match.Event.DeckRevisionId, DeckName = deck?.Name, DeckArchetype = deck?.Archetype,
      DeckColors = deck?.Colors,
      OpponentName = GetOpponentName(match, username), OpponentDeckArchetype = match.OpponentDeckArchetype,
      OpponentDeckColors = match.OpponentDeckColors, IsActive = isActive
    };
  }

  internal static async Task<Dictionary<int, TimeSpan>> ReadGameDurationsAsync(
    EventContext context, IEnumerable<int> gameIds)
  {
    int[] ids = gameIds.Distinct().ToArray();
    var ranges = await context.GameStates
      .Where(state => ids.Contains(state.GameId))
      .GroupBy(state => state.GameId)
      .Select(group => new
      {
        GameId = group.Key,
        Start = group.Min(state => state.ClientTimestamp),
        End = group.Max(state => state.ClientTimestamp)
      })
      .ToListAsync();

    return ranges.ToDictionary(range => range.GameId, range => range.End - range.Start);
  }

  internal static TimeSpan GetGameDuration(
    GameModel game, GamePlayerResult? result,
    IReadOnlyDictionary<int, TimeSpan>? recordedDurations = null)
  {
    TimeSpan recorded = TimeSpan.Zero;
    if (recordedDurations != null)
      recordedDurations.TryGetValue(game.Id, out recorded);
    else if (game.States.Count >= 2)
      recorded = game.States.Max(state => state.ClientTimestamp)
        - game.States.Min(state => state.ClientTimestamp);

    if (recorded > TimeSpan.Zero) return recorded;
    return result?.Clock > TimeSpan.Zero ? result.Clock : TimeSpan.Zero;
  }

  internal static string GetMatchResult(MatchModel match, string currentUser, bool isActive)
  {
    var result = match.PlayerResults.FirstOrDefault(player => string.Equals(player.Player, currentUser, StringComparison.OrdinalIgnoreCase));
    if (result != null && result.Result != MatchResult.NotSet) return result.Result.ToString();
    if (isActive) return "In Progress";
    return match.Games.Count == 0 ? "Cancelled" : "Incomplete";
  }

  internal static string FormatDuration(TimeSpan duration) =>
    $"{Math.Floor(duration.TotalMinutes)}m {duration.Seconds}s";

  internal static bool IsDifferentPlayer(string? player, string currentUser) =>
    !string.IsNullOrWhiteSpace(player) &&
    !string.Equals(player, currentUser, StringComparison.OrdinalIgnoreCase);

  internal static string? GetOpponentName(MatchModel match, string currentUser)
  {
    return match.PlayerResults
        .Select(result => result.Player)
        .FirstOrDefault(player => IsDifferentPlayer(player, currentUser))
      ?? match.Games
        .SelectMany(game => game.GamePlayerResults)
        .Select(result => result.Player)
        .FirstOrDefault(player => IsDifferentPlayer(player, currentUser))
      ?? match.Games
        .SelectMany(game => game.Players)
        .OrderBy(player => player.PlayerIndex)
        .Select(player => player.Name)
        .FirstOrDefault(player => IsDifferentPlayer(player, currentUser));
  }

  internal static List<GameLogDTO> BuildGameLogs(GameModel game)
  {
    var logs = new List<GameLogDTO>();
    int syntheticId = -1;
    int lastTurn = 0;
    string lastPhase = "";

    var statesOrdered = game.States.OrderBy(s => s.Timestamp).ThenBy(s => s.Id).ToList();
    int previousNonce = 0;

    foreach (var state in statesOrdered)
    {
      var timestamp = state.ClientTimestamp.ToLocalTime();

      // GameState (turn/phase changes)
      if (state.TurnNumber != lastTurn || state.CurrentPhase != lastPhase)
      {
        logs.Add(new GameLogDTO
        {
          Id = syntheticId--,
          GameId = game.Id,
          Timestamp = timestamp,
          GameLogType = GameLogType.GameState,
          Nonce = state.Nonce,
          Data = JsonSerializer.Serialize(new GameStateData
          {
            Turn = state.TurnNumber,
            Phase = state.CurrentPhase,
            PreviousTurn = lastTurn,
            PreviousPhase = lastPhase
          }, JsonSerializerOptions.Web)
        });
        lastTurn = state.TurnNumber;
        lastPhase = state.CurrentPhase;
      }

      // Zone transfers — split reveal (toZone/fromZone == "Revealed") from regular
      var revealTransfers = state.ZoneTransfers
        .Where(zt => zt.ToZone == "Revealed" || zt.FromZone == "Revealed")
        .ToList();
      var regularTransfers = state.ZoneTransfers
        .Where(zt => zt.ToZone != "Revealed" && zt.FromZone != "Revealed")
        .ToList();

      if (revealTransfers.Count > 0)
      {
        logs.Add(new GameLogDTO
        {
          Id = syntheticId--,
          GameId = game.Id,
          Timestamp = timestamp,
          GameLogType = GameLogType.Reveal,
          Nonce = state.Nonce,
          Data = JsonSerializer.Serialize(revealTransfers.Select(zt =>
            new ZoneTransferData
            {
              CardId = zt.CardId, CardName = zt.CardName,
              FromZone = zt.FromZone, ToZone = zt.ToZone,
              SourceId = zt.SourceId, Type = zt.Type
            }), JsonSerializerOptions.Web)
        });
      }

      if (regularTransfers.Count > 0)
      {
        logs.Add(new GameLogDTO
        {
          Id = syntheticId--,
          GameId = game.Id,
          Timestamp = timestamp,
          GameLogType = GameLogType.ZoneChange,
          Nonce = state.Nonce,
          Data = JsonSerializer.Serialize(regularTransfers.Select(zt =>
            new ZoneTransferData
            {
              CardId = zt.CardId, CardName = zt.CardName,
              FromZone = zt.FromZone, ToZone = zt.ToZone,
              SourceId = zt.SourceId, Type = zt.Type
            }), JsonSerializerOptions.Web)
        });
      }

      // Card property changes
      if (state.CardChanges.Count > 0)
      {
        logs.Add(new GameLogDTO
        {
          Id = syntheticId--,
          GameId = game.Id,
          Timestamp = timestamp,
          GameLogType = GameLogType.CardChange,
          Nonce = state.Nonce,
          Data = JsonSerializer.Serialize(state.CardChanges.Select(cc =>
            new CardChangeData
            {
              CardId = cc.CardId, CardName = cc.CardName,
              Property = cc.Property, OldValue = cc.OldValue,
              NewValue = cc.NewValue
            }), JsonSerializerOptions.Web)
        });
      }

      // Player property changes
      if (state.PlayerChanges.Count > 0)
      {
        logs.Add(new GameLogDTO
        {
          Id = syntheticId--,
          GameId = game.Id,
          Timestamp = timestamp,
          GameLogType = GameLogType.PlayerChange,
          Nonce = state.Nonce,
          Data = JsonSerializer.Serialize(state.PlayerChanges.Select(pc =>
            new PlayerChangeData
            {
              PlayerIndex = pc.PlayerIndex, PlayerName = pc.PlayerName,
              Property = pc.Property, OldValue = pc.OldValue,
              NewValue = pc.NewValue
            }), JsonSerializerOptions.Web)
        });
      }

      // ActionProcessor only finalizes pending actions on TurnStep
      // boundaries, so every action stored under this state was actually
      // performed during the *previous* state. Assign the previous nonce
      // so it groups with the state it was executed in, and use the
      // action's own client timestamp for accurate temporal placement.
      foreach (var action in state.Actions)
      {
        var actionTs = action.ClientTimestamp != default
          ? action.ClientTimestamp.ToLocalTime()
          : timestamp;
        var actionNonce = previousNonce != 0
            ? previousNonce
            : state.Nonce;

        logs.Add(new GameLogDTO
        {
          Id = syntheticId--,
          GameId = game.Id,
          Timestamp = actionTs,
          GameLogType = GameLogType.GameAction,
          Nonce = actionNonce,
          Data = action.Data
        });
      }

      // Log messages
      foreach (var log in state.Logs)
      {
        logs.Add(new GameLogDTO
        {
          Id = log.Id,
          GameId = game.Id,
          Timestamp = log.Timestamp.ToLocalTime(),
          GameLogType = Enum.Parse<GameLogType>(log.GameLogType),
          Nonce = state.Nonce,
          Data = ChatTextNormalizer.Normalize(log.Data)
        });
      }

      previousNonce = state.Nonce;
    }

    // Timestamp-primary sort: all entries within a nonce group share the
    // state's ClientTimestamp, so they'll be adjacent. Reassigned actions
    // (whose ClientTimestamp predates the new state) sort between their
    // original nonce group and the new state's header. Within the same
    // timestamp, type priority ensures GameState -> Action -> ZoneChange -> etc.
    return logs
      .OrderBy(l => l.Timestamp)
      .ThenBy(l => TypeOrder(l.GameLogType))
      .ToList();

    // Action before state changes: the action is the cause, zone/card/player
    // changes are the effect.
    static int TypeOrder(GameLogType t) => t switch
    {
      GameLogType.GameState    => 0,
      GameLogType.GameAction   => 1,
      GameLogType.ZoneChange   => 2,
      GameLogType.Reveal       => 2,
      GameLogType.CardChange   => 3,
      GameLogType.PlayerChange => 4,
      GameLogType.LogMessage   => 5,
      _                        => 6,
    };
  }
}
