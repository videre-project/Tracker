/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
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
using Tracker.Services.MTGO;


namespace Tracker.Controllers;

/// <summary>
/// Decks management API
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class DecksController(
  EventContext context,
  IHttpClientFactory httpClientFactory,
  IServiceScopeFactory scopeFactory,
  ApplicationOptions appOptions,
  IClientAPIProvider clientProvider) : APIController
{
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

  private static readonly JsonSerializerOptions s_jsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true
  };

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
          Colors = d.Colors
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

  private record CachedDeck(Deck Deck, List<CardQuantityPair> Mainboard, List<CardQuantityPair> Sideboard);

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

    // Prioritize ID/Hash lookup (DB -> Cache -> SDK)
    if (!string.IsNullOrEmpty(id))
    {
      var cached = await GetOrLoadDeck(id);
      if (cached == null)
        return NotFound(new { error = $"Deck with hash {id} not found in database" });

      // Union of mainboard + sideboard (both grids share the same sheet image).
      // Deduped in DB order — must match the ordering used by GetSortableCards.
      catalogIds = cached.Mainboard
        .Concat(cached.Sideboard)
        .Select(c => c.Id)
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

    DisableBuffering();
    SetNdjsonContentType();

    var opts    = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    var newline = new byte[] { (byte)'\n' };

    async Task WriteLineAsync<T>(T obj)
    {
      await JsonSerializer.SerializeAsync(Response.Body, obj, opts, HttpContext.RequestAborted);
      await Response.Body.WriteAsync(newline, HttpContext.RequestAborted);
      await Response.Body.FlushAsync(HttpContext.RequestAborted);
    }

    // Line 1 — metadata: client pre-sizes the canvas and hides the overlay.
    await WriteLineAsync(new { type = "meta", columns, cardWidth, cardHeight, total });

    // Stream one row at a time. RenderCardsRowByRow does a single WPF render
    // pass for all cards, then yields each row as soon as its parallel
    // crop+encode finishes — no second render pass, no double overhead.
    int startIndex = 0;
    foreach (var rowCards in CardRenderer.RenderCardsRowByRow(catalogIds, columns, cardHeight))
    {
      if (HttpContext.RequestAborted.IsCancellationRequested) break;
      await WriteLineAsync(new { type = "cards", startIndex, cards = rowCards });
      startIndex += rowCards.Length;
    }

    return new EmptyResult();
  }

  /// <summary>
  /// Get sortable card metadata for a deck.
  /// Uses fast parallel serialization that matches MTGO's NonPileByName order.
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
    CachedDeck? cachedDeck = null;
    Deck? deck = null;

    // Prioritize ID/Hash lookup (DB -> Cache -> SDK)
    if (!string.IsNullOrEmpty(id))
    {
      cachedDeck = await GetOrLoadDeck(id);
      if (cachedDeck == null)
      {
        return NotFound(new { error = $"Deck with hash {id} not found in database" });
      }
      deck = cachedDeck.Deck;
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

    // Create lookup for card metadata by CatalogId
    // Group by ID because identical cards (same printing) have same ID
    var metadataMap = cardDataList
      .GroupBy(c => c.Id)
      .ToDictionary(g => g.Key, g => g.First());

    var resultList = new List<SortableCardEntry>();

    if (cachedDeck != null)
    {
      var sortedSDKCards = cardDataList
        .OrderBy(c => GetSortableName(c.Name))
        .ThenBy(c => c.Id)
        .ToList();

      // Build a lookup: catalogId → sheet slot index, matching GetDeckSheet's
      // catalogIds ordering (mainboard ∪ sideboard deduped in DB order).
      var sheetIndexById = cachedDeck.Mainboard
        .Concat(cachedDeck.Sideboard)
        .Select(c => c.Id)
        .Distinct()
        .Select((id, i) => (id, i))
        .ToDictionary(t => t.id, t => t.i);

      for (int idx = 0; idx < sortedSDKCards.Count; idx++)
      {
        var card = sortedSDKCards[idx];
        int totalQty = card.Quantity;
        int mainQty = 0;
        int sideQty = 0;

        // Count in Main/Sideboards
        mainQty = cachedDeck.Mainboard.Where(x => x.Id == card.Id).Sum(x => x.Quantity);
        sideQty = cachedDeck.Sideboard.Where(x => x.Id == card.Id).Sum(x => x.Quantity);
        
        // Safety: If mismatch (e.g. strict version mismatch in SDK vs DB), dump undefined into Main?
        if (mainQty + sideQty == 0) mainQty = totalQty; 

        if (mainQty > 0)
        {
          resultList.Add(new SortableCardEntry
          {
            Index = -1,
            OriginalIndex = sheetIndexById.TryGetValue(card.Id, out var msi) ? msi : idx,
            CatalogId = card.Id,
            Name = card.Name,
            Quantity = mainQty,
            Zone = "Mainboard",
            Cmc = card.ConvertedManaCost,
            Colors = card.Colors?.ToCharArray().Select(ch => ch.ToString()).ToList() ?? new List<string>(),
            Types = card.Types?.ToList() ?? new List<string>(),
            Rarity = card.Rarity
          });
        }
        if (sideQty > 0)
        {
          resultList.Add(new SortableCardEntry
          {
            Index = -1, // Assigned later
            OriginalIndex = sheetIndexById.TryGetValue(card.Id, out var ssi) ? ssi : idx,
            CatalogId = card.Id,
            Name = card.Name,
            Quantity = sideQty,
            Zone = "Sideboard",
            Cmc = card.ConvertedManaCost,
            Colors = card.Colors?.ToCharArray().Select(ch => ch.ToString()).ToList() ?? new List<string>(),
            Types = card.Types?.ToList() ?? new List<string>(),
            Rarity = card.Rarity
          });
        }
      }
    }
    else
    {
       // Old logic for non-cached
       var sorted = cardDataList.OrderBy(c => GetSortableName(c.Name)).ThenBy(c => c.Id).ToList();
       for (int i = 0; i < sorted.Count; i++)
       {
         var c = sorted[i];
         resultList.Add(new SortableCardEntry
         {
            Index = -1,
            OriginalIndex = i,
            CatalogId = c.Id,
            Name = c.Name,
            Quantity = c.Quantity,
            Zone = "Mainboard", // Default
            Cmc = c.ConvertedManaCost,
            Colors = c.Colors?.ToCharArray().Select(ch => ch.ToString()).ToList() ?? new List<string>(),
            Types = c.Types?.ToList() ?? new List<string>(),
            Rarity = c.Rarity
         });
       }
    }

    // Assign sequential indices
    for(int i=0; i<resultList.Count; i++) resultList[i].Index = i;

    return Ok(resultList);
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
      var mainboard = deckModel.Mainboard.Select(c => new CardQuantityPair(c.catalogId, c.quantity)).ToList();
      var sideboard = deckModel.Sideboard.Select(c => new CardQuantityPair(c.catalogId, c.quantity)).ToList();

      // Deck constructor makes synchronous IPC calls (CreateInstance,
      // CreateArray, ReconcileCards). Run on the request thread — not
      // Task.Run — to avoid ThreadPool starvation deadlocks.
      var deck = new Deck(mainboard, sideboard, deckModel.Name);
      var result = new CachedDeck(deck, mainboard, sideboard);
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
  [HttpGet("{hash}/archetype")] // GET /api/decks/{hash}/archetype
  [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status502BadGateway)]
  public async Task<ActionResult<object>> GetArchetype(string hash)
  {
    var deck = await context.Decks.FindAsync(hash);
    if (deck == null)
    {
      return NotFound(new { error = $"Deck with hash {hash} not found" });
    }

    // Build request body with card names and quantities
    var cards = deck.Mainboard
      .Select(c => new { name = c.name, quantity = c.quantity })
      .ToList();

    if (cards.Count < 2)
    {
      return BadRequest(new { error = "Deck must have at least 2 cards for archetype detection" });
    }

    try
    {
      var client = httpClientFactory.CreateClient();
      var requestBody = JsonSerializer.Serialize(cards, s_jsonOptions);
      var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

      // Call NBAC API with explain=1 for testing
      var response = await client.PostAsync(
        $"{appOptions.NbacApiUrl}?format={Uri.EscapeDataString(deck.Format.ToLower())}&explain=1",
        content
      );

      var responseContent = await response.Content.ReadAsStringAsync();

      if (!response.IsSuccessStatusCode)
      {
        return StatusCode(StatusCodes.Status502BadGateway, new
        {
          error = "NBAC API request failed",
          statusCode = (int)response.StatusCode,
          response = responseContent
        });
      }

      // Parse and return raw JSON response
      var nbacResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
      return Ok(nbacResponse);
    }
    catch (HttpRequestException ex)
    {
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
  [HttpGet("{hash}/colors")] // GET /api/decks/{hash}/colors
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
            if ("WUBRG".Contains(color))
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

    // Sort colors in WUBRG order
    var sortedColors = colors
      .OrderBy(c => "WUBRG".IndexOf(c))
      .Select(c => c.ToString())
      .ToList();

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
  [HttpGet("archetypes/aggregated")] // GET /api/decks/archetypes/aggregated
  [ProducesResponseType(typeof(List<AggregatedArchetypeDTO>), StatusCodes.Status200OK)]
  public async Task<ActionResult<List<AggregatedArchetypeDTO>>> GetAggregatedArchetypes(
    [FromQuery] DateTime? minDate,
    [FromQuery] DateTime? maxDate,
    [FromQuery] string? format)
  {
    var currentUser = clientProvider.Client?.CurrentUser?.Name;
    if (string.IsNullOrEmpty(currentUser))
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

      // Sort colors in WUBRG order
      var sortedColors = colors
        .OrderBy(c => "WUBRG".IndexOf(c))
        .ToList();

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

public class DeckDTO
{
  public required string Hash { get; set; }
  public required int Id { get; set; }
  public required string Name { get; set; }
  public required string Format { get; set; }
  public required DateTime Timestamp { get; set; }
  public required int MainboardCount { get; set; }
  public required int SideboardCount { get; set; }
  public string? Archetype { get; set; }
  public List<string> Colors { get; set; } = new();
}

public class DeckDetailDTO
{
  public required string Hash { get; set; }
  public required int Id { get; set; }
  public required string Name { get; set; }
  public required string Format { get; set; }
  public required DateTime Timestamp { get; set; }
  public required List<CardEntry> Mainboard { get; set; }
  public required List<CardEntry> Sideboard { get; set; }
}

public class AggregatedArchetypeDTO
{
  public required string Archetype { get; set; }
  public required List<string> Colors { get; set; }
  public required int Matches { get; set; }
  public required int Wins { get; set; }
  public required int Losses { get; set; }
  public required double Winrate { get; set; }
  public required string TopCard { get; set; }
  public required double TopCardAvgScore { get; set; }
  public required double TopCardAvgQuantity { get; set; }
}

public class DeckColorsDTO
{
  public required string Hash { get; set; }
  public required List<string> Colors { get; set; }
  public required string ColorString { get; set; }
}

/// <summary>
/// Card entry with sortable metadata (CMC, Colors, Types)
/// </summary>
public class SortableCardEntry
{
  public int Index { get; set; }
  public int OriginalIndex { get; set; }
  public required int CatalogId { get; set; }
  public required string Name { get; set; }
  public int Quantity { get; set; }
  public int Cmc { get; set; }
  public List<string> Colors { get; set; } = new();
  public List<string> Types { get; set; } = new();
  public required string Rarity { get; set; }
  public string Zone { get; set; } = "Mainboard";
}

public class DeckIdentifierDTO
{
  public required string Hash { get; set; }
  public required int Id { get; set; }
  public required string Name { get; set; }
  public required string Format { get; set; }
}
