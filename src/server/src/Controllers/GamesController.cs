/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;

using Tracker.Database;
using Tracker.Database.Models;
using Tracker.Services.MTGO;


namespace Tracker.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController(EventContext context, IClientAPIProvider clientProvider) : ControllerBase
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

  private string FormatDuration(TimeSpan duration)
  {
    return $"{Math.Floor(duration.TotalMinutes)}m {duration.Seconds}s";
  }
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
