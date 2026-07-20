/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

using MTGOSDK.API.Chat;

using Tracker.Database.Models.Events;
using Tracker.Controllers.Models.Games;


namespace Tracker.Services.MTGO.Events;

internal static class MatchHistorySerialization
{
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

    var statesOrdered = game.States.OrderBy(s => s.Timestamp).ToList();
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
    // timestamp, type priority ensures GameState → Action → ZoneChange → etc.
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
