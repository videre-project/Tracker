/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using MTGOTracker.Controllers.Base;
using MTGOTracker.Controllers.Models.Decks;
using MTGOTracker.Services.Videre;
using MTGOTracker.Services.MTGO.Collection;


namespace MTGOTracker.Controllers;

[ApiController]
public sealed class VidereAPIController(
  VidereAPIClient videreAPIClient,
  CollectionDeckService deckService) : APIController
{
  [HttpGet("/api/decks/search-cards")]
  [ProducesResponseType(typeof(List<CardSearchResultDTO>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status502BadGateway)]
  public async Task<ActionResult<List<CardSearchResultDTO>>> SearchCards(
    [FromQuery] string? q = null,
    [FromQuery] int limit = 24,
    CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(q)) return Ok(new List<CardSearchResultDTO>());

    try
    {
      List<CardSearchResultDTO> cards = await videreAPIClient.SearchCardsAsync(q, limit, cancellationToken);
      deckService.EnrichSearchCardTypes(cards);
      return Ok(cards);
    }
    catch (VidereAPIException ex)
    {
      return StatusCode(StatusCodes.Status502BadGateway, new
      {
        error = ex.Message,
        statusCode = ex.StatusCode,
        response = ex.Response,
        message = ex.InnerException?.Message
      });
    }
  }
}
