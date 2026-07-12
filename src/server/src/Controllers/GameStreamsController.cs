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
using Microsoft.Extensions.DependencyInjection;

using MTGOSDK.API.Play.Games;

using Tracker.Controllers.Base;
using Tracker.Database;
using Tracker.Models.API.Games;
using Tracker.Services.MTGO;
using Tracker.Services.MTGO.Events;

using static Tracker.Services.Games.MatchHistorySerialization;


namespace Tracker.Controllers;

[ApiController]
public sealed class GameStreamsController(
  EventContext context,
  ClientStateMonitor clientMonitor,
  IClientAPIProvider clientProvider) : APIController
{  [HttpGet("/api/games/match/{matchId}/watch")]
  [ProducesResponseType(typeof(IEnumerable<GameLogDTO>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  [Produces("application/x-ndjson")]
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

  [HttpGet("/api/games/history/watch")]
  [ProducesResponseType(typeof(IEnumerable<MatchHistoryDTO>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  [Produces("application/x-ndjson")]
  public async Task<IActionResult> WatchMatchHistory()
  {
    if (!clientMonitor.IsClientReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "MTGO client is not ready" });
    }

    if (!clientProvider.TryGetCurrentUsername(out var currentUser))
    {
      return BadRequest("Client not ready or user not logged in.");
    }

    async Task historyCallback(object? sender, int id)
    {
      // Use a fresh DbContext scope to ensure we see the latest committed data
      using var scope = HttpContext.RequestServices.CreateScope();
      var freshContext = scope.ServiceProvider.GetRequiredService<EventContext>();

      // Try as match first (covers MatchCreated + MatchResultUpdated)
      var match = await freshContext.Matches
        .Include(m => m.Event)
          .ThenInclude(e => e.Deck)
        .Include(m => m.Games)
          .ThenInclude(g => g.Players)
        .AsNoTracking()
        .FirstOrDefaultAsync(m => m.Id == id);

      if (match != null)
      {
        bool isActive = GameAPIService.ActiveMatchIds.Contains(id);
        var playerResult = match.PlayerResults.FirstOrDefault(p => p.Player == currentUser);

        TimeSpan matchDuration = TimeSpan.Zero;
        int wins = 0, losses = 0;
        foreach (var game in match.Games)
        {
          var gameResult = game.GamePlayerResults.FirstOrDefault(p => p.Player == currentUser);
          if (gameResult != null)
          {
            matchDuration += gameResult.Clock;
            if (gameResult.Result == GameResult.Win) wins++;
            else if (gameResult.Result == GameResult.Loss) losses++;
          }
        }

        var dto = new MatchHistoryDTO
        {
          Id = match.Id,
          EventId = match.EventId,
          EventName = match.Event?.Description ?? "",
          Format = match.Event?.Format ?? "",
          StartTime = match.Event?.StartTime ?? DateTime.MinValue,
          Result = playerResult?.Result.ToString() ?? "In Progress",
          Record = $"{wins}-{losses}",
          Duration = $"{Math.Floor(matchDuration.TotalMinutes)}m {matchDuration.Seconds}s",
          DeckHash = match.Event?.DeckHash,
          DeckName = match.Event?.Deck?.Name,
          DeckColors = GetDeckColors(match.Event?.Deck),
          OpponentName = GetOpponentName(match, currentUser),
          IsActive = isActive
        };

        await StreamResponse(new[] { dto });
        return;
      }

      // Fall back to event lookup (tournament just joined, no matches yet)
      var evt = await freshContext.Events
        .Include(e => e.Deck)
        .AsNoTracking()
        .FirstOrDefaultAsync(e => e.Id == id);

      if (evt != null)
      {
        var dto = new MatchHistoryDTO
        {
          Id = 0,
          EventId = evt.Id,
          EventName = evt.Description,
          Format = evt.Format,
          StartTime = evt.StartTime,
          Result = "In Progress",
          Record = "",
          Duration = "",
          DeckHash = evt.DeckHash,
          DeckName = evt.Deck?.Name,
          DeckColors = GetDeckColors(evt.Deck),
          IsActive = true,
          IsEvent = true
        };

        await StreamResponse(new[] { dto });
      }
    }

    return await StreamNdjsonEventHandler<int>(
      e =>
      {
        GameAPIService.EventCreated += e;
        GameAPIService.MatchCreated += e;
        GameAPIService.MatchResultUpdated += e;
      },
      e =>
      {
        GameAPIService.EventCreated -= e;
        GameAPIService.MatchCreated -= e;
        GameAPIService.MatchResultUpdated -= e;
      },
      historyCallback,
      clientMonitor.Token);
  }


}
