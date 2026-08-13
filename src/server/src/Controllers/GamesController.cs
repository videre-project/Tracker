/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using MTGOSDK.API.Collection;
using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;
using MTGOSDK.Core.Logging;

using Tracker.Controllers.Base;
using Tracker.Controllers.Models.Decks;
using Tracker.Database;
using Tracker.Database.Models.Events;
using Tracker.Controllers.Models.Games;
using Tracker.Services.MTGO;
using Tracker.Services.MTGO.Collection;
using Tracker.Services.Videre;
using static Tracker.Services.MTGO.Events.MatchHistorySerialization;


namespace Tracker.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController : APIController
{
  private readonly EventContext context;
  private readonly IClientAPIProvider clientProvider;
  private readonly CollectionDeckService deckService;
  private readonly IManafoldArchetypeClient manafoldArchetypeClient;
  private static readonly ConcurrentDictionary<string, string> s_cardNameColorCache = new(StringComparer.OrdinalIgnoreCase);

  public GamesController(
    EventContext context,
    IClientAPIProvider clientProvider,
    CollectionDeckService deckService,
    IManafoldArchetypeClient manafoldArchetypeClient)
  {
    this.context = context;
    this.clientProvider = clientProvider;
    this.deckService = deckService;
    this.manafoldArchetypeClient = manafoldArchetypeClient;
  }

