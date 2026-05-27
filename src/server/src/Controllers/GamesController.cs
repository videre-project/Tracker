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
using Tracker.Services.MTGO;
using Tracker.Services.MTGO.Events;


namespace Tracker.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController(EventContext context, IClientAPIProvider clientProvider, ClientStateMonitor clientMonitor) : APIController
{
  [HttpGet("formats")]
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

  [HttpGet("dashboard-stats")]
  public async Task<ActionResult<DashboardStatsDTO>> GetDashboardStats(
    [FromQuery] DateTime? minDate,
    [FromQuery] DateTime? maxDate,
    [FromQuery] string? format)
  {
    var currentUser = clientProvider.Client?.CurrentUser?.Name;
    if (string.IsNullOrEmpty(currentUser))
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

  [HttpGet("performance-trend")]
  public async Task<ActionResult<List<PerformanceTrendDTO>>> GetPerformanceTrend(
    [FromQuery] DateTime? minDate,
    [FromQuery] DateTime? maxDate,
    [FromQuery] string? format)
  {
    var currentUser = clientProvider.Client?.CurrentUser?.Name;
    if (string.IsNullOrEmpty(currentUser))
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

  [HttpGet("history")]
  public async Task<ActionResult<PaginatedMatchesDTO>> GetMatchHistory(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 50,
    [FromQuery] DateTime? minDate = null,
    [FromQuery] DateTime? maxDate = null,
    [FromQuery] string? format = null)
  {
    var currentUser = clientProvider.Client?.CurrentUser?.Name;
    if (string.IsNullOrEmpty(currentUser))
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
        DeckName = match.Event.Deck?.Name
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
          .Include(m => m.Event).ThenInclude(e => e.Deck)
          .Include(m => m.Games)
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
            DeckName = match.Event?.Deck?.Name,
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
            DeckName = evt.Deck?.Name,
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
          DeckName = first.DeckName,
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
    var currentUser = clientProvider.Client?.CurrentUser?.Name;
    if (string.IsNullOrEmpty(currentUser))
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
      DeckName = match.Event.Deck?.Name,
      IsActive = isActive,
      Games = gameDetails
    });
  }

  [HttpGet("match/{matchId}/watch")]
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

  [HttpGet("history/watch")]
  [ProducesResponseType(typeof(IEnumerable<MatchHistoryDTO>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  [Produces("application/x-ndjson")]
  public async Task<IActionResult> WatchMatchHistory()
  {
    if (!clientMonitor.IsClientReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "MTGO client is not ready" });
    }

    var currentUser = clientProvider.Client?.CurrentUser?.Name;
    if (string.IsNullOrEmpty(currentUser))
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
          Duration = FormatDuration(matchDuration),
          DeckName = match.Event?.Deck?.Name,
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
          DeckName = evt.Deck?.Name,
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

  private static readonly JsonSerializerOptions s_jsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false,
  };

  private static List<GameLogDTO> BuildGameLogs(GameModel game)
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
          }, s_jsonOptions)
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
            }), s_jsonOptions)
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
            }), s_jsonOptions)
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
            }), s_jsonOptions)
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
            }), s_jsonOptions)
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
          Data = log.Data
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

  [HttpGet("game/{gameId}/replay")]
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

  private string FormatDuration(TimeSpan duration)
  {
    return $"{Math.Floor(duration.TotalMinutes)}m {duration.Seconds}s";
  }
}

public class GameLogDTO
{
  public int Id { get; set; }
  public int GameId { get; set; }
  public DateTime Timestamp { get; set; }
  public required GameLogType GameLogType { get; set; }
  public required string Data { get; set; }
  public int Nonce { get; set; }
}

public class GameDetailsDTO
{
  public int Id { get; set; }
  public int GameNumber { get; set; }
  public required string Result { get; set; }
  public required string Duration { get; set; }
  public required string PlayDraw { get; set; }
  public List<GameLogDTO> Logs { get; set; } = new();
}

public class MatchDetailsDTO
{
  public int Id { get; set; }
  public int EventId { get; set; }
  public required string EventName { get; set; }
  public required string Format { get; set; }
  public DateTime StartTime { get; set; }
  public required string Result { get; set; }
  public required string Record { get; set; }
  public required string Duration { get; set; }
  public string? DeckName { get; set; }
  public bool IsActive { get; set; }

  public List<GameDetailsDTO> Games { get; set; } = new();
}

public class PaginatedMatchesDTO
{
  public List<MatchHistoryDTO> Items { get; set; } = new();
  public int TotalCount { get; set; }
  public int Page { get; set; }
  public int PageSize { get; set; }
  public int TotalPages { get; set; }
}

