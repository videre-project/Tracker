/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Play.Tournaments;

using MTGOTracker.Controllers.Base;
using MTGOTracker.Database;
using MTGOTracker.Controllers.Models.Games;
using MTGOTracker.Services.MTGO;
using MTGOTracker.Services.MTGO.Events;


namespace MTGOTracker.Controllers;

[ApiController]
public sealed class GameStreamsController(
  EventContext context,
  ClientStateMonitor clientMonitor,
  IClientAPIProvider clientProvider) : APIController
{
  [HttpGet("/api/games/match/{matchId}/watch")]
  [ProducesResponseType(typeof(IEnumerable<GameLogDTO>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  [Produces("application/json", "application/x-ndjson")]
  public async Task<IActionResult> WatchMatchLogs(int matchId)
  {
    if (!clientMonitor.IsClientReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "MTGO client is not ready" });
    }

    var cachedGameIds = new HashSet<int>(await context.Games
        .Where(g => g.MatchId == matchId)
        .Select(g => g.Id)
        .ToListAsync());

    async Task gameLogCallback(object? sender, GameLogEntry entry)
    {
      if (!cachedGameIds.Contains(entry.GameId))
      {
         bool exists = await context.Games.AnyAsync(g => g.Id == entry.GameId && g.MatchId == matchId);
         if (exists) cachedGameIds.Add(entry.GameId);
         else return;
      }

      var dto = new GameLogDTO
      {
        Id = 0,
        GameId = entry.GameId,
        Timestamp = entry.Timestamp,
        GameLogType = entry.Type,
        Data = entry.Data,
        Nonce = entry.Nonce
      };

      await StreamResponse(new[] { dto });
    }

    return await StreamNdjsonEventHandler<GameLogEntry>(
      e => GameAPIService.GameLogReceived += e,
      e => GameAPIService.GameLogReceived -= e,
      gameLogCallback,
      clientMonitor.Token);
  }

  /// <summary>
  /// The IDs of events whose match history has changed for the signed-in player.
  /// </summary>
  [HttpGet("/api/games/history/watch")]
  [ProducesResponseType(typeof(IEnumerable<int>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  [Produces("application/x-ndjson")]
  public async Task<IActionResult> WatchMatchHistory()
  {
    if (!clientMonitor.IsClientReady)
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "MTGO client is not ready" });
    if (!clientProvider.TryGetCurrentUsername(out var username)) return BadRequest("Client not ready or user not logged in.");

    async Task historyCallback(object? sender, (int Id, bool IsMatch) update)
    {
      // Standings and round hooks also receive public tournaments the player never joined.
      if (sender is Tournament tournament && !tournament.HasJoined && !tournament.IsParticipant &&
          !await context.MatchesForPlayer(username).AnyAsync(match => match.EventId == update.Id, HttpContext.RequestAborted))
        return;

      int eventId = update.IsMatch
        ? await context.Matches.Where(match => match.Id == update.Id)
          .Select(match => match.EventId).SingleOrDefaultAsync(HttpContext.RequestAborted)
        : update.Id;
      if (eventId > 0) await StreamResponse(new[] { eventId });
    }

    EventHandler<int>? eventUpdate = null, matchUpdate = null;
    EventHandler<(Tournament Tournament, IList<StandingRecord> Standings)>? standingsUpdate = null;
    EventHandler<(Tournament Tournament, TournamentRound Round)>? roundUpdate = null;
    EventHandler<(Tournament Tournament, TournamentState State)>? stateUpdate = null;
    return await StreamNdjsonEventHandler<(int Id, bool IsMatch)>(
      e =>
      {
        eventUpdate = (sender, id) => e(sender, (id, false));
        matchUpdate = (sender, id) => e(sender, (id, true));
        standingsUpdate = (_, update) => e(update.Tournament, (update.Tournament.Id, false));
        roundUpdate = (_, update) => e(update.Tournament, (update.Tournament.Id, false));
        stateUpdate = (_, update) => e(update.Tournament, (update.Tournament.Id, false));
        GameAPIService.EventCreated += eventUpdate;
        GameAPIService.LeagueHistoryUpdated += eventUpdate;
        GameAPIService.MatchCreated += matchUpdate;
        GameAPIService.MatchUpdated += matchUpdate;
        GameAPIService.StandingsUpdated += standingsUpdate;
        GameAPIService.RoundUpdated += roundUpdate;
        GameAPIService.StateUpdated += stateUpdate;
      },
      e =>
      {
        GameAPIService.EventCreated -= eventUpdate;
        GameAPIService.LeagueHistoryUpdated -= eventUpdate;
        GameAPIService.MatchCreated -= matchUpdate;
        GameAPIService.MatchUpdated -= matchUpdate;
        GameAPIService.StandingsUpdated -= standingsUpdate;
        GameAPIService.RoundUpdated -= roundUpdate;
        GameAPIService.StateUpdated -= stateUpdate;
      },
      historyCallback,
      clientMonitor.Token);
  }
}
