/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using MTGOSDK.API.Collection;
using MTGOSDK.API.Graphics;

using Tracker.Controllers.Base;
using Tracker.Services.MTGO;


namespace Tracker.Controllers;

/// <summary>
/// Collection management API for rendering and streaming card images
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class CollectionController(IClientAPIProvider clientProvider) : APIController
{
  // The Power Nine cards from the BasicBot example
  private static readonly string[] s_powerNineCards =
  [
    "Black Lotus",
    "Mox Sapphire",
    "Mox Jet",
    "Mox Pearl",
    "Mox Ruby",
    "Mox Emerald",
    "Ancestral Recall",
    "Time Walk",
    "Timetwister"
  ];

  // Cache for rendered card images (base64-encoded PNG)
  // Key: card name, Value: base64 image data
  private static readonly ConcurrentDictionary<string, string> s_imageCache = new();

  /// <summary>
  /// Stream rendered card images as PNG data
  /// </summary>
  /// <returns>NDJSON stream of base64-encoded PNG images</returns>
  [HttpGet("cards/stream")] // GET /api/collection/cards/stream
  public async Task<IActionResult> StreamCardImages(CancellationToken cancellationToken)
  {
    // Check if client is ready before attempting SDK operations
    if (!clientProvider.IsReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new
      {
        error = "MTGO client not ready",
        hint = "Please wait for the client to fully initialize and log in"
      });
    }

    var cardObjects = s_powerNineCards
      .Select(name => CollectionManager.GetCard(name))
      .Where(card => card != null)
      .ToList();

    // Only set NDJSON content type AFTER validation passes
    DisableBuffering();
    SetNdjsonContentType();

    try
    {
      var serializerOptions = new System.Text.Json.JsonSerializerOptions
      {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
      };
      var newline = System.Text.Encoding.UTF8.GetBytes("\n");

      // Render all cards and stream each result
      var catalogIds = cardObjects.Select(card => (int)card!).ToArray();
      var base64Images = CardRenderer.RenderCards(catalogIds);

      for (int index = 0; index < base64Images.Length; index++)
      {
        if (cancellationToken.IsCancellationRequested) break;

        var base64 = base64Images[index];

        var dto = new CardImageDTO
        {
          Index = index,
          Name = "",
          ImageData = base64
        };

        // Write JSON and flush immediately
        await System.Text.Json.JsonSerializer.SerializeAsync(
          Response.Body, dto, serializerOptions, cancellationToken);
        await Response.Body.WriteAsync(newline, cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);

        index++;
      }
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"Error during card rendering: {ex.Message}");
      Console.Error.WriteLine(ex.StackTrace);
    }

    return new EmptyResult();
  }

  /// <summary>
  /// Get a single rendered card image as PNG
  /// </summary>
  /// <param name="name">Card name</param>
  /// <returns>PNG image data</returns>
  [HttpGet("cards/{name}/image")] // GET /api/collection/cards/{name}/image
  [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public ActionResult GetCardImage(string name)
  {
    // Check if client is ready before attempting SDK operations
    if (!clientProvider.IsReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new
      {
        error = "MTGO client not ready",
        hint = "Please wait for the client to fully initialize and log in"
      });
    }

    name = Uri.UnescapeDataString(name);

    // Check cache first
    if (s_imageCache.TryGetValue(name, out var cachedBase64))
    {
      return File(Convert.FromBase64String(cachedBase64), "image/png", $"{name}.png");
    }

    try
    {
      var card = CollectionManager.GetCard(name);
      if (card == null)
      {
        return NotFound(new { error = $"Card '{name}' not found" });
      }

      // Render the card (single card)
      var base64 = CardRenderer.RenderCards([(int)card]).FirstOrDefault();
      if (string.IsNullOrEmpty(base64))
      {
        return NotFound(new { error = $"Failed to render card '{name}'" });
      }

      // Cache and return
      s_imageCache[name] = base64;
      var pngData = Convert.FromBase64String(base64);

      return File(pngData, "image/png", $"{name}.png");
    }
    catch (KeyNotFoundException)
    {
      return NotFound(new { error = $"Card '{name}' not found in collection" });
    }
  }

  /// <summary>
  /// Get a card's art image as JPEG (from MTGO client)
  /// </summary>
  /// <param name="name">Card name</param>
  /// <returns>JPEG image data</returns>
  [HttpGet("cards/{name}/art")] // GET /api/collection/cards/{name}/art
  [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<ActionResult> GetCardArt(string name)
  {
    // Check if client is ready before attempting SDK operations
    if (!clientProvider.IsReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new
      {
        error = "MTGO client not ready",
        hint = "Please wait for the client to fully initialize and log in"
      });
    }

    name = Uri.UnescapeDataString(name);

    try
    {
      var card = CollectionManager.GetCard(name);
      if (card == null)
      {
        return NotFound(new { error = $"Card '{name}' not found" });
      }

      var artPath = await CardRenderer.GetCardArtPath(card);
      if (artPath == null || !System.IO.File.Exists(artPath))
      {
        return NotFound(new { error = $"Art not available for card '{name}'" });
      }

      // Read and return the art file
      var artBytes = await System.IO.File.ReadAllBytesAsync(artPath);
      var contentType = artPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        ? "image/png"
        : "image/jpeg";

      return File(artBytes, contentType, $"{name}_art{Path.GetExtension(artPath)}");
    }
    catch (KeyNotFoundException)
    {
      return NotFound(new { error = $"Card '{name}' not found in collection" });
    }
  }

  /// <summary>
  /// Get card art for multiple cards in parallel (streams as NDJSON)
  /// </summary>
  /// <param name="cards">Comma-separated list of card names</param>
  /// <param name="cancellationToken">Cancellation token</param>
  /// <returns>NDJSON stream of base64-encoded art</returns>
  [HttpGet("cards/art/batch")] // GET /api/collection/cards/art/batch?cards=name1,name2
  [ProducesResponseType(StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<IActionResult> GetCardArtBatch(
    [FromQuery] string cards,
    CancellationToken cancellationToken)
  {
    if (!clientProvider.IsReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new
      {
        error = "MTGO client not ready",
        hint = "Please wait for the client to fully initialize and log in"
      });
    }

    if (string.IsNullOrWhiteSpace(cards))
    {
      return BadRequest(new { error = "cards parameter required" });
    }

    var cardNames = cards.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (cardNames.Length == 0)
    {
      return BadRequest(new { error = "No valid card names provided" });
    }

    DisableBuffering();
    SetNdjsonContentType();

    var serializerOptions = new System.Text.Json.JsonSerializerOptions
    {
      PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };
    var newline = System.Text.Encoding.UTF8.GetBytes("\n");

    try
    {
      // Resolve all cards first
      var cardObjects = cardNames
        .Select(name => (Name: name, Card: CollectionManager.GetCard(name)))
        .ToList();

      // Load all art paths in parallel
      var artTasks = cardObjects.Select(async (item, index) =>
      {
        if (item.Card == null)
          return new CardArtDTO { Index = index, Name = item.Name, ImageData = null };

        var artPath = await CardRenderer.GetCardArtPath(item.Card);
        if (artPath == null || !System.IO.File.Exists(artPath))
          return new CardArtDTO { Index = index, Name = item.Name, ImageData = null };

        var artBytes = await System.IO.File.ReadAllBytesAsync(artPath, cancellationToken);
        return new CardArtDTO
        {
          Index = index,
          Name = item.Name,
          ImageData = Convert.ToBase64String(artBytes)
        };
      });

      // Process results as they complete
      foreach (var task in artTasks)
      {
        if (cancellationToken.IsCancellationRequested) break;

        var result = await task;
        await System.Text.Json.JsonSerializer.SerializeAsync(
          Response.Body, result, serializerOptions, cancellationToken);
        await Response.Body.WriteAsync(newline, cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
      }
    }
    catch (Exception ex)
    {
      Console.Error.WriteLine($"Error loading card art: {ex.Message}");
      Console.Error.WriteLine(ex.StackTrace);
    }

    return new EmptyResult();
  }

  /// <summary>
  /// Clears the image cache
  /// </summary>
  [HttpPost("cache/clear")] // POST /api/collection/cache/clear
  public IActionResult ClearCache()
  {
    var count = s_imageCache.Count;
    s_imageCache.Clear();
    return Ok(new { cleared = count });
  }

  /// <summary>
  /// Renders cards with caching - returns cached results immediately,
  /// only renders cards not in cache.
  /// </summary>
  private static IEnumerable<CardImageDTO> RenderCardsWithCache(IList<Card> cards)
  {
    var uncachedCards = new List<(int Index, Card Card)>();

    // First, yield all cached cards immediately
    for (int i = 0; i < cards.Count; i++)
    {
      var card = cards[i];
      if (s_imageCache.TryGetValue(card.Name, out var cachedBase64))
      {
        yield return new CardImageDTO
        {
          Index = i,
          Name = card.Name,
          ImageData = cachedBase64
        };
      }
      else
      {
        uncachedCards.Add((i, card));
      }
    }

    // If all cards were cached, we're done
    if (uncachedCards.Count == 0)
    {
      yield break;
    }

    // Render uncached cards
    var cardsToRender = uncachedCards.Select(x => x.Card).ToList();
    var catalogIds = cardsToRender.Select(c => (int)c).ToArray();
    var renderedImages = CardRenderer.RenderCards(catalogIds);

    for (int renderIndex = 0; renderIndex < renderedImages.Length; renderIndex++)
    {
      var (originalIndex, card) = uncachedCards[renderIndex];
      var base64 = renderedImages[renderIndex];

      // Cache the result
      s_imageCache[card.Name] = base64;

      yield return new CardImageDTO
      {
        Index = originalIndex,
        Name = card.Name,
        ImageData = base64
      };
    }
  }
}

public class CardImageDTO
{
  public required int Index { get; set; }
  public required string Name { get; set; }
  public required string ImageData { get; set; }
}

public class CardArtDTO
{
  public required int Index { get; set; }
  public required string Name { get; set; }
  public string? ImageData { get; set; }
}

