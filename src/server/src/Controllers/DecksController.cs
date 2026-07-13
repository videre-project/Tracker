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

using Tracker.Controllers.Base;
using Tracker.Database;
using Tracker.Database.Models;
using Tracker.Controllers.Models.Decks;
using Tracker.Services.MTGO;
using Tracker.Services.Videre;


namespace Tracker.Controllers;

/// <summary>
/// Decks management API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DecksController : APIController
{
  private readonly EventContext context;
  private readonly IServiceScopeFactory scopeFactory;
  private readonly INBACArchetypeClient nbacArchetypeClient;
  private readonly IClientAPIProvider clientProvider;

  public DecksController(
    EventContext context,
    IServiceScopeFactory scopeFactory,
    INBACArchetypeClient nbacArchetypeClient,
    IClientAPIProvider clientProvider)
  {
    this.context = context;
    this.scopeFactory = scopeFactory;
    this.nbacArchetypeClient = nbacArchetypeClient;
    this.clientProvider = clientProvider;
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

  /// <summary>
  /// Simple record implementing ICardData for manual property extraction
  /// </summary>
  private sealed record CardDataRecord(
    int Id,
    string Name,
    int Quantity,
    int ConvertedManaCost,
    string Colors,
    IList<string> Types,
    string Rarity
  ) : ICardData;

  /// <summary>
  /// Get all decks grouped by format
  /// </summary>
  /// <returns>Dictionary of format to list of decks</returns>
  [HttpGet] // GET /api/decks
  [ProducesResponseType(typeof(Dictionary<string, List<DeckDTO>>), StatusCodes.Status200OK)]
  public async Task<ActionResult<Dictionary<string, List<DeckDTO>>>> GetDecks()
  {
    var decks = await context.Decks
      .OrderByDescending(d => d.Timestamp)
      .ToListAsync();

    var grouped = decks
      .GroupBy(d => d.Format)
      .ToDictionary(
        g => g.Key,
        g => g.Select(d => new DeckDTO
        {
          Hash = d.Hash,
          Id = d.Id,
          Name = d.Name,
          Format = d.Format,
          Timestamp = d.Timestamp,
          MainboardCount = d.Mainboard.Sum(c => c.quantity),
          SideboardCount = d.Sideboard.Sum(c => c.quantity),
          Archetype = d.Archetype,
          Colors = d.Colors,
          FeaturedCards = d.Mainboard
            .OrderByDescending(c => c.quantity)
            .Take(5)
            .ToList()
        }).ToList()
      );

    return Ok(grouped);
  }

  /// <summary>
  /// Get a flat list of all deck identifiers
  /// </summary>
  /// <returns>List of deck identifiers</returns>
  [HttpGet("identifiers")] // GET /api/decks/identifiers
  [ProducesResponseType(typeof(List<DeckIdentifierDTO>), StatusCodes.Status200OK)]
  public async Task<ActionResult<List<DeckIdentifierDTO>>> GetDeckIdentifiers()
  {
    var decks = await context.Decks
      .OrderByDescending(d => d.Timestamp)
      .Select(d => new DeckIdentifierDTO
      {
        Hash = d.Hash,
        Id = d.Id,
        Name = d.Name,
        Format = d.Format
      })
      .ToListAsync();

    return Ok(decks);
  }

  /// <summary>
  /// Get a single deck by hash with full card details
  /// </summary>
  /// <param name="hash">Deck hash</param>
  /// <returns>Full deck details</returns>
  [HttpGet("{hash}")] // GET /api/decks/{hash}
  [ProducesResponseType(typeof(DeckDetailDTO), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<DeckDetailDTO>> GetDeck(string hash)
  {
    var deck = await context.Decks.FindAsync(hash);
    if (deck == null)
    {
      return NotFound(new { error = $"Deck with hash {hash} not found" });
    }

    return Ok(new DeckDetailDTO
    {
      Hash = deck.Hash,
      Id = deck.Id,
      Name = deck.Name,
      Format = deck.Format,
      Timestamp = deck.Timestamp,
      Mainboard = deck.Mainboard,
      Sideboard = deck.Sideboard
    });
  }

  private record CachedDeck(List<CardEntry> Mainboard, List<CardEntry> Sideboard);

  private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CachedDeck> s_deckCache = new();
  /// <summary>
  /// Render a deck as a streaming NDJSON sequence of framed card images.
  ///
  /// Line 1: <c>{"type":"meta", columns, cardWidth, cardHeight, total}</c>
  ///   — emitted immediately so the client can pre-size its canvas.
  /// Remaining lines: <c>{"type":"cards", startIndex, cards: string[]}</c>
  ///   — emitted in two phases so the first visible row arrives before the
  ///   full deck finishes rendering.
  /// </summary>
  [HttpGet("sheet")] // GET /api/decks/sheet?name=Plains&id=HASH
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<IActionResult> GetDeckSheet(
    [FromQuery] string? name = null,
    [FromQuery] string? id = null,
    [FromQuery] int columns = 5,
    [FromQuery] int cardHeight = 300)
  {
    int[] catalogIds;

    // Prioritize ID/Hash lookup (DB -> Cache)
    if (!string.IsNullOrEmpty(id))
    {
      var cached = await GetOrLoadDeck(id);
      if (cached == null)
        return NotFound(new { error = $"Deck with hash {id} not found in database" });

      // Union of mainboard + sideboard (both grids share the same sheet image).
      // Deduped in DB order — must match the ordering used by GetSortableCards.
      catalogIds = cached.Mainboard
        .Concat(cached.Sideboard)
        .Select(c => c.catalogId)
        .Distinct()
        .ToArray();
    }
    // Fallback to Name lookup (live MTGO client)
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

    // Line 1 — metadata: client pre-sizes the canvas and hides the overlay.
    await WriteCompactNDJSONLine(
      new { type = "meta", columns, cardWidth, cardHeight, total },
      HttpContext.RequestAborted);

    // Stream one row at a time. RenderCardsRowByRow does a single WPF render
    // pass for all cards, then yields each row as soon as its parallel
    // crop+encode finishes — no second render pass, no double overhead.
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
  /// Get sortable card metadata for a deck.
  /// Uses persisted deck entries for deck hashes and live MTGO serialization for deck names.
  /// </summary>
  /// <param name="name">Deck name (looks up from MTGO client)</param>
  /// <param name="id">Deck hash/ID (looks up from DB and caches)</param>
  /// <returns>List of cards with sortable metadata (CMC, Colors, Types, Rarity)</returns>
  [HttpGet("sortable")] // GET /api/decks/sortable?name=Plains&id=HASH
  [ProducesResponseType(typeof(List<SortableCardEntry>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<List<SortableCardEntry>>> GetSortableCards(
    [FromQuery] string? name = null,
    [FromQuery] string? id = null)
  {
    Deck? deck = null;

    // Prioritize ID/Hash lookup (DB -> Cache)
    if (!string.IsNullOrEmpty(id))
    {
      var cachedDeck = await GetOrLoadDeck(id);
      if (cachedDeck == null)
      {
        return NotFound(new { error = $"Deck with hash {id} not found in database" });
      }
      return Ok(BuildCachedSortableCards(cachedDeck));
    }
    // Fallback to Name lookup (Client)
    else if (!string.IsNullOrEmpty(name)) 
    {
      Log.Trace($"Looking up deck '{name}' in MTGO client");
      deck = CollectionManager.GetDeck(name);
      if (deck == null)
      {
        return NotFound(new { error = $"Deck '{name}' not found in MTGO client" });
      }
      // For non-cached decks, we treat everything as 'Mainboard' since we don't have the DB model
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
    for (int i = 0; i < sorted.Count; i++)
    {
      var c = sorted[i];
      resultList.Add(new SortableCardEntry
      {
        Index = i,
        OriginalIndex = i,
        CatalogId = c.Id,
        Name = c.Name,
        Quantity = c.Quantity,
        Zone = "Mainboard",
        Cmc = c.ConvertedManaCost,
        Colors = c.Colors?.ToCharArray().Select(ch => ch.ToString()).ToList() ?? new List<string>(),
        Types = c.Types?.ToList() ?? new List<string>(),
        Rarity = c.Rarity
      });
    }

    return Ok(resultList);
  }

  private static List<SortableCardEntry> BuildCachedSortableCards(CachedDeck cachedDeck)
  {
    var sheetIndexById = cachedDeck.Mainboard
      .Concat(cachedDeck.Sideboard)
      .Select(c => c.catalogId)
      .Distinct()
      .Select((id, i) => (id, i))
      .ToDictionary(t => t.id, t => t.i);

    var sortedCards = MergeCardEntries(cachedDeck.Mainboard)
      .Select(entry => (Entry: entry, Zone: "Mainboard", Card: GetCardData(entry)))
      .Concat(MergeCardEntries(cachedDeck.Sideboard)
        .Select(entry => (Entry: entry, Zone: "Sideboard", Card: GetCardData(entry))))
      .OrderBy(c => GetSortableName(c.Card.Name))
      .ThenBy(c => c.Entry.catalogId)
      .ThenBy(c => c.Zone == "Mainboard" ? 0 : 1)
      .ToList();

    var resultList = new List<SortableCardEntry>();
    for (var idx = 0; idx < sortedCards.Count; idx++)
    {
      var (entry, zone, card) = sortedCards[idx];
      if (entry.quantity <= 0) continue;

      resultList.Add(new SortableCardEntry
      {
        Index = resultList.Count,
        OriginalIndex = sheetIndexById.TryGetValue(entry.catalogId, out var originalIndex) ? originalIndex : idx,
        CatalogId = entry.catalogId,
        Name = card.Name,
        Quantity = entry.quantity,
        Zone = zone,
        Cmc = card.ConvertedManaCost,
        Colors = card.Colors?.ToCharArray().Select(ch => ch.ToString()).ToList() ?? new List<string>(),
        Types = card.Types?.ToList() ?? new List<string>(),
        Rarity = card.Rarity
      });
    }

    return resultList;
  }

  private static List<CardEntry> MergeCardEntries(IEnumerable<CardEntry> entries)
  {
    return entries
      .GroupBy(c => c.catalogId)
      .Select(g => new CardEntry(g.Key, g.First().name, g.Sum(c => c.quantity)))
      .ToList();
  }

  private static ICardData GetCardData(CardEntry entry)
  {
    var fallbackName = string.IsNullOrWhiteSpace(entry.name)
      ? entry.catalogId.ToString()
      : entry.name;

    try
    {
      var card = CollectionManager.GetCard(entry.catalogId);
      if (card == null)
      {
        return new CardDataRecord(entry.catalogId, fallbackName, entry.quantity, 0, "", new List<string>(), "");
      }

      try
      {
        var data = card.SerializeAs<ICardData>();
        return new CardDataRecord(
          entry.catalogId,
          string.IsNullOrWhiteSpace(data.Name) ? fallbackName : data.Name,
          entry.quantity,
          data.ConvertedManaCost,
          data.Colors ?? "",
          data.Types?.ToList() ?? new List<string>(),
          data.Rarity ?? ""
        );
      }
      catch (Exception ex)
      {
        Log.Trace($"Failed to serialize card metadata for catalog ID {entry.catalogId}: {ex.Message}");
      }

      try
      {
        return new CardDataRecord(
          entry.catalogId,
          string.IsNullOrWhiteSpace(card.Name) ? fallbackName : card.Name,
          entry.quantity,
          0,
          card.Colors ?? "",
          new List<string>(),
          ""
        );
      }
      catch (Exception ex)
      {
        Log.Trace($"Failed to read card fallback metadata for catalog ID {entry.catalogId}: {ex.Message}");
      }
    }
    catch (KeyNotFoundException)
    {
      Log.Trace($"Catalog ID {entry.catalogId} was not found while building sortable deck cards.");
    }
    catch (Exception ex)
    {
      Log.Trace($"Failed to resolve card metadata for catalog ID {entry.catalogId}: {ex.Message}");
    }

    return new CardDataRecord(entry.catalogId, fallbackName, entry.quantity, 0, "", new List<string>(), "");
  }

  private async Task<CachedDeck?> GetOrLoadDeck(string hash)
  {
    if (s_deckCache.TryGetValue(hash, out var cached))
      return cached;

    using var scope = scopeFactory.CreateScope();
    var scopedContext = scope.ServiceProvider.GetRequiredService<EventContext>();

    var deckModel = await scopedContext.Decks.FindAsync(hash);
    if (deckModel == null) return null;

    try
    {
      var mainboard = deckModel.Mainboard.ToList();
      var sideboard = deckModel.Sideboard.ToList();

      var result = new CachedDeck(mainboard, sideboard);
      s_deckCache.TryAdd(hash, result);
      return result;
    }
    catch (Exception ex)
    {
      Log.Error($"Failed to construct deck for hash {hash}: {ex}");
      return null;
    }
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
  /// Get archetype information for a deck from the NBAC API
  /// </summary>
  /// <param name="hash">Deck hash</param>
  /// <returns>Raw NBAC API response</returns>
  [HttpGet("/api/decks/{hash}/archetype")] // GET /api/decks/{hash}/archetype
  [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status502BadGateway)]
  public async Task<ActionResult<object>> GetArchetype(
    string hash,
    CancellationToken cancellationToken)
  {
    var deck = await context.Decks.FindAsync(hash);
    if (deck == null)
    {
      return NotFound(new { error = $"Deck with hash {hash} not found" });
    }

    // Build request body with card names and quantities
    var cards = deck.Mainboard
      .Select(card => new NBACDeckCard(card.name, card.quantity))
      .ToList();

    if (cards.Count < 2)
    {
      return BadRequest(new { error = "Deck must have at least 2 cards for archetype detection" });
    }

    try
    {
      var nbacResponse = await nbacArchetypeClient.DetectArchetypeAsync(
        cards,
        deck.Format,
        cancellationToken);
      return Ok(nbacResponse);
    }
    catch (NBACAPIException ex)
    {
      if (ex.StatusCode.HasValue)
      {
        return StatusCode(StatusCodes.Status502BadGateway, new
        {
          error = "NBAC API request failed",
          statusCode = ex.StatusCode.Value,
          response = ex.Response
        });
      }

      return StatusCode(StatusCodes.Status502BadGateway, new
      {
        error = "Failed to connect to NBAC API",
        message = ex.Message
      });
    }
  }

  /// <summary>
  /// Get the colors of a deck by analyzing all cards in the mainboard
  /// </summary>
  /// <param name="hash">Deck hash</param>
  /// <returns>List of color characters (W, U, B, R, G)</returns>
  [HttpGet("/api/decks/{hash}/colors")] // GET /api/decks/{hash}/colors
  [ProducesResponseType(typeof(DeckColorsDTO), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public async Task<ActionResult<DeckColorsDTO>> GetDeckColors(string hash)
  {
    var deck = await context.Decks.FindAsync(hash);
    if (deck == null)
    {
      return NotFound(new { error = $"Deck with hash {hash} not found" });
    }

    var colors = new HashSet<char>();

    foreach (var cardEntry in deck.Mainboard)
    {
      try
      {
        // Look up the card using the MTGOSDK CollectionManager
        var card = CollectionManager.GetCard(cardEntry.name);
        if (card != null && !string.IsNullOrEmpty(card.Colors))
        {
          foreach (char color in card.Colors)
          {
            if (VidereCardColors.IsCanonical(color))
            {
              colors.Add(color);
            }
          }
        }
      }
      catch (KeyNotFoundException)
      {
        // Card not found in collection, skip silently
        continue;
      }
      catch (Exception)
      {
        // Other errors, skip silently
        continue;
      }
    }

    var sortedColors = VidereCardColors.Normalize(colors).ToList();

    return Ok(new DeckColorsDTO
    {
      Hash = hash,
      Colors = sortedColors,
      ColorString = string.Join("", sortedColors)
    });
  }

  /// <summary>
  /// Get aggregated archetype statistics across all decks with win/loss data
  /// </summary>
  /// <param name="minDate">Minimum event start date</param>
  /// <param name="maxDate">Maximum event start date</param>
  /// <param name="format">Format filter</param>
  /// <returns>List of archetypes with their top card, winrate, and match count</returns>
  [HttpGet("/api/decks/archetypes/aggregated")] // GET /api/decks/archetypes/aggregated
  [ProducesResponseType(typeof(List<AggregatedArchetypeDTO>), StatusCodes.Status200OK)]
  public async Task<ActionResult<List<AggregatedArchetypeDTO>>> GetAggregatedArchetypes(
    [FromQuery] DateTime? minDate,
    [FromQuery] DateTime? maxDate,
    [FromQuery] string? format)
  {
    if (!clientProvider.TryGetCurrentUsername(out var currentUser))
    {
      return Ok(new List<AggregatedArchetypeDTO>());
    }

    // Query events with their decks and matches, filtered by date/format
    var eventsQuery = context.Events
      .Include(e => e.Deck)
      .Include(e => e.Matches)
      .Where(e => e.Deck != null)
      .AsQueryable();

    if (minDate.HasValue)
    {
      eventsQuery = eventsQuery.Where(e => e.StartTime >= minDate.Value);
    }
    if (maxDate.HasValue)
    {
      eventsQuery = eventsQuery.Where(e => e.StartTime <= maxDate.Value);
    }
    if (!string.IsNullOrEmpty(format))
    {
      eventsQuery = eventsQuery.Where(e => e.Format == format);
    }

    var events = await eventsQuery.ToListAsync();

    if (events.Count == 0)
    {
      return Ok(new List<AggregatedArchetypeDTO>());
    }

    // Track stats per archetype (using only stored data)
    // Key: archetype name -> (wins, losses, ties, colors, featuredCardVotes)
    var archetypeStats = new Dictionary<string, (int wins, int losses, int ties, HashSet<string> colors, Dictionary<string, int> featuredCardVotes)>();

    foreach (var evt in events)
    {
      var deck = evt.Deck!;

      // Skip decks without a stored archetype
      if (string.IsNullOrEmpty(deck.Archetype)) continue;

      var archetypeName = deck.Archetype;

      // Calculate wins/losses for this event
      int eventWins = 0, eventLosses = 0, eventTies = 0;
      foreach (var match in evt.Matches)
      {
        var playerResult = match.PlayerResults.FirstOrDefault(p => p.Player == currentUser);
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

      // Vote for this deck's featured card
      if (!string.IsNullOrEmpty(deck.FeaturedCard))
      {
        if (!featuredCardVotes.ContainsKey(deck.FeaturedCard))
          featuredCardVotes[deck.FeaturedCard] = 0;
        featuredCardVotes[deck.FeaturedCard]++;
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
        TopCard = topCardName,
        TopCardAvgScore = 0,
        TopCardAvgQuantity = 0
      });
    }

    // Sort by matches descending
    return Ok(result.OrderByDescending(a => a.Matches).ToList());
  }

}
