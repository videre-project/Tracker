/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Tracker.Controllers.Base;
using Tracker.Database;
using Tracker.Database.Models;
using Tracker.Models.API.Replays;
using Tracker.Services.MTGO;
using Tracker.Services.MTGO.Events;


namespace Tracker.Controllers;

[ApiController]
public sealed class GameReplayController(EventContext context) : APIController
{  [HttpGet("/api/games/game/{gameId}/replay")]
  public async Task<ActionResult<ReplayDataDTO>> GetReplayData(int gameId)
  {
    // Flush any pending data for active games
    GameAPIService.FlushPendingGameData(gameId);

    var game = await context.Games
      .Include(g => g.Cards)
      .Include(g => g.Players)
      .Include(g => g.States)
        .ThenInclude(s => s.ZoneTransfers)
      .Include(g => g.States)
        .ThenInclude(s => s.CardChanges)
      .Include(g => g.States)
        .ThenInclude(s => s.PlayerChanges)
      .Include(g => g.States)
        .ThenInclude(s => s.Actions)
      .Include(g => g.States)
        .ThenInclude(s => s.Logs)
      .AsSplitQuery()
      .AsNoTracking()
      .FirstOrDefaultAsync(g => g.Id == gameId);

    if (game == null) return NotFound();

    return Ok(BuildReplayData(game));
  }

  private static ReplayDataDTO BuildReplayData(GameModel game)
  {
    var statesOrdered = game.States.OrderBy(s => s.Timestamp).ToList();

    // Map state DB IDs to sequential indices for firstSeenSnapshotIndex
    var stateIdToIndex = new Dictionary<int, int>();
    for (int i = 0; i < statesOrdered.Count; i++)
      stateIdToIndex[statesOrdered[i].Id] = i;

    var cards = game.Cards.Select(c => new ReplayCardDTO
    {
      CardId = c.CardId,
      Name = c.Name,
      RulesText = c.RulesText,
      ManaCost = c.ManaCost,
      CatalogId = c.CatalogId,
      InitialZone = c.InitialZone,
      InitialPower = c.InitialPower,
      InitialToughness = c.InitialToughness,
      OwnerId = c.OwnerId,
      SourceId = c.SourceId,
      IsTapped = c.IsTapped,
      IsToken = c.IsToken,
      IsLand = c.IsLand,
      IsActivatedAbility = c.IsActivatedAbility,
      IsTriggeredAbility = c.IsTriggeredAbility,
      FirstSeenSnapshotIndex = stateIdToIndex.GetValueOrDefault(
        c.FirstSeenStateId, 0)
    }).ToList();

    var players = game.Players.Select(p => new ReplayPlayerDTO
    {
      PlayerIndex = p.PlayerIndex,
      Name = p.Name,
      PlayDraw = p.PlayDraw,
      InitialLife = p.InitialLife,
      InitialHandCount = p.InitialHandCount,
      InitialLibraryCount = p.InitialLibraryCount,
      InitialGraveyardCount = p.InitialGraveyardCount,
      InitialManaPool = p.InitialManaPool,
      IsActivePlayer = p.IsActivePlayer,
      ClockRemaining = p.ClockRemaining,
      UserId = p.UserId,
      AvatarId = p.AvatarId
    }).ToList();

    int previousNonce = 0;
    var snapshots = new List<ReplaySnapshotDTO>();
    for (int i = 0; i < statesOrdered.Count; i++)
    {
      var state = statesOrdered[i];

      // Actions belong to the previous nonce (see BuildGameLogs)
      var actionNonce = previousNonce != 0 ? previousNonce : state.Nonce;

      snapshots.Add(new ReplaySnapshotDTO
      {
        Index = i,
        Nonce = state.Nonce,
        Timestamp = state.ClientTimestamp,
        TurnNumber = state.TurnNumber,
        CurrentPhase = state.CurrentPhase,
        PromptedPlayer = state.PromptedPlayer,
        PromptText = state.PromptText,
        PromptOptions = state.PromptOptions,
        ZoneTransfers = state.ZoneTransfers.Select(zt =>
          new ZoneTransferData
          {
            CardId = zt.CardId, CardName = zt.CardName,
            FromZone = zt.FromZone, ToZone = zt.ToZone,
            SourceId = zt.SourceId, Type = zt.Type
          }).ToList(),
        CardChanges = state.CardChanges.Select(cc =>
          new CardChangeData
          {
            CardId = cc.CardId, CardName = cc.CardName,
            Property = cc.Property, OldValue = cc.OldValue,
            NewValue = cc.NewValue
          }).ToList(),
        PlayerChanges = state.PlayerChanges.Select(pc =>
          new PlayerChangeData
          {
            PlayerIndex = pc.PlayerIndex, PlayerName = pc.PlayerName,
            Property = pc.Property, OldValue = pc.OldValue,
            NewValue = pc.NewValue
          }).ToList(),
        Actions = state.Actions.Select(a => new ReplayActionDTO
        {
          ActionType = a.ActionType,
          ActionName = a.ActionName,
          CardId = a.CardId,
          CardName = a.CardName,
          Targets = a.Targets,
          Data = a.Data,
          ClientTimestamp = a.ClientTimestamp,
          Nonce = actionNonce
        }).ToList(),
        Logs = state.Logs.Select(l => new ReplayLogDTO
        {
          Timestamp = l.Timestamp,
          Data = l.Data
        }).ToList()
      });

      previousNonce = state.Nonce;
    }

    return new ReplayDataDTO
    {
      GameId = game.Id,
      Players = players,
      Cards = cards,
      Snapshots = snapshots
    };
  }

}