  [HttpGet("history")]
  public async Task<ActionResult<PaginatedMatchesDTO>> GetMatchHistory(
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 50,
    [FromQuery] DateTime? minDate = null,
    [FromQuery] DateTime? maxDate = null,
    [FromQuery] string? format = null,
    [FromQuery] long? deckRevisionId = null)
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
    if (deckRevisionId.HasValue)
    {
      query = query.Where(
        m => m.Event.DeckRevisionId == deckRevisionId.Value);
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
    var deckRevisions = new Dictionary<long, DeckRevisionView>();
    await LoadDeckRevisionsAsync(matches.Select(match => match.Event));

    // Map to DTOs
    var matchDTOs = new List<MatchHistoryDTO>();
    foreach (var match in matches)
    {
      DeckRevisionView? deck = GetDeck(match.Event);
      var playerResult = match.PlayerResults.FirstOrDefault(p => p.Player == currentUser);
      
      TimeSpan matchDuration = TimeSpan.Zero;
      foreach (var game in match.Games)
      {
         var gameResult = game.GamePlayerResults.FirstOrDefault(p => p.Player == currentUser);
         matchDuration += GetGameDuration(game, gameResult);
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
        DeckRevisionId = match.Event.DeckRevisionId,
        DeckName = deck?.Name,
        DeckColors = deck?.Colors,
        OpponentName = GetOpponentName(match, currentUser),
        OpponentDeckArchetype = match.OpponentDeckArchetype,
        OpponentDeckColors = match.OpponentDeckColors
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
          .Where(m => !deckRevisionId.HasValue ||
            m.Event.DeckRevisionId == deckRevisionId.Value)
          .Include(m => m.Event)
          .Include(m => m.Games)
            .ThenInclude(g => g.Players)
          .AsSplitQuery().AsNoTracking()
          .ToListAsync();
        await LoadDeckRevisionsAsync(
          activeMatches.Select(match => match.Event));

        foreach (var match in activeMatches)
        {
          DeckRevisionView? deck = GetDeck(match.Event);
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
            DeckRevisionId = match.Event?.DeckRevisionId,
            DeckName = deck?.Name,
            DeckColors = deck?.Colors,
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
          .Where(e => !deckRevisionId.HasValue ||
            e.DeckRevisionId == deckRevisionId.Value)
          .AsNoTracking()
          .ToListAsync();
        await LoadDeckRevisionsAsync(activeEvents);

        foreach (var evt in activeEvents)
        {
          DeckRevisionView? deck = GetDeck(evt);
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
            DeckRevisionId = evt.DeckRevisionId,
            DeckName = deck?.Name,
            DeckColors = deck?.Colors,
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
          DeckRevisionId = first.DeckRevisionId,
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

    async Task LoadDeckRevisionsAsync(
      IEnumerable<Database.Models.Events.EventModel> events)
    {
      var loaded = await deckService.GetRevisionsAsync(
        events
          .Where(eventModel => eventModel.DeckRevisionId != null)
          .Select(eventModel => eventModel.DeckRevisionId!.Value));
      foreach (var pair in loaded)
        deckRevisions[pair.Key] = pair.Value;
    }

    DeckRevisionView? GetDeck(Database.Models.Events.EventModel? eventModel) =>
      eventModel?.DeckRevisionId is long id &&
      deckRevisions.TryGetValue(id, out var deck)
        ? deck
        : null;
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
    DeckRevisionView? deck = match.Event.DeckRevisionId is long revisionId
      ? await deckService.GetRevisionAsync(revisionId)
      : null;

    // Verify user participated in this match (or it's currently active)
    var playerResult = match.PlayerResults.FirstOrDefault(p => p.Player == currentUser);
    bool isActive = GameAPIService.ActiveMatchIds.Contains(matchId);
    if (playerResult == null && !isActive)
    {
        return StatusCode(StatusCodes.Status403Forbidden,
          "You do not have access to this match.");
    }

    int wins = 0;
    int losses = 0;
    TimeSpan matchDuration = TimeSpan.Zero;

    var gameDetails = new List<GameDetailsDTO>();

    foreach (var game in match.Games)
    {
      var gameResult = game.GamePlayerResults.FirstOrDefault(p => p.Player == currentUser);
      TimeSpan dur = GetGameDuration(game, gameResult);
      if (gameResult != null)
      {
        if (gameResult.Result == GameResult.Win) wins++;
        else if (gameResult.Result == GameResult.Loss) losses++;
      }
      matchDuration += dur;

      var gameDTO = new GameDetailsDTO
      {
        Id = game.Id,
        GameNumber = match.Games.IndexOf(game) + 1,
        Result = gameResult?.Result.ToString() ?? "Unknown",
        Duration = FormatDuration(dur),
        PlayDraw = ResolvePlayDraw(game, currentUser, gameResult?.PlayDraw),
        SideboardChanges = match.SideboardChanges
          .GetValueOrDefault(game.Id, [])
          .Select(change => new SideboardChangeDTO
          {
            CatalogId = change.catalogId,
            Name = change.name,
            Quantity = change.quantity
          })
          .ToList(),
        Logs = BuildGameLogs(game)
      };
      gameDetails.Add(gameDTO);
    }

    if (string.IsNullOrEmpty(match.OpponentDeckArchetype))
    {
      await DetectAndPersistOpponentArchetypeAsync(match, currentUser);
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
      DeckRevisionId = match.Event.DeckRevisionId,
      DeckName = deck?.Name,
      DeckArchetype = deck?.Archetype,
      DeckColors = deck?.Colors,
      OpponentName = GetOpponentName(match, currentUser),
      OpponentDeckArchetype = match.OpponentDeckArchetype,
      OpponentDeckColors = match.OpponentDeckColors,
      IsActive = isActive,
      Games = gameDetails
    });
  }

  /// <summary>
  /// Manually update opponent archetype for a match
  /// </summary>
  [HttpPut("match/{matchId:int}/opponent-archetype")]
  [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<object>> UpdateOpponentArchetype(
    int matchId,
    [FromBody] DecksController.UpdateArchetypeRequest request)
  {
    if (!clientProvider.TryGetCurrentUsername(out var currentUser))
    {
      return BadRequest("Client not ready or user not logged in.");
    }

    var match = await context.Matches.FirstOrDefaultAsync(m => m.Id == matchId);
    if (match == null) return NotFound($"Match {matchId} not found.");

    string? newArchetype = string.IsNullOrWhiteSpace(request.Archetype) ? null : request.Archetype.Trim();
    match.OpponentDeckArchetype = newArchetype;
    await context.SaveChangesAsync();

    return Ok(new { matchId, opponentDeckArchetype = newArchetype, opponentDeckColors = match.OpponentDeckColors });
  }

  private async Task<(string? Archetype, List<string>? Colors)> DetectAndPersistOpponentArchetypeAsync(
    MatchModel match,
    string currentUser,
    CancellationToken cancellationToken = default)
  {
    bool hasArchetype = !string.IsNullOrEmpty(match.OpponentDeckArchetype);
    bool hasColors = match.OpponentDeckColors != null && match.OpponentDeckColors.Count > 0;

    if (hasArchetype && hasColors)
    {
      return (match.OpponentDeckArchetype, match.OpponentDeckColors);
    }

    try
    {
      var games = match.Games;
      if (games == null || games.Count == 0 || games.Any(g => g.Cards == null || g.Players == null))
      {
        games = await context.Games
          .Include(g => g.Cards)
          .Include(g => g.Players)
          .Where(g => g.MatchId == match.Id)
          .AsNoTracking()
          .ToListAsync(cancellationToken);
      }

      if (games.Count == 0) return (match.OpponentDeckArchetype, match.OpponentDeckColors);

      var cardCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
      var catalogIds = new HashSet<int>();

      foreach (var game in games)
      {
        var perspectivePlayer = game.Players.FirstOrDefault(p =>
          string.Equals(p.Name, currentUser, StringComparison.OrdinalIgnoreCase));
        int? perspectiveIdx = perspectivePlayer?.PlayerIndex;

        foreach (var card in game.Cards)
        {
          if (perspectiveIdx.HasValue && card.OwnerId == perspectiveIdx.Value) continue;
          if (card.IsToken || card.IsActivatedAbility || card.IsTriggeredAbility) continue;
          if (string.IsNullOrWhiteSpace(card.Name)) continue;

          cardCounts[card.Name] = cardCounts.GetValueOrDefault(card.Name, 0) + 1;
          if (card.CatalogId.HasValue && card.CatalogId.Value > 0)
          {
            catalogIds.Add(card.CatalogId.Value);
          }
        }
      }

      if (cardCounts.Count == 0) return (match.OpponentDeckArchetype, match.OpponentDeckColors);

      var detectedColorChars = new HashSet<char>();

      // Look up card definitions by name in local MTGO catalog (using static cache)
      foreach (var cardName in cardCounts.Keys)
      {
        if (!s_cardNameColorCache.TryGetValue(cardName, out var colorsStr))
        {
          try
          {
            var cardDef = CollectionManager.GetCard(cardName);
            if (cardDef != null)
            {
              dynamic dynCard = cardDef;
              colorsStr = dynCard?.Colors?.ToString() ?? "";
              s_cardNameColorCache[cardName] = colorsStr;
            }
          }
          catch
          {
            // Ignore
          }
        }

        if (!string.IsNullOrEmpty(colorsStr))
        {
          foreach (char c in colorsStr)
          {
            if (VidereCardColors.IsCanonical(c))
            {
              detectedColorChars.Add(c);
            }
          }
        }
      }

      // Fallback: try catalog IDs if present
      foreach (var cid in catalogIds)
      {
        try
        {
          var cardDef = CollectionManager.GetCard(cid);
          if (cardDef != null)
          {
            dynamic dynCard = cardDef;
            string? colorsStr = dynCard?.Colors?.ToString();
            if (!string.IsNullOrEmpty(colorsStr))
            {
              foreach (char c in colorsStr)
              {
                if (VidereCardColors.IsCanonical(c))
                {
                  detectedColorChars.Add(c);
                }
              }
            }
          }
        }
        catch
        {
          // Ignore
        }
      }

      List<string> opponentColors = VidereCardColors.Normalize(detectedColorChars).ToList();

      string? detectedArchetype = null;
      if (!hasArchetype && manafoldArchetypeClient != null && cardCounts.Count >= 1)
      {
        var manafoldCards = cardCounts.Select(kvp => new ManafoldDeckCard(kvp.Key, kvp.Value)).ToList();
        string format = match.Event?.Format ?? "Modern";
        var response = await manafoldArchetypeClient.DetectArchetypeAsync(manafoldCards, format, cancellationToken);
        (detectedArchetype, _) = DecksController.ParseArchetype(response);
      }

      var finalArchetype = !string.IsNullOrEmpty(match.OpponentDeckArchetype)
        ? match.OpponentDeckArchetype
        : detectedArchetype;

      var finalColors = (match.OpponentDeckColors != null && match.OpponentDeckColors.Count > 0)
        ? match.OpponentDeckColors
        : opponentColors;

      if (finalArchetype != match.OpponentDeckArchetype || (finalColors.Count > 0 && (match.OpponentDeckColors == null || match.OpponentDeckColors.Count == 0)))
      {
        var dbMatch = await context.Matches.FirstOrDefaultAsync(m => m.Id == match.Id, cancellationToken);
        if (dbMatch != null)
        {
          dbMatch.OpponentDeckArchetype = finalArchetype;
          dbMatch.OpponentDeckColors = finalColors;
          await context.SaveChangesAsync(cancellationToken);
        }
        match.OpponentDeckArchetype = finalArchetype;
        match.OpponentDeckColors = finalColors;
      }

      return (match.OpponentDeckArchetype, match.OpponentDeckColors);
    }
    catch (Exception ex)
    {
      Log.Trace($"Failed to detect opponent archetype for match {match.Id}: {ex.Message}");
      return (match.OpponentDeckArchetype, match.OpponentDeckColors);
    }
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
      .Include(m => m.Games)
        .ThenInclude(g => g.Players)
      .Include(m => m.Games)
        .ThenInclude(g => g.States)
          .ThenInclude(s => s.Logs)
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
        var playDraw = ResolvePlayDraw(firstGame, currentUser, gameResult?.PlayDraw);
        if (playDraw == "Play")
        {
          playMatches++;
          if (playerResult.Result == MatchResult.Win)
          {
            playWins++;
          }
        }
        else if (playDraw == "Draw")
        {
          drawMatches++;
          if (playerResult.Result == MatchResult.Win)
          {
            drawWins++;
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

  private static TimeSpan GetGameDuration(GameModel game, GamePlayerResult? gameResult)
  {
    if (game.States != null && game.States.Count >= 2)
    {
      var minTime = game.States.Min(s => s.ClientTimestamp);
      var maxTime = game.States.Max(s => s.ClientTimestamp);
      var diff = maxTime - minTime;
      if (diff > TimeSpan.Zero && diff < TimeSpan.FromHours(12))
      {
        return diff;
      }
    }

    if (gameResult != null && gameResult.Clock > TimeSpan.Zero)
    {
      return gameResult.Clock;
    }

    return TimeSpan.Zero;
  }

  private static string ResolvePlayDraw(
    GameModel game,
    string? playerUsername,
    PlayDrawResult? existingResult)
  {
    if (string.IsNullOrWhiteSpace(playerUsername))
    {
      playerUsername = game.GamePlayerResults.FirstOrDefault()?.Player
        ?? game.Players.FirstOrDefault()?.Name;
    }

    if (game.States != null && !string.IsNullOrWhiteSpace(playerUsername))
    {
      // Scan states in reverse (latest first) so the last occurrence of
      // "skips their draw step" / "chooses to play first" is found first.
      // MTGO replays these log lines during each mulligan loop, so earlier
      // occurrences can be pre-game echoes, not the real Turn 1 decision.
      var statesReversed = game.States.OrderByDescending(s => s.Id).ToList();

      foreach (var state in statesReversed)
      {
        if (state.Logs == null) continue;
        foreach (var log in state.Logs.OrderByDescending(l => l.Id))
        {
          if (string.IsNullOrEmpty(log.Data)) continue;

          // "<Player> skips their draw step." -> <Player> is on the Play
          if (log.Data.Contains("skips their draw step", StringComparison.OrdinalIgnoreCase))
          {
            return log.Data.Contains(playerUsername, StringComparison.OrdinalIgnoreCase)
              ? "Play"
              : "Draw";
          }

          // "<Player> chooses to play first." -> <Player> is on the Play
          if (log.Data.Contains("chooses to play first", StringComparison.OrdinalIgnoreCase))
          {
            return log.Data.Contains(playerUsername, StringComparison.OrdinalIgnoreCase)
              ? "Play"
              : "Draw";
          }

          // "<Player> chooses to draw first." or "<Player> chooses to play second." -> <Player> is on the Draw
          if (log.Data.Contains("chooses to draw first", StringComparison.OrdinalIgnoreCase) ||
              log.Data.Contains("chooses to play second", StringComparison.OrdinalIgnoreCase))
          {
            return log.Data.Contains(playerUsername, StringComparison.OrdinalIgnoreCase)
              ? "Draw"
              : "Play";
          }
        }
      }

      foreach (var state in statesReversed)
      {
        if (!string.IsNullOrEmpty(state.PromptText))
        {
          if (state.PromptText.Contains("You lost the die roll", StringComparison.OrdinalIgnoreCase) &&
              state.PromptText.Contains("decide whether or not to play first", StringComparison.OrdinalIgnoreCase))
          {
            return "Draw";
          }
          if (state.PromptText.Contains("You won the die roll", StringComparison.OrdinalIgnoreCase) ||
              state.PromptText.Contains("decide whether or not to play first", StringComparison.OrdinalIgnoreCase))
          {
            return "Play";
          }
        }
      }
    }

    if (existingResult.HasValue && existingResult.Value != PlayDrawResult.Unknown)
    {
      return existingResult.Value.ToString();
    }

    return "Unknown";
  }
}
