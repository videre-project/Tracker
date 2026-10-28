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

using MTGOTracker.Controllers.Base;
using MTGOTracker.Controllers.Models.Decks;
using MTGOTracker.Database;
using MTGOTracker.Database.Models;
using MTGOTracker.Database.Models.Events;
using MTGOTracker.Controllers.Models.Games;
using MTGOTracker.Services.MTGO;
using MTGOTracker.Services.MTGO.Collection;
using MTGOTracker.Services.Videre;
using static MTGOTracker.Services.MTGO.Events.MatchHistorySerialization;


namespace MTGOTracker.Controllers;

[ApiController]
[Route("api/[controller]")]
public partial class GamesController : APIController
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
    [FromQuery] long? deckRevisionId = null,
    [FromQuery] long? deckId = null,
    [FromQuery] string? opponent = null,
    [FromQuery] bool completedOnly = false,
    [FromQuery] bool groupEvents = true,
    [FromQuery] string? archetype = null,
    [FromQuery] bool includeOpponentCards = false,
    [FromQuery] string? gameType = null,
    CancellationToken cancellationToken = default)
  {
    if (!clientProvider.TryGetCurrentUser(out var account) || account == null)
    {
      return BadRequest("Client not ready or user not logged in.");
    }
    string currentUser = account.Username;
    if (!DeckFormatResolver.TryGetGameType(gameType, clientProvider.Pid, out var formatType, out var legacyFormats))
      return BadRequest("Choose all, constructed, or limited games.");

    // Validate and clamp pagination
    page = Math.Max(1, page);
    pageSize = Math.Clamp(pageSize, 1, 200);

    var query = context.MatchesForPlayer(currentUser, minDate, maxDate, format, completedOnly, formatType, legacyFormats)
      .Include(match => match.Games).ThenInclude(game => game.Players).AsSplitQuery();
    if (deckRevisionId.HasValue)
    {
      query = query.Where(
        m => m.Event.DeckRevisionId == deckRevisionId.Value);
    }

    if (deckId.HasValue)
    {
      var ids = (await deckService.GetRevisionGroupingIdsAsync(new[] { deckId.Value })).Keys.ToArray();
      query = query.Where(match => match.Event.DeckRevisionId.HasValue && ids.Contains(match.Event.DeckRevisionId.Value));
    }
    if (archetype != null)
    {
      if (string.IsNullOrWhiteSpace(archetype) || string.IsNullOrWhiteSpace(format))
        return BadRequest("Choose an archetype and format.");
      var scope = await ReadArchetypeScopeAsync(query, archetype, format, account.Id, cancellationToken);
      query = query.Where(match => match.Event.DeckRevisionId.HasValue && scope.Revisions.Contains(match.Event.DeckRevisionId.Value));
    }
    // Model binding turns an explicit empty opponent (unclassified) into null.
    bool filterOpponent = opponent != null || HttpContext?.Request.Query.ContainsKey(nameof(opponent)) == true;
    if (filterOpponent)
    {
      opponent = opponent?.Trim() ?? string.Empty;
      query = query.Where(match =>
        EF.Functions.Collate((match.OpponentDeckArchetype ?? "").Trim(), "NOCASE") == opponent);
    }

    var activeEventIds = GameAPIService.ActiveEventIds.ToArray();
    var activeMatchIds = GameAPIService.ActiveMatchIds.ToArray();
    bool includeActive = !completedOnly && deckId == null && !filterOpponent && archetype == null;
    var activeMatchesQuery = context.Matches.Where(match => activeMatchIds.Contains(match.Id))
      .Where(match => !deckRevisionId.HasValue || match.Event.DeckRevisionId == deckRevisionId.Value);
    var activeEventsQuery = context.Events.Where(evt => activeEventIds.Contains(evt.Id))
      .Where(evt => !deckRevisionId.HasValue || evt.DeckRevisionId == deckRevisionId.Value);
    if (minDate.HasValue)
    {
      activeMatchesQuery = activeMatchesQuery.Where(match => match.Event.StartTime >= minDate.Value);
      activeEventsQuery = activeEventsQuery.Where(evt => evt.StartTime >= minDate.Value);
    }
    if (maxDate.HasValue)
    {
      activeMatchesQuery = activeMatchesQuery.Where(match => match.Event.StartTime <= maxDate.Value);
      activeEventsQuery = activeEventsQuery.Where(evt => evt.StartTime <= maxDate.Value);
    }
    if (!string.IsNullOrWhiteSpace(format))
    {
      activeMatchesQuery = activeMatchesQuery.Where(match => match.Event.Format == format);
      activeEventsQuery = activeEventsQuery.Where(evt => evt.Format == format);
    }
    if (formatType is { } category)
    {
      activeMatchesQuery = activeMatchesQuery.Where(match => match.Event.FormatType.HasValue
        ? (match.Event.FormatType.Value & category) != 0
        : legacyFormats.Contains(EF.Functions.Collate(match.Event.Format, "NOCASE")));
      activeEventsQuery = activeEventsQuery.Where(evt => evt.FormatType.HasValue
        ? (evt.FormatType.Value & category) != 0
        : legacyFormats.Contains(EF.Functions.Collate(evt.Format, "NOCASE")));
    }
    // A grouped history page contains complete events, not fragments of the
    // matches that happened to fall within a page of individual matches.
    var eventIds = query.Select(match => match.EventId);
    if (groupEvents && includeActive)
      eventIds = eventIds.Union(activeMatchesQuery.Select(match => match.EventId))
        .Union(activeEventsQuery.Select(evt => evt.Id));
    var eventQuery = context.Events.Where(evt => eventIds.Contains(evt.Id));
    int totalCount = groupEvents ? await eventQuery.CountAsync(cancellationToken)
      : await query.CountAsync(cancellationToken);
    int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

    List<MatchModel> matches;
    List<int> pageEvents = [];
    if (groupEvents)
    {
      pageEvents = await eventQuery
        .OrderByDescending(evt => activeEventIds.Contains(evt.Id) || evt.Matches.Any(match => activeMatchIds.Contains(match.Id)))
        .ThenByDescending(evt => evt.StartTime).ThenByDescending(evt => evt.Id)
        .Skip((page - 1) * pageSize).Take(pageSize)
        .Select(group => group.Id).ToListAsync(cancellationToken);
      matches = await query.Where(match => pageEvents.Contains(match.EventId))
        .OrderByDescending(match => match.Event.StartTime).ThenByDescending(match => match.Id)
        .ToListAsync(cancellationToken);
    }
    else
    {
      matches = await query.OrderByDescending(match => match.Event.StartTime).ThenByDescending(match => match.Id)
        .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
    }
    var deckRevisions = new Dictionary<long, DeckRevisionView>();
    await LoadDeckRevisionsAsync(matches.Select(match => match.Event));

    var stateSpans = await ReadGameStateSpansAsync(
      context, matches.SelectMany(match => match.Games).Select(game => game.Id));

    var matchDTOs = matches.Select(match => ToMatchHistory(match, currentUser, GetDeck(match.Event),
      GameAPIService.ActiveMatchIds.Contains(match.Id), stateSpans)).ToList();
    if (includeOpponentCards)
    {
      var cards = await ReadOpponentCardsAsync(matches.Select(match => match.Id), currentUser, cancellationToken);
      foreach (var row in matchDTOs) row.OpponentCards = cards.GetValueOrDefault(row.Id, []);
    }

    // Include active matches and events in the selected event page.
    if (includeActive && (groupEvents || page == 1))
    {
      var existingMatchIds = new HashSet<int>(matchDTOs.Select(m => m.Id));
      var existingEventIds = new HashSet<int>(matchDTOs.Select(m => m.EventId));

      // Active matches not yet in the saved history
      var missingMatchIds = activeMatchIds.Where(id => !existingMatchIds.Contains(id)).ToList();

      if (missingMatchIds.Count > 0)
      {
        var activeQuery = activeMatchesQuery
          .Where(match => missingMatchIds.Contains(match.Id))
          .Include(m => m.Event)
          .Include(m => m.Games)
            .ThenInclude(g => g.Players)
          .AsSplitQuery().AsNoTracking();
        if (groupEvents) activeQuery = activeQuery.Where(match => pageEvents.Contains(match.EventId));
        var activeMatches = await activeQuery.ToListAsync(cancellationToken);
        await LoadDeckRevisionsAsync(
          activeMatches.Select(match => match.Event));

        var activeStateSpans = await ReadGameStateSpansAsync(
          context, activeMatches.SelectMany(match => match.Games).Select(game => game.Id));
        foreach (var (id, span) in activeStateSpans) stateSpans[id] = span;
        matches.AddRange(activeMatches);

        foreach (var match in activeMatches)
        {
          matchDTOs.Add(ToMatchHistory(match, currentUser, GetDeck(match.Event), true, activeStateSpans));
          existingEventIds.Add(match.EventId);
        }
      }

      // Active events (tournaments) not yet represented
      var missingEventIds = activeEventIds
        .Where(id => !existingEventIds.Contains(id))
        .ToList();

      if (missingEventIds.Count > 0)
      {
        var activeQuery = activeEventsQuery.Where(evt => missingEventIds.Contains(evt.Id)).AsNoTracking();
        if (groupEvents) activeQuery = activeQuery.Where(evt => pageEvents.Contains(evt.Id));
        var activeEvents = await activeQuery.ToListAsync(cancellationToken);
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
            DeckArchetype = deck?.Archetype,
            DeckColors = deck?.Colors,
            IsActive = true,
            IsEvent = true
          });
        }
      }
    }

    if (!groupEvents) return Ok(new PaginatedMatchesDTO
    {
      Items = matchDTOs, TotalCount = totalCount, Page = page, PageSize = pageSize, TotalPages = totalPages
    });

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
        int wins = childMatches.Count(child => child.Result == "Win");
        int losses = childMatches.Count(child => child.Result == "Loss");
        int draws = childMatches.Count(child => child.Result == "Draw");
        string record = $"{wins}-{losses}" + (draws > 0 ? $"-{draws}" : "");
        bool anyActive = childMatches.Any(child => child.IsActive) || activeEventIds.Contains(first.EventId);
        var totalDuration = matches.Where(match => match.EventId == group.Key)
          .SelectMany(match => match.Games)
          .Aggregate(TimeSpan.Zero, (total, game) => total + GetGameDuration(game,
            game.GamePlayerResults.FirstOrDefault(result => string.Equals(result.Player, currentUser, StringComparison.OrdinalIgnoreCase)), stateSpans));

        parentRow = new MatchHistoryDTO
        {
          Id = 0,
          EventId = first.EventId,
          EventName = first.EventName,
          Format = first.Format,
          StartTime = first.StartTime,
          Result = anyActive ? "In Progress" : record,
          Record = record,
          Duration = FormatDuration(totalDuration),
          DeckRevisionId = first.DeckRevisionId,
          DeckName = first.DeckName,
          DeckArchetype = first.DeckArchetype,
          DeckColors = first.DeckColors,
          IsActive = anyActive,
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
    var playerResult = match.PlayerResults.FirstOrDefault(p => string.Equals(p.Player, currentUser, StringComparison.OrdinalIgnoreCase));
    bool isActive = GameAPIService.ActiveMatchIds.Contains(matchId);
    if (playerResult == null && !isActive &&
        !match.Games.Any(game => game.Players.Any(player => string.Equals(player.Name, currentUser, StringComparison.OrdinalIgnoreCase))))
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
      var gameResult = game.GamePlayerResults.FirstOrDefault(p => string.Equals(p.Player, currentUser, StringComparison.OrdinalIgnoreCase));
      TimeSpan dur = GetGameDuration(game, gameResult);
      if (gameResult != null)
      {
        if (gameResult.Result == GameResult.Win) wins++;
        else if (gameResult.Result == GameResult.Loss) losses++;
      }
      matchDuration += dur;

      gameDetails.Add(ToGameDetails(game, match, currentUser,
        match.Games.IndexOf(game) + 1, includeLogs: true));
    }

    if (string.IsNullOrEmpty(match.OpponentDeckArchetype))
    {
      await DetectAndPersistOpponentArchetypeAsync(match, currentUser);
    }

    return Ok(new MatchDetailsDTO
    {
      Id = match.Id,
      EventId = match.EventId,
      MatchNumber = match.MatchNumber,
      EventName = match.Event.Description,
      Format = match.Event.Format,
      StartTime = match.Event.StartTime,
      Result = GetMatchResult(match, currentUser, isActive),
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
  /// An omitted color selection preserves the recorded colors; an empty list identifies a colorless deck.
  /// </summary>
  public sealed record UpdateOpponentArchetypeRequest(string Archetype, List<string>? Colors = null);

  public sealed record OpponentArchetypeUpdate(int MatchId, string? Archetype, List<string>? Colors);
  public sealed record OpponentArchetypeOption(string Name, List<string>? Colors);

  /// <summary>
  /// Manually update opponent archetype and deck colors for a match.
  /// </summary>
  [HttpPut("match/{matchId:int}/opponent-archetype")]
  [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<object>> UpdateOpponentArchetype(
    int matchId,
    [FromBody] UpdateOpponentArchetypeRequest request,
    CancellationToken cancellationToken)
  {
    var result = await ApplyOpponentArchetypesAsync([new(matchId, request.Archetype, request.Colors)],
      request.Colors == null, cancellationToken);
    if (result.Result is not OkObjectResult) return result.Result!;
    var match = await context.Matches.FindAsync([matchId], cancellationToken);
    return Ok(new { matchId, opponentDeckArchetype = match!.OpponentDeckArchetype, opponentDeckColors = match.OpponentDeckColors });
  }

  /// <summary>
  /// Replace opponent classifications together and return their previous values for Undo.
  /// </summary>
  [HttpPut("opponent-archetypes")]
  public Task<ActionResult<IReadOnlyList<OpponentArchetypeUpdate>>> UpdateOpponentArchetypes(
    [FromBody] List<OpponentArchetypeUpdate> changes, CancellationToken cancellationToken) =>
    ApplyOpponentArchetypesAsync(changes, false, cancellationToken);

  private async Task<ActionResult<IReadOnlyList<OpponentArchetypeUpdate>>> ApplyOpponentArchetypesAsync(
    List<OpponentArchetypeUpdate> changes, bool preserveColors, CancellationToken cancellationToken)
  {
    if (!clientProvider.TryGetCurrentUsername(out var username)) return BadRequest("Client not ready.");
    if (changes.Count is < 1 or > 200 || changes.Any(change => change is null || change.MatchId <= 0) ||
      changes.Select(change => change.MatchId).Distinct().Count() != changes.Count)
      return BadRequest("Choose between 1 and 200 distinct matches.");
    if (changes.Any(change => change.Archetype?.Length > 120 ||
      change.Colors?.Any(color => color is null || color.Length != 1 || !VidereCardColors.IsCanonical(color[0])) == true))
      return BadRequest("Use an archetype name up to 120 characters and W, U, B, R, or G for colors.");
    var ids = changes.Select(change => change.MatchId).ToArray();
    var matches = await context.MatchesForPlayer(username).AsTracking().Where(match => ids.Contains(match.Id))
      .ToDictionaryAsync(match => match.Id, cancellationToken);
    if (matches.Count != ids.Length) return NotFound("Some selected matches are not in your history.");
    var previous = matches.Values.Select(match => new OpponentArchetypeUpdate(
      match.Id, match.OpponentDeckArchetype, match.OpponentDeckColors?.ToList())).ToList();
    foreach (var change in changes)
    {
      var match = matches[change.MatchId];
      match.OpponentDeckArchetype = string.IsNullOrWhiteSpace(change.Archetype) ? null : change.Archetype.Trim();
      if (!preserveColors) match.OpponentDeckColors = change.Colors == null ? null : VidereCardColors.Normalize(change.Colors).ToList();
    }
    await context.SaveChangesAsync(cancellationToken);
    return Ok(previous);
  }

  /// <summary>
  /// Previously recorded opponent classifications for the archetype picker.
  /// </summary>
  [HttpGet("opponent-archetypes")]
  public async Task<ActionResult<IReadOnlyList<OpponentArchetypeOption>>> GetOpponentArchetypes(
    string? format, CancellationToken cancellationToken)
  {
    if (!clientProvider.TryGetCurrentUsername(out var username)) return BadRequest("Client not ready.");
    var rows = await context.MatchesForPlayer(username, format: format)
      .Where(match => match.OpponentDeckArchetype != null && match.OpponentDeckArchetype != "")
      .OrderByDescending(match => match.Event.StartTime)
      .Select(match => new { match.OpponentDeckArchetype, match.OpponentDeckColors, match.Event.Format })
      .ToListAsync(cancellationToken);
    return Ok(rows.GroupBy(row => (row.Format, Name: row.OpponentDeckArchetype!.Trim().ToUpperInvariant()))
      .Select(group => new OpponentArchetypeOption(group.First().OpponentDeckArchetype!.Trim(),
        group.FirstOrDefault(row => row.OpponentDeckColors != null)?.OpponentDeckColors))
      .OrderBy(option => option.Name).ToList());
  }

  /// <summary>
  /// Distinct cards revealed by opponents in the selected recorded matches.
  /// </summary>
  [HttpGet("opponent-cards")]
  public async Task<ActionResult<IReadOnlyList<CardEntry>>> GetOpponentCards(
    [FromQuery] int[] matchIds, CancellationToken cancellationToken)
  {
    if (!clientProvider.TryGetCurrentUsername(out var username)) return BadRequest("Client not ready.");
    if (matchIds.Length is < 1 or > 200) return BadRequest("Choose between 1 and 200 matches.");
    var ids = matchIds.Distinct().ToArray();
    if (await context.MatchesForPlayer(username).CountAsync(match => ids.Contains(match.Id), cancellationToken) != ids.Length)
      return NotFound("Some selected matches are not in your history.");
    var cards = await ReadOpponentCardsAsync(ids, username, cancellationToken);
    return Ok(cards.Values.SelectMany(cards => cards).DistinctBy(card => card.name, StringComparer.OrdinalIgnoreCase).ToList());
  }

  private async Task<Dictionary<int, IReadOnlyList<CardEntry>>> ReadOpponentCardsAsync(
    IEnumerable<int> matchIds, string username, CancellationToken cancellationToken)
  {
    var ids = matchIds.ToArray();
    var cards = await context.GameCards.AsNoTracking().Where(card => ids.Contains(card.Game.MatchId) &&
      !card.IsToken && !card.IsActivatedAbility && !card.IsTriggeredAbility && card.Name != "" &&
      card.Game.Players.Any(player => player.PlayerIndex == card.OwnerId &&
        EF.Functions.Collate(player.Name, "NOCASE") != username))
      .Select(card => new { card.Game.MatchId, card.Name, card.CatalogId, card.IsLand })
      .ToListAsync(cancellationToken);
    return cards.GroupBy(card => card.MatchId).ToDictionary(group => group.Key, group =>
      (IReadOnlyList<CardEntry>)group.GroupBy(card => card.Name, StringComparer.OrdinalIgnoreCase)
        .Select(entries => new CardEntry(entries.FirstOrDefault(card => card.CatalogId > 0)?.CatalogId ?? 0, entries.Key, 1,
          types: entries.First().IsLand ? ["Land"] : null))
        .OrderBy(card => card.types?.Contains("Land") == true).ThenBy(card => card.name).ToList());
  }

  private async Task<(string? Archetype, List<string>? Colors)> DetectAndPersistOpponentArchetypeAsync(
    MatchModel match,
    string currentUser,
    CancellationToken cancellationToken = default)
  {
    bool hasArchetype = !string.IsNullOrEmpty(match.OpponentDeckArchetype);
    bool hasColors = match.OpponentDeckColors is not null;

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

      var finalColors = match.OpponentDeckColors ?? opponentColors;

      if (finalArchetype != match.OpponentDeckArchetype || (!hasColors && finalColors.Count > 0))
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

  [HttpGet("/api/games/formats")]
  public async Task<ActionResult<List<string>>> GetFormats([FromQuery] string? gameType = null)
  {
    if (!clientProvider.TryGetCurrentUsername(out var currentUser)) return BadRequest("Client not ready.");
    if (!DeckFormatResolver.TryGetGameType(gameType, clientProvider.Pid, out var formatType, out var legacyFormats))
      return BadRequest("Choose all, constructed, or limited games.");
    var formats = await context.MatchesForPlayer(currentUser, formatType: formatType, legacyFormats: legacyFormats)
      .Select(match => match.Event.Format)
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
    [FromQuery] string? format,
    [FromQuery] string? gameType = null)
  {
    if (!clientProvider.TryGetCurrentUsername(out var currentUser))
    {
      return BadRequest("Client not ready or user not logged in.");
    }

    if (!DeckFormatResolver.TryGetGameType(gameType, clientProvider.Pid, out var formatType, out var legacyFormats))
      return BadRequest("Choose all, constructed, or limited games.");
    var matches = await context.MatchesForPlayer(currentUser, minDate, maxDate, format, completedOnly: true,
      formatType: formatType, legacyFormats: legacyFormats)
      .Include(match => match.Games).ThenInclude(game => game.Players)
      .AsSplitQuery().ToListAsync();
    await ReadPlayDrawAsync(context, matches.SelectMany(match => match.Games)
      .Where(game => game.GameNumber == 1), currentUser);
    var stateSpans = await ReadGameStateSpansAsync(context, matches.SelectMany(match => match.Games).Select(game => game.Id));

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

      var playerResult = match.PlayerResults.FirstOrDefault(p => string.Equals(p.Player, currentUser, StringComparison.OrdinalIgnoreCase));
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

      // Match play/draw comes from Game 1, which may be absent from a partial recording.
      var firstGame = match.Games.FirstOrDefault(game => game.GameNumber == 1);
      if (firstGame != null)
      {
        var gameResult = firstGame.GamePlayerResults.FirstOrDefault(p => string.Equals(p.Player, currentUser, StringComparison.OrdinalIgnoreCase));
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
        var gameResult = game.GamePlayerResults.FirstOrDefault(p => string.Equals(p.Player, currentUser, StringComparison.OrdinalIgnoreCase));
        if (gameResult != null)
        {
          matchDuration += GetGameDuration(game, gameResult, stateSpans);
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
    [FromQuery] string? format,
    [FromQuery] string? gameType = null)
  {
    if (!clientProvider.TryGetCurrentUsername(out var currentUser))
    {
      return BadRequest("Client not ready or user not logged in.");
    }

    if (!DeckFormatResolver.TryGetGameType(gameType, clientProvider.Pid, out var formatType, out var legacyFormats))
      return BadRequest("Choose all, constructed, or limited games.");
    var matches = await context.MatchesForPlayer(currentUser, minDate, maxDate, format, completedOnly: true,
      formatType: formatType, legacyFormats: legacyFormats)
      .ToListAsync();

    var trendData = new List<PerformanceTrendDTO>();

    if (matches.Count == 0) return Ok(trendData);

    var startDate = matches.Min(m => m.Event.StartTime.Date);
    var endDate = matches.Max(m => m.Event.StartTime.Date);

    // Ensure we cover the requested range if provided
    if (minDate.HasValue && minDate.Value.Date < startDate) startDate = minDate.Value.Date;
    if (maxDate.HasValue && maxDate.Value.Date > endDate) endDate = maxDate.Value.Date;

    var days = matches.GroupBy(match => match.Event.StartTime.Date).ToDictionary(group => group.Key,
      group => (Matches: group.Count(), Wins: group.Count(match => match.PlayerResults.FirstOrDefault(player =>
        string.Equals(player.Player, currentUser, StringComparison.OrdinalIgnoreCase))?.Result == MatchResult.Win)));
    var window = new Queue<(int Matches, int Wins)>();
    int windowMatches = 0, windowWins = 0;
    var dateCursor = startDate;
    int loopCounter = 0;
    while (dateCursor <= endDate)
    {
      if (++loopCounter % 128 == 0) await Task.Yield();
      var day = days.GetValueOrDefault(dateCursor);
      int n = day.Matches;
      int wins = day.Wins;

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

      // Weight the seven-day window by matches, including days with no matches.
      window.Enqueue(day);
      windowMatches += day.Matches;
      windowWins += day.Wins;
      if (window.Count > 7)
      {
        var expired = window.Dequeue();
        windowMatches -= expired.Matches;
        windowWins -= expired.Wins;
      }
      double? rollingAvg = windowMatches > 0 ? Math.Round((double) windowWins / windowMatches * 100, 1) : null;

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

      if (dateCursor == endDate) break;
      dateCursor = dateCursor.AddDays(1);
    }

    return Ok(trendData);
  }


}
