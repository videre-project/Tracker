/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;

using Tracker.Controllers.Base;
using Tracker.Database;
using Tracker.Database.Models;
using Tracker.Models.API.Games;
using Tracker.Services.MTGO;
using Tracker.Services.MTGO.Events;
using static Tracker.Services.Games.MatchHistorySerialization;

namespace Tracker.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController : APIController
{
  private readonly EventContext context;
  private readonly IClientAPIProvider clientProvider;

  public GamesController(EventContext context, IClientAPIProvider clientProvider)
  {
    this.context = context;
    this.clientProvider = clientProvider;
  }

  [HttpGet("history")]
  public async Task<ActionResult<PaginatedMatchesDTO>> GetMatchHistory(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 50,
    [FromQuery] DateTime? minDate = null,
    [FromQuery] DateTime? maxDate = null,
    [FromQuery] string? format = null,
    [FromQuery] string? deckHash = null)
  {
    if (!clientProvider.TryGetCurrentUsername(out var currentUser))
    {
      return BadRequest("Client not ready or user not logged in.");
    }

    // Validate and clamp pagination
    page = Math.Max(1, page);
    pageSize = Math.Clamp(pageSize, 1, 200);

    // Base query
    var query = context.Matches
      .FromSqlRaw(@"
          SELECT m.* FROM Matches m
          WHERE EXISTS (
            SELECT 1 FROM json_each(m.PlayerResults)
            WHERE json_extract(value, '$.player') = {0}
          )", currentUser)
      .Include(m => m.Event)
        .ThenInclude(e => e.Deck)
      .Include(m => m.Games)
      .AsSplitQuery()
      .AsNoTracking()
      .AsQueryable();

    // Filters
    if (minDate.HasValue)
    {
      query = query.Where(m => m.Event.StartTime >= minDate.Value);
    }
    if (maxDate.HasValue)
    {
      query = query.Where(m => m.Event.StartTime <= maxDate.Value);
    }
    if (!string.IsNullOrEmpty(format))
    {
      query = query.Where(m => m.Event.Format == format);
    }
    if (!string.IsNullOrEmpty(deckHash))
    {
      query = query.Where(m => m.Event.DeckHash == deckHash);
    }

    // Get total count before pagination
    int totalCount = await query.CountAsync();
    int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

    // Pagination and ordering (newest first)
    var matches = await query
      .OrderByDescending(m => m.Event.StartTime)
        .ThenByDescending(m => m.Id)
      .Skip((page - 1) * pageSize)
      .Take(pageSize)
      .ToListAsync();

    // Map to DTOs
    var matchDTOs = new List<MatchHistoryDTO>();
    foreach (var match in matches)
    {
      var playerResult = match.PlayerResults.FirstOrDefault(p => p.Player == currentUser);
      
      TimeSpan matchDuration = TimeSpan.Zero;
      foreach (var game in match.Games)
      {
         var gameResult = game.GamePlayerResults.FirstOrDefault(p => p.Player == currentUser);
         if (gameResult != null)
         {
           matchDuration += gameResult.Clock;
         }
      }

      int wins = 0;
      int losses = 0;

      // Calculate wins/losses from games to show e.g. "2-1"
      foreach(var game in match.Games)
      {
         var gameResult = game.GamePlayerResults.FirstOrDefault(p => p.Player == currentUser);
         if (gameResult != null && gameResult.Result == GameResult.Win) wins++;
         else if (gameResult != null && gameResult.Result == GameResult.Loss) losses++;
      }

      matchDTOs.Add(new MatchHistoryDTO
      {
        Id = match.Id,
        EventId = match.EventId,
        EventName = match.Event.Description,
        Format = match.Event.Format,
        StartTime = match.Event.StartTime,
        Result = playerResult?.Result.ToString() ?? "Unknown",
        Record = $"{wins}-{losses}",
        Duration = FormatDuration(matchDuration),
        DeckHash = match.Event.DeckHash,
        DeckName = match.Event.Deck?.Name,
        DeckColors = GetDeckColors(match.Event.Deck),
        OpponentName = GetOpponentName(match, currentUser)
      });
    }

    // On page 1, merge active (in-progress) matches and events
    if (page == 1)
    {
      var existingMatchIds = new HashSet<int>(matchDTOs.Select(m => m.Id));
      var existingEventIds = new HashSet<int>(matchDTOs.Select(m => m.EventId));

      // Active matches not yet in the completed results
      var activeMatchIds = GameAPIService.ActiveMatchIds
        .Where(id => !existingMatchIds.Contains(id))
        .ToList();

      if (activeMatchIds.Count > 0)
      {
        var activeMatches = await context.Matches
          .Where(m => activeMatchIds.Contains(m.Id))
          .Where(m => string.IsNullOrEmpty(deckHash) || m.Event.DeckHash == deckHash)
          .Include(m => m.Event).ThenInclude(e => e.Deck)
          .Include(m => m.Games)
            .ThenInclude(g => g.Players)
          .AsSplitQuery().AsNoTracking()
          .ToListAsync();

        foreach (var match in activeMatches)
        {
          TimeSpan dur = TimeSpan.Zero;
          int w = 0, l = 0;
          foreach (var game in match.Games)
          {
            var gr = game.GamePlayerResults.FirstOrDefault(p => p.Player == currentUser);
            if (gr != null)
            {
              dur += gr.Clock;
              if (gr.Result == GameResult.Win) w++;
              else if (gr.Result == GameResult.Loss) l++;
            }
          }
          var pr = match.PlayerResults.FirstOrDefault(p => p.Player == currentUser);
          matchDTOs.Add(new MatchHistoryDTO
          {
            Id = match.Id,
            EventId = match.EventId,
            EventName = match.Event?.Description ?? "",
            Format = match.Event?.Format ?? "",
            StartTime = match.Event?.StartTime ?? DateTime.Now,
            Result = pr?.Result.ToString() ?? "In Progress",
            Record = $"{w}-{l}",
            Duration = FormatDuration(dur),
            DeckHash = match.Event?.DeckHash,
            DeckName = match.Event?.Deck?.Name,
            DeckColors = GetDeckColors(match.Event?.Deck),
            OpponentName = GetOpponentName(match, currentUser),
            IsActive = true
          });
          existingEventIds.Add(match.EventId);
        }
      }

      // Active events (tournaments) not yet represented
      var activeEventIds = GameAPIService.ActiveEventIds
        .Where(id => !existingEventIds.Contains(id))
        .ToList();

      if (activeEventIds.Count > 0)
      {
        var activeEvents = await context.Events
          .Where(e => activeEventIds.Contains(e.Id))
          .Where(e => string.IsNullOrEmpty(deckHash) || e.DeckHash == deckHash)
          .Include(e => e.Deck)
          .AsNoTracking()
          .ToListAsync();

        foreach (var evt in activeEvents)
        {
          matchDTOs.Add(new MatchHistoryDTO
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
          });
        }
      }
    }

    // Group matches by EventId into tournament parent rows
    var grouped = new List<MatchHistoryDTO>();
    foreach (var group in matchDTOs.GroupBy(m => m.EventId))
    {
      var items = group.OrderByDescending(m => m.Id).ToList();

      // Check if this is a standalone match (eventId == matchId, single item)
      if (items.Count == 1 && items[0].Id == items[0].EventId && !items[0].IsEvent)
      {
        grouped.Add(items[0]);
        continue;
      }

      // Find or build the parent event row
      var parentRow = items.FirstOrDefault(m => m.IsEvent);
      var childMatches = items.Where(m => !m.IsEvent).ToList();

      if (parentRow == null)
      {
        // No explicit event row — build one from the first child match
        var first = childMatches.First();
        int totalWins = 0, totalLosses = 0;
        TimeSpan totalDuration = TimeSpan.Zero;
        bool anyActive = false;
        foreach (var child in childMatches)
        {
          var parts = child.Record.Split('-');
          if (parts.Length == 2)
          {
            if (int.TryParse(parts[0], out int cw)) totalWins += cw;
            if (int.TryParse(parts[1], out int cl)) totalLosses += cl;
          }
          if (TimeSpan.TryParseExact(child.Duration.Replace("m ", ":").Replace("s", ""),
              @"m\:ss", null, out var d)) totalDuration += d;
          if (child.IsActive) anyActive = true;
        }

        parentRow = new MatchHistoryDTO
        {
          Id = 0,
          EventId = first.EventId,
          EventName = first.EventName,
          Format = first.Format,
          StartTime = first.StartTime,
          Result = anyActive ? "In Progress" : $"{totalWins}-{totalLosses}",
          Record = $"{totalWins}-{totalLosses}",
          Duration = FormatDuration(totalDuration),
          DeckHash = first.DeckHash,
          DeckName = first.DeckName,
          DeckColors = first.DeckColors,
          IsActive = anyActive || GameAPIService.ActiveEventIds.Contains(first.EventId),
          IsEvent = true,
          Matches = childMatches
        };
      }
      else
      {
        parentRow.Matches = childMatches;
        if (childMatches.Any(c => c.IsActive))
        {
          parentRow.IsActive = true;
          parentRow.Result = "In Progress";
        }
      }

      grouped.Add(parentRow);
    }

    // Sort by StartTime descending (active items first)
    var result = grouped
      .OrderByDescending(m => m.IsActive)
      .ThenByDescending(m => m.StartTime)
      .ToList();

    // Set pagination headers (similar to EventsController)
    Response.Headers["X-Page"] = page.ToString();
    Response.Headers["X-Page-Size"] = pageSize.ToString();
    Response.Headers["X-Total-Count"] = totalCount.ToString();
    Response.Headers["X-Total-Pages"] = totalPages.ToString();
    Response.Headers["X-Has-Next-Page"] = (page < totalPages).ToString();
    Response.Headers["X-Has-Previous-Page"] = (page > 1).ToString();

    return Ok(new PaginatedMatchesDTO
    {
      Items = result,
      TotalCount = totalCount,
      Page = page,
      PageSize = pageSize,
      TotalPages = totalPages
    });
  }

