/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using MTGOSDK.API.Collection;
using MTGOSDK.API.Graphics;
using MTGOSDK.API.Play;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Serialization;

using MTGOTracker.Controllers.Base;
using MTGOTracker.Database;
using MTGOTracker.Database.Models;
using MTGOTracker.Database.Models.Collection;
using MTGOTracker.Controllers.Models.Decks;
using MTGOTracker.Services.MTGO;
using MTGOTracker.Services.MTGO.Collection;
using MTGOTracker.Services.Videre;
using MTGOTracker.WebView;


namespace MTGOTracker.Controllers;

/// <summary>
/// Decks management API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public partial class DecksController : APIController
{
  private readonly EventContext context;
  private readonly CollectionContext collection;
  private readonly CollectionDeckService deckService;
  private readonly IManafoldArchetypeClient manafoldArchetypeClient;
  private readonly IClientAPIProvider clientProvider;
  private readonly WebViewHostAccessor webViewHost;

  public DecksController(
    EventContext context,
    CollectionContext collection,
    CollectionDeckService deckService,
    IManafoldArchetypeClient manafoldArchetypeClient,
    IClientAPIProvider clientProvider,
    WebViewHostAccessor webViewHost)
  {
    this.context = context;
    this.collection = collection;
    this.deckService = deckService;
    this.manafoldArchetypeClient = manafoldArchetypeClient;
    this.clientProvider = clientProvider;
    this.webViewHost = webViewHost;
  }

  //
  // Serialization Interfaces
  //

  /// <summary>
  /// Interface for card property serialization from MTGO client.
  /// </summary>
  public interface ICardData
  {
    int Id { get; }
    string Name { get; }
    int Quantity { get; }
    int ConvertedManaCost { get; }
    string Colors { get; }
    IList<string> Types { get; }
    string Rarity { get; }
  }

  private sealed class DeckMatchRecord
  {
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Ties { get; set; }
  }

  /// <summary>
  /// Get all decks grouped by format
  /// </summary>
  /// <returns>Dictionary of format to list of decks</returns>
  [HttpGet] // GET /api/decks
  [ProducesResponseType(typeof(Dictionary<string, List<DeckDTO>>), StatusCodes.Status200OK)]
  public async Task<ActionResult<Dictionary<string, List<DeckDTO>>>> GetDecks(
    CancellationToken cancellationToken)
  {
    var decks = (await deckService.GetCurrentDecksAsync(cancellationToken))
      .OrderByDescending(deck => deck.Timestamp)
      .ToList();
    Dictionary<long, DeckMatchRecord> matchRecords =
      await GetDeckMatchRecordsAsync(decks, cancellationToken);

    var grouped = decks
      .GroupBy(deck => deck.Format)
      .ToDictionary(
        group => group.Key,
        group => group.Select(deck =>
        {
          DeckMatchRecord? record = matchRecords.GetValueOrDefault(
            deck.CardGroupingId);
          return new DeckDTO
          {
            RevisionId = deck.RevisionId,
            NetDeckId = deck.NetDeckId,
            Name = deck.Name,
            Format = deck.Format,
            Timestamp = deck.Timestamp,
            MainboardCount = deck.Mainboard.Sum(card => card.quantity),
            SideboardCount = deck.Sideboard.Sum(card => card.quantity),
            Wins = record?.Wins ?? 0,
            Losses = record?.Losses ?? 0,
            Ties = record?.Ties ?? 0,
            Archetype = deck.Archetype,
            Colors = deck.Colors,
            FeaturedCards = deck.FeaturedCards.ToList(),
          };
        }).ToList());

    return Ok(grouped);
  }

  /// <summary>
  /// Import or update a Tracker-local deck in SQLite.
  /// </summary>
  [HttpPost("import")] // POST /api/decks/import
  [ProducesResponseType(typeof(ImportDeckResponse), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(ImportDeckResponse), StatusCodes.Status201Created)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<ImportDeckResponse>> ImportDeck(
    [FromBody] ImportDeckRequest request,
    CancellationToken cancellationToken)
  {
    try
    {
      ImportDeckResponse result = await deckService.ImportLocalDeckAsync(
        request,
        cancellationToken);
      return result.Created
        ? CreatedAtAction(nameof(GetDeck), new { revisionId = result.RevisionId }, result)
        : Ok(result);
    }
    catch (ArgumentException exception)
    {
      return BadRequest(new { error = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
      return Conflict(new { error = exception.Message });
    }
  }

  /// <summary>
  /// Imports a Tracker-local deck and opens it in the deck editor.
  /// </summary>
  [HttpPost("import-and-open")]
  [ProducesResponseType(typeof(ImportDeckResponse), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(ImportDeckResponse), StatusCodes.Status201Created)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<ImportDeckResponse>> ImportDeckAndOpen(
    [FromBody] ImportDeckRequest request,
    CancellationToken cancellationToken)
  {
    try
    {
      ImportDeckResponse result = await deckService.ImportLocalDeckAsync(
        request,
        cancellationToken);
      webViewHost.HostForm?.NavigateToPath($"/decks/{result.RevisionId}");
      return result.Created
        ? CreatedAtAction(nameof(GetDeck), new { revisionId = result.RevisionId }, result)
        : Ok(result);
    }
    catch (ArgumentException exception)
    {
      return BadRequest(new { error = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
      return Conflict(new { error = exception.Message });
    }
  }

  /// <summary>
  /// Add copies of a card to a deck's mainboard or sideboard.
  /// </summary>
  [HttpPost("{revisionId:long}/cards")]
  [ProducesResponseType(typeof(DeckMutationResponse), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<DeckMutationResponse>> AddDeckCard(
    long revisionId,
    [FromBody] DeckCardMutationRequest request,
    CancellationToken cancellationToken)
  {
    try
    {
      return Ok(await deckService.AddCardAsync(
        revisionId,
        request,
        cancellationToken));
    }
    catch (KeyNotFoundException exception)
    {
      return NotFound(new { error = exception.Message });
    }
    catch (ArgumentException exception)
    {
      return BadRequest(new { error = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
      return Conflict(new { error = exception.Message });
    }
  }

  /// <summary>
  /// Remove copies of a card from a deck's mainboard or sideboard.
  /// </summary>
  [HttpDelete("{revisionId:long}/cards")]
  [ProducesResponseType(typeof(DeckMutationResponse), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<DeckMutationResponse>> RemoveDeckCard(
    long revisionId,
    [FromBody] DeckCardMutationRequest request,
    CancellationToken cancellationToken)
  {
    try
    {
      return Ok(await deckService.RemoveCardAsync(
        revisionId,
        request,
        cancellationToken));
    }
    catch (KeyNotFoundException exception)
    {
      return NotFound(new { error = exception.Message });
    }
    catch (ArgumentException exception)
    {
      return BadRequest(new { error = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
      return Conflict(new { error = exception.Message });
    }
  }

  /// <summary>
  /// Delete a deck from Tracker and queue deletion from MTGO.
  /// </summary>
  [HttpDelete("{revisionId:long}")]
  [ProducesResponseType(typeof(DeckMutationResponse), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<DeckMutationResponse>> DeleteDeck(
    long revisionId,
    CancellationToken cancellationToken)
  {
    try
    {
      return Ok(await deckService.DeleteLocalDeckAsync(
        revisionId,
        cancellationToken));
    }
    catch (KeyNotFoundException exception)
    {
      return NotFound(new { error = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
      return Conflict(new { error = exception.Message });
    }
  }

  /// <summary>
  /// Move copies between deck zones in one saved revision.
  /// </summary>
  [HttpPost("{revisionId:long}/cards/move")]
  [ProducesResponseType(typeof(DeckMutationResponse), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status400BadRequest)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status409Conflict)]
  public async Task<ActionResult<DeckMutationResponse>> MoveDeckCards(
    long revisionId,
    [FromBody] DeckCardMoveRequest request,
    CancellationToken cancellationToken)
  {
    try
    {
      return Ok(await deckService.MoveCardsAsync(revisionId, request, cancellationToken));
    }
    catch (KeyNotFoundException exception)
    {
      return NotFound(new { error = exception.Message });
    }
    catch (ArgumentException exception)
    {
      return BadRequest(new { error = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
      return Conflict(new { error = exception.Message });
    }
  }

  private async Task<Dictionary<long, DeckMatchRecord>> GetDeckMatchRecordsAsync(
    IReadOnlyCollection<DeckRevisionView> decks,
    CancellationToken cancellationToken)
  {
    var records = new Dictionary<long, DeckMatchRecord>();
    string? currentUser = clientProvider.CurrentUser?.Username;
    if (decks.Count == 0 || string.IsNullOrWhiteSpace(currentUser))
      return records;

    IReadOnlyDictionary<long, long> groupingIdsByRevision =
      await deckService.GetRevisionGroupingIdsAsync(
        decks.Select(deck => deck.CardGroupingId),
        cancellationToken);
    if (groupingIdsByRevision.Count == 0)
      return records;

    long[] relevantRevisionIds = groupingIdsByRevision.Keys.ToArray();
    var events = await context.Events
      .AsNoTracking()
      .Include(eventModel => eventModel.Matches)
      .Where(eventModel =>
        eventModel.DeckRevisionId != null &&
        relevantRevisionIds.Contains(eventModel.DeckRevisionId.Value))
      .ToListAsync(cancellationToken);
    if (events.Count == 0)
      return records;

    foreach (var eventModel in events)
    {
      if (eventModel.DeckRevisionId is not long revisionId ||
          !groupingIdsByRevision.TryGetValue(revisionId, out long groupingId))
      {
        continue;
      }

      foreach (var match in eventModel.Matches)
      {
        PlayerResult? result = match.PlayerResults.FirstOrDefault(player =>
          string.Equals(
            player.Player,
            currentUser,
            StringComparison.OrdinalIgnoreCase));
        if (result == null || result.Result == MatchResult.NotSet)
          continue;

        if (!records.TryGetValue(groupingId, out DeckMatchRecord? record))
        {
          record = new DeckMatchRecord();
          records[groupingId] = record;
        }

        switch (result.Result)
        {
          case MatchResult.Win:
            record.Wins++;
            break;
          case MatchResult.Loss:
            record.Losses++;
            break;
          case MatchResult.Draw:
            record.Ties++;
            break;
        }
      }
    }

    return records;
  }
  /// <summary>
  /// Get a flat list of all deck identifiers
  /// </summary>
  /// <returns>List of deck identifiers</returns>
  [HttpGet("identifiers")] // GET /api/decks/identifiers
  [ProducesResponseType(typeof(List<DeckIdentifierDTO>), StatusCodes.Status200OK)]
  public async Task<ActionResult<List<DeckIdentifierDTO>>> GetDeckIdentifiers(
    CancellationToken cancellationToken)
  {
    var decks = (await deckService.GetCurrentDecksAsync(cancellationToken))
      .OrderByDescending(deck => deck.Timestamp)
      .Select(deck => new DeckIdentifierDTO
      {
        RevisionId = deck.RevisionId,
        NetDeckId = deck.NetDeckId,
        Name = deck.Name,
        Format = deck.Format
      })
      .ToList();

    return Ok(decks);
  }

  /// <summary>
  /// Get a single deck revision with full card details
  /// </summary>
  /// <param name="revisionId">Collection-history deck revision ID</param>
  /// <returns>Full deck details</returns>
  [HttpGet("{revisionId:long}")]
  [ProducesResponseType(typeof(DeckDetailDTO), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<DeckDetailDTO>> GetDeck(long revisionId)
  {
    var deck = await deckService.GetRevisionAsync(revisionId);
    if (deck == null)
    {
      return NotFound(new { error = $"Deck revision {revisionId} not found" });
    }

    return Ok(new DeckDetailDTO
    {
      RevisionId = deck.RevisionId,
      CurrentRevisionId = await collection.CardGroupingRevisions
        .Where(revision => revision.CardGroupingId == deck.CardGroupingId &&
          revision.RevisionType != CardGroupingRevisionType.Deleted)
        .Select(revision => (long?)revision.Id).MaxAsync() ?? deck.RevisionId,
      NetDeckId = deck.NetDeckId,
      Name = deck.Name,
      Format = deck.Format,
      Timestamp = deck.Timestamp,
      Archetype = deck.Archetype,
      Colors = deck.Colors,
      Mainboard = deck.Mainboard,
      Sideboard = deck.Sideboard
    });
  }

  /// <summary>
  /// Get full revision history for a deck
  /// </summary>
  /// <param name="revisionId">Collection-history deck revision ID</param>
  /// <returns>History of revisions with card diffs</returns>
  [HttpGet("{revisionId:long}/history")]
  [ProducesResponseType(typeof(DeckHistoryView), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<DeckHistoryView>> GetDeckHistory(
    long revisionId,
    CancellationToken cancellationToken)
  {
    var history = await deckService.GetDeckHistoryAsync(revisionId, cancellationToken);
    if (history == null)
    {
      return NotFound(new { error = $"Deck revision {revisionId} history not found" });
    }
    return Ok(history);
  }

  /// <summary>
  /// Streams framed card images as NDJSON. The first line describes the sheet;
  /// subsequent lines contain each rendered row and its starting slot index.
  /// </summary>
  [HttpGet("sheet")]
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> GetDeckSheet(
    [FromQuery] string? name = null,
    [FromQuery] string? id = null,
    [FromQuery] int columns = 5,
    [FromQuery] int cardHeight = 300)
  {
    int[] catalogIds;

    if (!string.IsNullOrEmpty(id))
    {
      if (!long.TryParse(id, out long revisionId))
        return BadRequest(new { error = "Deck ID must be a revision ID" });
      var deck = await deckService.GetRevisionAsync(revisionId, HttpContext.RequestAborted);
      if (deck == null)
        return NotFound(new { error = $"Deck revision {revisionId} not found" });

      // Slot indices must match the ordering used by GetSortableCards.
      catalogIds = deck.Mainboard
        .Concat(deck.Sideboard)
        .Select(c => c.catalogId)
        .Distinct()
        .ToArray();
    }
    else if (!string.IsNullOrEmpty(name))
    {
      var deck = CollectionManager.GetDeck(name);
      if (deck == null)
        return NotFound(new { error = $"Deck '{name}' not found in MTGO client" });

      // Sort by name (matches GetSortableCards name-path ordering)
      var cardData = await deck.SerializeItemsAsAsync<ICardData>();
      catalogIds = cardData
        .OrderBy(c => GetSortableName(c.Name))
        .ThenBy(c => c.Id)
        .Select(c => c.Id)
        .Distinct()
        .ToArray();
    }
    else
    {
      return BadRequest(new { error = "Must provide either 'id' or 'name' parameter" });
    }

    int cardWidth = (int)Math.Ceiling(cardHeight * (5.0 / 7.0));
    int total     = catalogIds.Length;

    StartNDJSONResponse();

    await WriteCompactNDJSONLine(
      new { type = "meta", columns, cardWidth, cardHeight, total },
      HttpContext.RequestAborted);

    int startIndex = 0;
    foreach (var rowCards in CardRenderer.RenderCardsRowByRow(catalogIds, columns, cardHeight))
    {
      if (HttpContext.RequestAborted.IsCancellationRequested) break;
      await WriteCompactNDJSONLine(
        new { type = "cards", startIndex, cards = rowCards },
        HttpContext.RequestAborted);
      startIndex += rowCards.Length;
    }

    return new EmptyResult();
  }

  /// <summary>
  /// Reads sortable card metadata from a saved revision or a live deck name.
  /// </summary>
  /// <param name="name">Deck name (looks up from MTGO client)</param>
  /// <param name="id">Saved deck revision ID; takes precedence over name.</param>
  /// <returns>List of cards with sortable metadata (CMC, Colors, Types, Rarity)</returns>
  [HttpGet("sortable")] // GET /api/decks/sortable?name=Plains&id=REVISION_ID
  [ProducesResponseType(typeof(List<SortableCardEntry>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<List<SortableCardEntry>>> GetSortableCards(
    [FromQuery] string? name = null,
    [FromQuery] string? id = null)
  {
    Deck? deck = null;

    if (!string.IsNullOrEmpty(id))
    {
      if (!long.TryParse(id, out long revisionId))
        return BadRequest(new { error = "Deck ID must be a revision ID" });
      var revision = await deckService.GetRevisionAsync(revisionId, HttpContext.RequestAborted);
      if (revision == null)
      {
        return NotFound(new { error = $"Deck revision {revisionId} not found" });
      }
      return Ok(BuildSortableCards(revision));
    }
    else if (!string.IsNullOrEmpty(name))
    {
      Log.Trace($"Looking up deck '{name}' in MTGO client");
      deck = CollectionManager.GetDeck(name);
      if (deck == null)
      {
        return NotFound(new { error = $"Deck '{name}' not found in MTGO client" });
      }
      Log.Trace($"Found deck '{name}' in MTGO client");
    }
    else
    {
      return BadRequest(new { error = "Must provide either 'id' or 'name' parameter" });
    }

    // Single IPC call using cross-card batch serialization to get metadata
    Log.Trace($"Serializing deck to cards");
    var cardDataList = await deck.SerializeItemsAsAsync<ICardData>();
    Log.Trace($"Serialized deck.");

    var resultList = new List<SortableCardEntry>();
    var sorted = cardDataList.OrderBy(c => GetSortableName(c.Name)).ThenBy(c => c.Id).ToList();
    await deckService.EnsureCatalogAsync(
      sorted.Select(card => card.Id),
      HttpContext.RequestAborted);
    for (int i = 0; i < sorted.Count; i++)
    {
      var c = sorted[i];
      CardEntry catalog = deckService.GetCatalogCard(c.Id, c.Quantity);
      resultList.Add(new SortableCardEntry
      {
        Index = i,
        OriginalIndex = i,
        CatalogId = c.Id,
        OtherFaceCatalogId = catalog.otherFaceCatalogId,
        Name = string.IsNullOrWhiteSpace(catalog.name) ? c.Name : catalog.name,
        Quantity = c.Quantity,
        Zone = "Mainboard",
        Cmc = catalog.cmc,
        Colors = catalog.colors is { Count: > 0 }
          ? catalog.colors
          : c.Colors?.ToCharArray().Select(ch => ch.ToString()).ToList() ?? new List<string>(),
        Types = catalog.types is { Count: > 0 }
          ? catalog.types
          : c.Types?.ToList() ?? new List<string>(),
        Rarity = string.IsNullOrWhiteSpace(catalog.rarity) ? c.Rarity : catalog.rarity
      });
    }

    return Ok(resultList);
  }

  private static List<SortableCardEntry> BuildSortableCards(DeckRevisionView deck)
  {
    var sheetIndexById = deck.Mainboard
      .Concat(deck.Sideboard)
      .Select(c => c.catalogId)
      .Distinct()
      .Select((id, i) => (id, i))
      .ToDictionary(t => t.id, t => t.i);

    var sortedCards = MergeCardEntries(deck.Mainboard)
      .Select(entry => (Entry: entry, Zone: "Mainboard"))
      .Concat(MergeCardEntries(deck.Sideboard)
        .Select(entry => (Entry: entry, Zone: "Sideboard")))
      .OrderBy(c => GetSortableName(string.IsNullOrWhiteSpace(c.Entry.name) ? c.Entry.catalogId.ToString() : c.Entry.name))
      .ThenBy(c => c.Entry.catalogId)
      .ThenBy(c => c.Zone == "Mainboard" ? 0 : 1)
      .ToList();

    var resultList = new List<SortableCardEntry>();
    for (var idx = 0; idx < sortedCards.Count; idx++)
    {
      var (entry, zone) = sortedCards[idx];
      if (entry.quantity <= 0) continue;

      resultList.Add(new SortableCardEntry
      {
        Index = resultList.Count,
        OriginalIndex = sheetIndexById.TryGetValue(entry.catalogId, out var originalIndex) ? originalIndex : idx,
        CatalogId = entry.catalogId,
        Name = string.IsNullOrWhiteSpace(entry.name) ? entry.catalogId.ToString() : entry.name,
        OtherFaceCatalogId = entry.otherFaceCatalogId,
        Quantity = entry.quantity,
        Zone = zone,
        Cmc = Math.Max(0, entry.cmc),
        Colors = entry.colors?.ToList() ?? [],
        Types = entry.types?.ToList() ?? [],
        Rarity = string.IsNullOrWhiteSpace(entry.rarity) ? "common" : entry.rarity
      });
    }

    return resultList;
  }

  private static List<CardEntry> MergeCardEntries(IEnumerable<CardEntry> entries)
  {
    return entries
      .GroupBy(c => c.catalogId)
      .Select(g =>
      {
        CardEntry first = g.First();
        return first with { quantity = g.Sum(c => c.quantity) };
      })
      .ToList();
  }

  /// <summary>
  /// Converts a card name to its sortable form, matching MTGO's internal SortableName logic.
  /// Removes special characters: '[', ']', '@' (and the character following '@')
  /// </summary>
  private static string GetSortableName(string? name)
  {
    if (string.IsNullOrEmpty(name)) return "";
    
    // Fast path: if no special chars, return as-is
    if (name.IndexOfAny(new[] { '[', ']', '@' }) < 0) return name;
    
    var sb = new System.Text.StringBuilder(name.Length);
    bool skipNext = false;
    foreach (char c in name)
    {
      if (skipNext) { skipNext = false; continue; }
      if (c == '@') { skipNext = true; continue; }
      if (c == '[' || c == ']') continue;
      sb.Append(c);
    }
    return sb.ToString();
  }

  /// <summary>
  /// Get archetype information for a deck from the Manafold API
  /// </summary>
  /// <param name="revisionId">Collection-history deck revision ID</param>
  /// <returns>Raw Manafold API response</returns>
  [HttpGet("/api/decks/{revisionId:long}/archetype")]
  [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status502BadGateway)]
  public async Task<ActionResult<object>> GetArchetype(
    long revisionId,
    CancellationToken cancellationToken)
  {
    var deck = await deckService.GetRevisionAsync(revisionId, cancellationToken);
    if (deck == null)
    {
      return NotFound(new { error = $"Deck revision {revisionId} not found" });
    }

    // Build request body with card names and quantities
    var cards = deck.Mainboard
      .Select(card => new ManafoldDeckCard(card.name, card.quantity))
      .ToList();

    if (cards.Count < 2)
    {
      return BadRequest(new { error = "Deck must have at least 2 cards for archetype detection" });
    }

    try
    {
      var manafoldResponse = await manafoldArchetypeClient.DetectArchetypeAsync(
        cards,
        deck.Format,
        cancellationToken);
      var (archetype, featuredCard) = ParseArchetype(manafoldResponse);
      await deckService.SetEnrichmentAsync(
        revisionId,
        archetype,
        featuredCard,
        cancellationToken);
      return Ok(manafoldResponse);
    }
    catch (ManafoldAPIException ex)
    {
      if (ex.StatusCode.HasValue)
      {
        return StatusCode(StatusCodes.Status502BadGateway, new
        {
          error = "Manafold API request failed",
          statusCode = ex.StatusCode.Value,
          response = ex.Response
        });
      }

      return StatusCode(StatusCodes.Status502BadGateway, new
      {
        error = "Failed to connect to Manafold API",
        message = ex.Message
      });
    }
  }

  /// <summary>
  /// Request body for updating a deck archetype.
  /// </summary>
  public sealed record UpdateArchetypeRequest(string Archetype);

  /// <summary>
  /// Manually update archetype information for a deck
  /// </summary>
  /// <param name="revisionId">Collection-history deck revision ID</param>
  /// <param name="request">Request containing new archetype name</param>
  /// <returns>Updated archetype info</returns>
  [HttpPut("/api/decks/{revisionId:long}/archetype")]
  [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<object>> UpdateArchetype(
    long revisionId,
    [FromBody] UpdateArchetypeRequest request,
    CancellationToken cancellationToken)
  {
    var deck = await deckService.GetRevisionAsync(revisionId, cancellationToken);
    if (deck == null)
    {
      return NotFound(new { error = $"Deck revision {revisionId} not found" });
    }

    string? newArchetype = string.IsNullOrWhiteSpace(request.Archetype) ? null : request.Archetype.Trim();
    await deckService.SetEnrichmentAsync(
      revisionId,
      newArchetype,
      deck.FeaturedCard,
      cancellationToken);

    return Ok(new { revisionId, archetype = newArchetype });
  }

  /// <summary>
  /// Get the colors of a deck by analyzing all cards in the mainboard
  /// </summary>
  /// <param name="revisionId">Collection-history deck revision ID</param>
  /// <returns>List of color characters (W, U, B, R, G)</returns>
  [HttpGet("/api/decks/{revisionId:long}/colors")]
  [ProducesResponseType(typeof(DeckColorsDTO), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<DeckColorsDTO>> GetDeckColors(long revisionId)
  {
    var deck = await deckService.GetRevisionAsync(revisionId);
    if (deck == null)
    {
      return NotFound(new { error = $"Deck revision {revisionId} not found" });
    }

    return Ok(new DeckColorsDTO
    {
      RevisionId = revisionId,
      Colors = deck.Colors,
      ColorString = string.Join("", deck.Colors)
    });
  }

  public static (string? Archetype, string? FeaturedCard) ParseArchetype(
    JsonElement response)
  {
    if (!response.TryGetProperty("data", out var data))
      return (null, null);

    var best = data.EnumerateObject()
      .OrderByDescending(entry => entry.Value.GetDouble())
      .FirstOrDefault();
    if (string.IsNullOrEmpty(best.Name))
      return (null, null);

    string? featuredCard = null;
    if (response.TryGetProperty("predictions", out var predictions) &&
        predictions.ValueKind == JsonValueKind.Array)
    {
      var prediction = predictions.EnumerateArray()
        .FirstOrDefault(item =>
          item.TryGetProperty("label", out var label) &&
          string.Equals(label.GetString(), best.Name, StringComparison.Ordinal));
      if (prediction.ValueKind != JsonValueKind.Undefined &&
          prediction.TryGetProperty("ranking", out var ranking) &&
          ranking.ValueKind == JsonValueKind.Array)
      {
        JsonElement topCard = ranking.EnumerateArray().FirstOrDefault();
        if (topCard.ValueKind != JsonValueKind.Undefined &&
            topCard.TryGetProperty("card", out var card))
        {
          featuredCard = card.GetString();
        }
      }
    }

    return (best.Name, featuredCard);
  }

  /// <summary>
  /// Get aggregated archetype statistics across all decks with win/loss data
  /// </summary>
  /// <param name="minDate">Minimum event start date</param>
  /// <param name="maxDate">Maximum event start date</param>
  /// <param name="format">Format filter</param>
  /// <param name="gameType">All, constructed, or limited games.</param>
  /// <returns>List of archetypes with their top card, winrate, and match count</returns>
  [HttpGet("/api/decks/archetypes/aggregated")] // GET /api/decks/archetypes/aggregated
  [ProducesResponseType(typeof(List<AggregatedArchetypeDTO>), StatusCodes.Status200OK)]
  public async Task<ActionResult<List<AggregatedArchetypeDTO>>> GetAggregatedArchetypes(
    [FromQuery] DateTime? minDate,
    [FromQuery] DateTime? maxDate,
    [FromQuery] string? format,
    [FromQuery] string? gameType = null)
  {
    if (!clientProvider.TryGetCurrentUsername(out var currentUser))
    {
      return Ok(new List<AggregatedArchetypeDTO>());
    }

    if (!DeckFormatResolver.TryGetGameType(gameType, clientProvider.Pid, out var formatType, out var legacyFormats))
      return BadRequest("Choose all, constructed, or limited games.");
    var matches = await context.MatchesForPlayer(currentUser, minDate, maxDate, format, completedOnly: true,
        formatType: formatType, legacyFormats: legacyFormats)
      .Where(match => match.Event.DeckRevisionId.HasValue).ToListAsync();
    var revisions = await deckService.GetRevisionsAsync(matches.Select(match => match.Event.DeckRevisionId!.Value));

    // Track stats per archetype (using only stored data)
    // Key: archetype name -> (wins, losses, ties, colors, featuredCardVotes)
    var archetypeStats = new Dictionary<string, (int wins, int losses, int ties, HashSet<string> colors, Dictionary<string, int> featuredCardVotes)>();

    foreach (var eventMatches in matches.GroupBy(match => match.EventId))
    {
      if (eventMatches.First().Event.DeckRevisionId is not long revisionId ||
          !revisions.TryGetValue(revisionId, out var deck))
      {
        continue;
      }

      // Skip decks without a stored archetype
      if (string.IsNullOrEmpty(deck.Archetype)) continue;

      var archetypeName = deck.Archetype;

      // Calculate wins/losses for this event
      int eventWins = 0, eventLosses = 0, eventTies = 0;
      foreach (var match in eventMatches)
      {
        var playerResult = match.PlayerResults.FirstOrDefault(p => string.Equals(p.Player, currentUser, StringComparison.OrdinalIgnoreCase));
        if (playerResult == null) continue;

        if (playerResult.Result == MatchResult.Win)
          eventWins++;
        else if (playerResult.Result == MatchResult.Loss)
          eventLosses++;
        else
          eventTies++;
      }

      // Skip events with no matches we participated in
      if (eventWins + eventLosses + eventTies == 0) continue;

      // Initialize archetype stats if needed
      if (!archetypeStats.ContainsKey(archetypeName))
        archetypeStats[archetypeName] = (0, 0, 0, new HashSet<string>(), new Dictionary<string, int>());

      var (w, l, t, colors, featuredCardVotes) = archetypeStats[archetypeName];

      // Merge colors from this deck
      foreach (var color in deck.Colors)
      {
        colors.Add(color);
      }

      var featuredCard = deck.FeaturedCards.FirstOrDefault()?.name;
      if (!string.IsNullOrEmpty(featuredCard))
      {
        featuredCardVotes[featuredCard] = featuredCardVotes.GetValueOrDefault(featuredCard) + 1;
      }

      archetypeStats[archetypeName] = (w + eventWins, l + eventLosses, t + eventTies, colors, featuredCardVotes);
    }

    // Build result
    var result = new List<AggregatedArchetypeDTO>();

    foreach (var (archetype, stats) in archetypeStats)
    {
      var (wins, losses, ties, colors, featuredCardVotes) = stats;
      int totalMatches = wins + losses + ties;
      if (totalMatches == 0) continue;

      // Get the most voted featured card
      var topCardName = featuredCardVotes.Count > 0
        ? featuredCardVotes.OrderByDescending(kv => kv.Value).First().Key
        : "";

      var sortedColors = VidereCardColors.Normalize(colors).ToList();

      result.Add(new AggregatedArchetypeDTO
      {
        Archetype = archetype,
        Colors = sortedColors,
        Matches = totalMatches,
        Wins = wins,
        Losses = losses,
        Winrate = Math.Round((double)wins / totalMatches * 100, 1),
        TopCard = topCardName
      });
    }

    // Sort by matches descending
    return Ok(result.OrderByDescending(a => a.Matches).ToList());
  }

}
