/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
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
using MTGOSDK.Core.Logging;

using Tracker.Controllers.Base;
using Tracker.Controllers.Models.Collection;
using Tracker.Services.MTGO;
using Tracker.Services.Videre;


namespace Tracker.Controllers;

[ApiController]
[Route("api/collection")]
public sealed class CollectionMediaController(
  IClientAPIProvider clientProvider,
  VidereAPIClient videreAPIClient) : APIController
{
  private static readonly ConcurrentDictionary<string, string> s_imageCache = new();

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
  /// Get a single rendered card image as PNG by catalog ID
  /// </summary>
  [HttpGet("cards/{id:int}/image")] // GET /api/collection/cards/116306/image
  [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<ActionResult> GetCardImageById(int id, CancellationToken cancellationToken)
  {
    if (!clientProvider.IsReady)
    {
      var fallback = await TryRedirectToNonFoilImageAsync(id, cancellationToken);
      if (fallback is not null) return fallback;

      return StatusCode(StatusCodes.Status503ServiceUnavailable, new
      {
        error = "MTGO client not ready",
        hint = "Please wait for the client to fully initialize and log in"
      });
    }

    var cacheKey = $"id:{id}";
    if (s_imageCache.TryGetValue(cacheKey, out var cachedBase64))
      return File(Convert.FromBase64String(cachedBase64), "image/png", $"{id}.png");

    try
    {
      var card = CollectionManager.GetCard(id);
      if (card == null)
      {
        Log.Debug("GetCardImageById: catalog ID {Id} not found in collection", id);
        var fallback = await TryRedirectToNonFoilImageAsync(id, cancellationToken);
        if (fallback is not null) return fallback;
        return NotFound(new { error = $"Card with ID {id} not found" });
      }

      var base64 = CardRenderer.RenderCard(id);
      if (string.IsNullOrEmpty(base64))
      {
        Log.Warning("GetCardImageById: render returned empty result for catalog ID {Id}", id);
        var fallback = await TryRedirectToNonFoilImageAsync(id, cancellationToken);
        if (fallback is not null) return fallback;
        return NotFound(new { error = $"Failed to render card ID {id}" });
      }

      s_imageCache[cacheKey] = base64;
      Log.Debug("GetCardImageById: rendered catalog ID {Id} ({Name})", id, card.Name);
      return File(Convert.FromBase64String(base64), "image/png", $"{id}.png");
    }
    catch (KeyNotFoundException)
    {
      Log.Debug("GetCardImageById: catalog ID {Id} not found (KeyNotFoundException)", id);
      var fallback = await TryRedirectToNonFoilImageAsync(id, cancellationToken);
      if (fallback is not null) return fallback;
      return NotFound(new { error = $"Card with ID {id} not found in collection" });
    }
    catch (Exception ex)
    {
      Log.Error(ex, "GetCardImageById: unexpected error for catalog ID {Id}", id);
      var fallback = await TryRedirectToNonFoilImageAsync(id, cancellationToken);
      if (fallback is not null) return fallback;
      return StatusCode(StatusCodes.Status500InternalServerError, new { error = ex.Message });
    }
  }

  private async Task<ActionResult?> TryRedirectToNonFoilImageAsync(
    int catalogId,
    CancellationToken cancellationToken)
  {
    try
    {
      var nonFoilCatalogId = await videreAPIClient.FindNonFoilCatalogIdAsync(
        catalogId,
        cancellationToken);
      if (nonFoilCatalogId is not > 0) return null;

      Log.Debug(
        "GetCardImageById: using non-foil catalog ID {NonFoilId} for foil clone {FoilId}",
        nonFoilCatalogId,
        catalogId);
      return Redirect($"https://r2.videreproject.com/cards/{nonFoilCatalogId}-300px.png");
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (Exception ex)
    {
      Log.Debug(ex, "GetCardImageById: non-foil lookup failed for catalog ID {Id}", catalogId);
      return null;
    }
  }

  /// <summary>
  /// Get a card's art image by catalog ID (from MTGO client)
  /// </summary>
  /// <param name="id">Card catalog ID</param>
  /// <returns>Art image data</returns>
  [HttpGet("cards/{id:int}/art")] // GET /api/collection/cards/123/art
  [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public async Task<ActionResult> GetCardArtById(int id)
  {
    if (!clientProvider.IsReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable, new
      {
        error = "MTGO client not ready",
        hint = "Please wait for the client to fully initialize and log in"
      });
    }

    try
    {
      var card = CollectionManager.GetCard(id);
      if (card == null)
      {
        return NotFound(new { error = $"Card with ID {id} not found" });
      }

      var artPath = await CardRenderer.GetCardArtPath(card);
      if (artPath == null || !System.IO.File.Exists(artPath))
      {
        return NotFound(new { error = $"Art not available for card ID {id}" });
      }

      var artBytes = await System.IO.File.ReadAllBytesAsync(artPath);
      var contentType = artPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        ? "image/png"
        : "image/jpeg";

      return File(artBytes, contentType, $"{id}_art{Path.GetExtension(artPath)}");
    }
    catch (KeyNotFoundException)
    {
      return NotFound(new { error = $"Card with ID {id} not found in collection" });
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

    StartNDJSONResponse();

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
        await WriteCompactNDJSONLine(result, cancellationToken);
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

}