  [HttpGet("match/{matchId}")]
  public async Task<ActionResult<MatchDetailsDTO>> GetMatchDetails(int matchId)
  {
    if (!clientProvider.TryGetCurrentUsername(out var currentUser))
    {
      return BadRequest("Client not ready or user not logged in.");
    }

    // Flush any pending (unflushed) game data for active games in this match
    // so the DB query below sees the latest state.
    if (GameAPIService.ActiveMatchIds.Contains(matchId))
    {
      var activeGameIds = await context.Games
        .Where(g => g.MatchId == matchId)
        .Select(g => g.Id)
        .ToListAsync();
      foreach (var gid in activeGameIds)
        GameAPIService.FlushPendingGameData(gid);
    }

    var match = await context.Matches
      .Include(m => m.Event)
        .ThenInclude(e => e.Deck)
      .Include(m => m.Games)
        .ThenInclude(g => g.Players)
      .Include(m => m.Games)
        .ThenInclude(g => g.States)
          .ThenInclude(s => s.Logs)
      .Include(m => m.Games)
        .ThenInclude(g => g.States)
          .ThenInclude(s => s.ZoneTransfers)
      .Include(m => m.Games)
        .ThenInclude(g => g.States)
          .ThenInclude(s => s.CardChanges)
      .Include(m => m.Games)
        .ThenInclude(g => g.States)
          .ThenInclude(s => s.PlayerChanges)
      .Include(m => m.Games)
        .ThenInclude(g => g.States)
          .ThenInclude(s => s.Actions)
      .AsSplitQuery()
      .AsNoTracking()
      .FirstOrDefaultAsync(m => m.Id == matchId);

    if (match == null)
    {
      return NotFound($"Match with ID {matchId} not found.");
    }

    // Verify user participated in this match (or it's currently active)
    var playerResult = match.PlayerResults.FirstOrDefault(p => p.Player == currentUser);
    bool isActive = GameAPIService.ActiveMatchIds.Contains(matchId);
    if (playerResult == null && !isActive)
    {
        return Forbid("You do not have access to this match.");
    }

    int wins = 0;
    int losses = 0;
    TimeSpan matchDuration = TimeSpan.Zero;

    var gameDetails = new List<GameDetailsDTO>();

    foreach (var game in match.Games)
    {
      var gameResult = game.GamePlayerResults.FirstOrDefault(p => p.Player == currentUser);
      if (gameResult != null)
      {
        if (gameResult.Result == GameResult.Win) wins++;
        else if (gameResult.Result == GameResult.Loss) losses++;
        matchDuration += gameResult.Clock;
      }

      var gameDTO = new GameDetailsDTO
      {
        Id = game.Id,
        GameNumber = match.Games.IndexOf(game) + 1,
        Result = gameResult?.Result.ToString() ?? "Unknown",
        Duration = gameResult != null ? FormatDuration(gameResult.Clock) : "0m 0s",
        PlayDraw = gameResult?.PlayDraw.ToString() ?? "Unknown",
        Logs = BuildGameLogs(game)
      };
      gameDetails.Add(gameDTO);
    }

    return Ok(new MatchDetailsDTO
    {
      Id = match.Id,
      EventId = match.EventId,
      EventName = match.Event.Description,
      Format = match.Event.Format,
      StartTime = match.Event.StartTime,
      Result = playerResult?.Result.ToString() ?? "In Progress",
      Record = $"{wins}-{losses}",
      Duration = FormatDuration(matchDuration),
      DeckHash = match.Event.Deck?.Hash ?? match.Event.DeckHash,
      DeckName = match.Event.Deck?.Name,
      DeckArchetype = match.Event.Deck?.Archetype,
      DeckColors = GetDeckColors(match.Event.Deck),
      OpponentName = GetOpponentName(match, currentUser),
      IsActive = isActive,
      Games = gameDetails
    });
  }

