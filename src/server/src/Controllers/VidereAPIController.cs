/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Tracker.Controllers.Base;
using Tracker.Controllers.Models.Decks;
using Tracker.Services.Videre;


namespace Tracker.Controllers;

[ApiController]
public sealed class VidereAPIController(VidereAPIClient videreAPIClient) : APIController
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
      return Ok(await videreAPIClient.SearchCardsAsync(q, limit, cancellationToken));
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