public class MatchHistoryDTO
{
  public int Id { get; set; }
  public int EventId { get; set; }
  public required string EventName { get; set; }
  public required string Format { get; set; }
  public DateTime StartTime { get; set; }
  public required string Result { get; set; }
  public required string Record { get; set; }
  public required string Duration { get; set; }
  public string? DeckName { get; set; }
  public bool IsActive { get; set; }
  public bool IsEvent { get; set; }
  public List<MatchHistoryDTO>? Matches { get; set; }
}

public class DashboardStatsDTO
{
  public double OverallWinrate { get; set; }
  public int TotalMatches { get; set; }
  public int Wins { get; set; }
  public int Losses { get; set; }
  public int Ties { get; set; }
  public double PlayWinrate { get; set; }
  public int PlayMatches { get; set; }
  public double DrawWinrate { get; set; }
  public int DrawMatches { get; set; }
  public string AverageDuration { get; set; } = "0m 0s";
  public string DurationTwoGames { get; set; } = "0m 0s";
  public string DurationThreeGames { get; set; } = "0m 0s";
}

public class PerformanceTrendDTO
{
  public required string Date { get; set; }
  public required DateTime RawDate { get; set; }
  public double? Winrate { get; set; }
  public int Matches { get; set; }
  public double? RollingAvg { get; set; }
  public double[]? Ci95 { get; set; }
  public double[]? Ci80 { get; set; }
  public double[]? Ci50 { get; set; }
}

//
// Replay DTOs
//

public class ReplayDataDTO
{
  public int GameId { get; set; }
  public List<ReplayPlayerDTO> Players { get; set; } = new();
  public List<ReplayCardDTO> Cards { get; set; } = new();
  public List<ReplaySnapshotDTO> Snapshots { get; set; } = new();
}

public class ReplayPlayerDTO
{
  public int PlayerIndex { get; set; }
  public required string Name { get; set; }
  public string? PlayDraw { get; set; }
  public int InitialLife { get; set; }
  public int InitialHandCount { get; set; }
  public int InitialLibraryCount { get; set; }
  public int InitialGraveyardCount { get; set; }
  public string? InitialManaPool { get; set; }
  public bool IsActivePlayer { get; set; }
  public double ClockRemaining { get; set; }
  public int UserId { get; set; }
  public int AvatarId { get; set; }
}

public class ReplayCardDTO
{
  public int CardId { get; set; }
  public required string Name { get; set; }
  public string? RulesText { get; set; }
  public string? ManaCost { get; set; }
  public int? CatalogId { get; set; }
  public required string InitialZone { get; set; }
  public string? InitialPower { get; set; }
  public string? InitialToughness { get; set; }
  public int OwnerId { get; set; }
  public int? SourceId { get; set; }
  public bool IsTapped { get; set; }
  public bool IsToken { get; set; }
  public bool IsLand { get; set; }
  public bool IsActivatedAbility { get; set; }
  public bool IsTriggeredAbility { get; set; }
  public int FirstSeenSnapshotIndex { get; set; }
}

public class ReplaySnapshotDTO
{
  public int Index { get; set; }
  public int Nonce { get; set; }
  public DateTime Timestamp { get; set; }
  public int TurnNumber { get; set; }
  public required string CurrentPhase { get; set; }
  public int PromptedPlayer { get; set; }
  public required string PromptText { get; set; }
  /// <summary>
  /// JSON array of available prompt actions, e.g. [{"type":"ChooseOption","name":"OK"}].
  /// Null for snapshots captured before this feature was added.
  /// </summary>
  public string? PromptOptions { get; set; }
  public List<ZoneTransferData> ZoneTransfers { get; set; } = new();
  public List<CardChangeData> CardChanges { get; set; } = new();
  public List<PlayerChangeData> PlayerChanges { get; set; } = new();
  public List<ReplayActionDTO> Actions { get; set; } = new();
  public List<ReplayLogDTO> Logs { get; set; } = new();
}

public class ReplayActionDTO
{
  public required string ActionType { get; set; }
  public string? ActionName { get; set; }
  public int? CardId { get; set; }
  public string? CardName { get; set; }
  public string? Targets { get; set; }
  public required string Data { get; set; }
  public DateTime ClientTimestamp { get; set; }
  public int Nonce { get; set; }
}

public class ReplayLogDTO
{
  public DateTime Timestamp { get; set; }
  public required string Data { get; set; }
}