  private static string FormatDuration(TimeSpan duration) =>
    $"{Math.Floor(duration.TotalMinutes)}m {duration.Seconds}s";

[HttpGet("/api/games/formats")]
  public async Task<ActionResult<List<string>>> GetFormats()
  {
    var formats = await context.Events
      .Select(e => e.Format)
      .Distinct()
      .OrderBy(f => f)
      .AsNoTracking()
      .ToListAsync();

    return Ok(formats);
  }

  [HttpGet("/api/games/dashboard-stats")]
  public async Task<ActionResult<DashboardStatsDTO>> GetDashboardStats(
    [FromQuery] DateTime? minDate,
    [FromQuery] DateTime? maxDate,
    [FromQuery] string? format)
  {
    if (!clientProvider.TryGetCurrentUsername(out var currentUser))
    {
      return BadRequest("Client not ready or user not logged in.");
    }

    // Base query with JSON filtering for current user
    var query = context.Matches
      .FromSqlRaw(@"
          SELECT m.* FROM Matches m
          WHERE EXISTS (
            SELECT 1 FROM json_each(m.PlayerResults)
            WHERE json_extract(value, '$.player') = {0}
          )", currentUser)
      .Include(m => m.Event)
        .ThenInclude(e => e.Deck)
      .Include(m => m.Games)
        .ThenInclude(g => g.Players)
      .AsSplitQuery()
      .AsNoTracking()
      .AsQueryable();

    if (minDate.HasValue)
    {
      query = query.Where(m => m.Event.StartTime >= minDate.Value);
    }
    if (maxDate.HasValue)
    {
      query = query.Where(m => m.Event.StartTime <= maxDate.Value);
    }
    if (!string.IsNullOrEmpty(format))
    {
      query = query.Where(m => m.Event.Format == format);
    }

    var matches = await query.ToListAsync();

    // Calculate stats in memory
    int totalMatches = matches.Count;
    int wins = 0;
    int losses = 0;
    int ties = 0;
    int playMatches = 0;
    int playWins = 0;
    int drawMatches = 0;
    int drawWins = 0;

    var durations = new List<TimeSpan>();
    var twoGameDurations = new List<TimeSpan>();
    var threeGameDurations = new List<TimeSpan>();

    int counter = 0;
    foreach (var match in matches)
    {
      if (++counter % 100 == 0) await Task.Yield();

      var playerResult = match.PlayerResults.FirstOrDefault(p => p.Player == currentUser);
      if (playerResult == null) continue;

      // Match Result
      if (playerResult.Result == MatchResult.Win)
      {
        wins++;
      }
      else if (playerResult.Result == MatchResult.Loss)
      {
        losses++;
      }
      else
      {
        ties++;
      }

      // Play/Draw (Check first game)
      var firstGame = match.Games.OrderBy(g => g.Id).FirstOrDefault();
      if (firstGame != null)
      {
        var gameResult = firstGame.GamePlayerResults.FirstOrDefault(p => p.Player == currentUser);
        if (gameResult != null)
        {
          if (gameResult.PlayDraw == PlayDrawResult.Play)
          {
            playMatches++;
            if (playerResult.Result == MatchResult.Win)
            {
              playWins++;
            }
          }
          else if (gameResult.PlayDraw == PlayDrawResult.Draw)
          {
            drawMatches++;
            if (playerResult.Result == MatchResult.Win)
            {
              drawWins++;
            }
          }
        }
      }

      // Duration
      TimeSpan matchDuration = TimeSpan.Zero;
      foreach (var game in match.Games)
      {
        var gameResult = game.GamePlayerResults.FirstOrDefault(p => p.Player == currentUser);
        if (gameResult != null)
        {
          matchDuration += gameResult.Clock;
        }
      }

      if (matchDuration > TimeSpan.Zero)
      {
        durations.Add(matchDuration);
        if (match.Games.Count == 2)
        {
          twoGameDurations.Add(matchDuration);
        }
        else if (match.Games.Count == 3)
        {
          threeGameDurations.Add(matchDuration);
        }
      }
    }

    return Ok(new DashboardStatsDTO
    {
      OverallWinrate = totalMatches > 0 ? Math.Round((double) wins / totalMatches * 100, 1) : 0,
      TotalMatches = totalMatches,
      Wins = wins,
      Losses = losses,
      Ties = ties,
      PlayWinrate = playMatches > 0 ? Math.Round((double) playWins / playMatches * 100, 1) : 0,
      PlayMatches = playMatches,
      DrawWinrate = drawMatches > 0 ? Math.Round((double) drawWins / drawMatches * 100, 1) : 0,
      DrawMatches = drawMatches,
      AverageDuration = FormatDuration(durations.Count > 0 ? TimeSpan.FromTicks((long) durations.Average(t => t.Ticks)) : TimeSpan.Zero),
      DurationTwoGames = FormatDuration(twoGameDurations.Count > 0 ? TimeSpan.FromTicks((long) twoGameDurations.Average(t => t.Ticks)) : TimeSpan.Zero),
      DurationThreeGames = FormatDuration(threeGameDurations.Count > 0 ? TimeSpan.FromTicks((long) threeGameDurations.Average(t => t.Ticks)) : TimeSpan.Zero)
    });
  }

  [HttpGet("/api/games/performance-trend")]
  public async Task<ActionResult<List<PerformanceTrendDTO>>> GetPerformanceTrend(
    [FromQuery] DateTime? minDate,
    [FromQuery] DateTime? maxDate,
    [FromQuery] string? format)
  {
    if (!clientProvider.TryGetCurrentUsername(out var currentUser))
    {
      return BadRequest("Client not ready or user not logged in.");
    }

    var query = context.Matches
      .FromSqlRaw(@"
          SELECT m.* FROM Matches m
          WHERE EXISTS (
            SELECT 1 FROM json_each(m.PlayerResults)
            WHERE json_extract(value, '$.player') = {0}
          )", currentUser)
      .Include(m => m.Event)
        .ThenInclude(e => e.Deck)
      .AsNoTracking()
      .AsQueryable();

    if (minDate.HasValue)
    {
      query = query.Where(m => m.Event.StartTime >= minDate.Value);
    }
    if (maxDate.HasValue)
    {
      query = query.Where(m => m.Event.StartTime <= maxDate.Value);
    }
    if (!string.IsNullOrEmpty(format))
    {
      query = query.Where(m => m.Event.Format == format);
    }

    var matches = await query.ToListAsync();

    var trendData = new List<PerformanceTrendDTO>();

    if (matches.Count == 0) return Ok(trendData);

    var startDate = matches.Min(m => m.Event.StartTime.Date);
    var endDate = matches.Max(m => m.Event.StartTime.Date);

    // Ensure we cover the requested range if provided
    if (minDate.HasValue && minDate.Value.Date < startDate) startDate = minDate.Value.Date;
    if (maxDate.HasValue && maxDate.Value.Date > endDate) endDate = maxDate.Value.Date;

    var dateCursor = startDate;
    int loopCounter = 0;
    while (dateCursor <= endDate)
    {
      if (++loopCounter % 5 == 0) await Task.Yield();
      var dayMatches = matches.Where(m => m.Event.StartTime.Date == dateCursor).ToList();
      int n = dayMatches.Count;
      int wins = dayMatches.Count(m => m.PlayerResults.FirstOrDefault(p => p.Player == currentUser)?.Result == MatchResult.Win);

      // Use null for days with no data
      double? winrate = null;
      double[]? ci95 = null;
      double[]? ci80 = null;
      double[]? ci50 = null;

      if (n > 0)
      {
        double p = (double) wins / n;
        winrate = Math.Round(p * 100, 1);
        double se = Math.Sqrt((p * (1 - p)) / n) * 100;
        ci95 = [Math.Max(0, winrate.Value - 1.96 * se), Math.Min(100, winrate.Value + 1.96 * se)];
        ci80 = [Math.Max(0, winrate.Value - 1.28 * se), Math.Min(100, winrate.Value + 1.28 * se)];
        ci50 = [Math.Max(0, winrate.Value - 0.674 * se), Math.Min(100, winrate.Value + 0.674 * se)];
      }

      // Rolling Average: only use days with actual data within the 7-day window
      var windowStart = dateCursor.AddDays(-6);
      var windowMatches = matches.Where(m => m.Event.StartTime.Date >= windowStart && m.Event.StartTime.Date <= dateCursor).ToList();
      int windowN = windowMatches.Count;
      int windowWins = windowMatches.Count(m => m.PlayerResults.FirstOrDefault(p => p.Player == currentUser)?.Result == MatchResult.Win);

      // Only calculate rolling average if there's data in the window
      double? rollingAvg = windowN > 0 ? Math.Round((double) windowWins / windowN * 100, 1) : null;

      trendData.Add(new PerformanceTrendDTO
      {
        Date = dateCursor.ToString("MMM dd"),
        RawDate = dateCursor,
        Winrate = winrate,
        Matches = n,
        RollingAvg = rollingAvg,
        Ci95 = ci95,
        Ci80 = ci80,
        Ci50 = ci50
      });

      dateCursor = dateCursor.AddDays(1);
    }

    return Ok(trendData);
  }

}
