/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.Core.Reflection.Serialization;
using static MTGOSDK.Core.Reflection.DLRWrapper;
using MTGOSDK.Core.Remoting;

using Tracker.Controllers.Base;
using Tracker.Services.MTGO;


namespace Tracker.Controllers;

/// <summary>
/// Events and tournaments management API
/// </summary>
[ApiController]
[Route("api/[controller]/[action]")]
public class EventsController(ClientStateMonitor clientMonitor) : APIController
{
  //
  // Serialization Interfaces
  //

  public interface IEventStructure
  {
    string Name { get; }

    bool IsConstructed { get; }
    bool IsLimited { get; }
    bool IsDraft { get; }
    bool IsSealed { get; }
    bool IsSingleElimination { get; }
    bool IsSwiss { get; }
    bool HasPlayoffs { get; }
  }

  public interface ITournament
  {
    int Id { get; }
    string Description { get; }
    string Format { get; }
    int MinimumPlayers { get; }
    int TotalPlayers { get; }
    int TotalRounds { get; }

    IEventStructure EventStructure { get; }
    DateTime StartTime { get; }
    DateTime EndTime { get; }

    // ITournamentStateUpdate
    string State { get; }
    int CurrentRound { get; }
    DateTime RoundEndTime { get; }
    bool InPlayoffs { get; }
  }

  public interface ITournamentStateUpdate
  {
    int Id { get; }
    string State { get; }
    int CurrentRound { get; }
    DateTime RoundEndTime { get; }
    bool InPlayoffs { get; }
  }

  public interface ITournamentPlayerUpdate
  {
    int Id { get; }
    int TotalPlayers { get; }
    int TotalRounds { get; }
    DateTime EndTime { get; }
  }

  //
  // Events API Endpoints
  //

  /// <summary>
  /// Get list of available tournaments/events
  /// </summary>
  /// <param name="stream">Whether to stream all results as NDJSON (ignores pagination)</param>
  /// <param name="page">Page number (1-based, default: 1)</param>
  /// <param name="pageSize">Number of items per page (default: 50, max: 200)</param>
  /// <param name="includeCount">Whether to include total count in headers (requires enumeration, default: true)</param>
  /// <returns>List of tournaments</returns>
  [HttpGet] // GET /api/events/geteventslist
  [ProducesResponseType(
    typeof(IEnumerable<ITournament>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public IActionResult GetEventsList(
    [FromQuery] bool stream = false,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 50,
    [FromQuery] bool includeCount = true)
  {
    // Validate and clamp pagination parameters
    page = Math.Max(1, page);
    pageSize = Math.Clamp(pageSize, 1, 200);

    // Build the query with deferred enumeration
    var eventsQuery = EventManager.FeaturedEvents
      .Where(e => e.MinimumPlayers > 2)
      .OrderBy(e => e.StartTime);

    // Set basic pagination headers
    Response.Headers["X-Page"] = page.ToString();
    Response.Headers["X-Page-Size"] = pageSize.ToString();

    // Optionally include count metadata (requires full enumeration)
    if (includeCount && !stream)
    {
      var totalCount = eventsQuery.Count();
      var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

      Response.Headers["X-Total-Count"] = totalCount.ToString();
      Response.Headers["X-Total-Pages"] = totalPages.ToString();
      Response.Headers["X-Has-Next-Page"] = (page < totalPages).ToString();
      Response.Headers["X-Has-Previous-Page"] = (page > 1).ToString();
    }

    if (stream)
    {
      // Stream all events without pagination
      return NdjsonStream(eventsQuery.SerializeAs<ITournament>());
    }
    else
    {
      // Stream only the requested page
      var pagedEvents = eventsQuery
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .SerializeAs<ITournament>();

      return NdjsonStream(pagedEvents);
    }
  }

  /// <summary>
  /// Get tournament by ID
  /// </summary>
  /// <param name="id">Tournament ID</param>
  /// <returns>Tournament details</returns>
  [HttpGet("{id}")] // GET /api/events/getevent/{id}
  [ProducesResponseType(typeof(Event), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public Event GetEvent(int id)
  {
    return EventManager.GetEvent(id);
  }

  /// <summary>
  /// Get tournament state by ID
  /// </summary>
  /// <param name="id">Tournament ID</param>
  /// <returns>Tournament state information</returns>
  [HttpGet("{id}")] // GET /api/events/gettournamentstate/{id}
  [ProducesResponseType(typeof(ITournamentStateUpdate), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  public IActionResult GetTournamentState(int id)
  {
    try
    {
      Tournament tournament = EventManager.GetEvent(id);
      return Ok(tournament.SerializeAs<ITournamentStateUpdate>());
    }
    catch (KeyNotFoundException)
    {
      return NotFound(new { error = $"Tournament {id} not found" });
    }
    catch (Exception ex)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = ex.Message });
    }
  }

  /// <summary>
  /// Get entry fee information for a tournament
  /// </summary>
  /// <param name="id">Tournament ID</param>
  /// <returns>Entry fee description</returns>
  [HttpGet("{id}")] // GET /api/events/getentryfee/{id}
  [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public string GetEntryFee(int id)
  {
    return Retry<string>(() =>
    {
      Tournament tournament = EventManager.GetEvent(id);
      IList<EntryFeeSuite.EntryFee> entryFee = tournament.EntryFee;

      IList<string> entryFeeNames = [];
      foreach (var fee in entryFee)
      {
        switch (fee.Id)
        {
          case 1:     // Event Ticket
            entryFeeNames.Add($"{fee.Count} TIX");
            break;
          case 45195: // Play Point
            entryFeeNames.Add($"{fee.Count} PP");
            break;
          default:
            var name = fee.Item?.Name;
            if (name!.Contains("Magic Online Championship Series QP"))
            {
              entryFeeNames.Add($"{fee.Count} QP");
            }
            break;
        }
      }

      return string.Join(" / ", entryFeeNames);
    }, raise: true);
  }

  /// <summary>
  /// Get tournament standings
  /// </summary>
  /// <param name="id">Tournament ID</param>
  /// <param name="stream">Whether to stream results as NDJSON</param>
  /// <returns>Tournament standings</returns>
  [HttpGet("{id}")] // GET /api/events/getstandings/{id}
  [ProducesResponseType(
    typeof(IEnumerable<StandingRecord>), StatusCodes.Status200OK)]
  public IActionResult GetStandings(
    int id,
    [FromQuery] bool stream = false)
  {
    Tournament tournament = EventManager.GetEvent(id);
    IList<StandingRecord> standings = tournament.Standings;

    if (stream)
    {
      return NdjsonStream(standings);
    }
    else
    {
      return Ok(standings);
    }
  }

  //
  // Event Streaming Endpoints
  //

  /// <summary>
  /// Stream real-time tournament state updates
  /// </summary>
  /// <returns>Server-sent events stream of tournament updates as NDJSON</returns>
  [HttpGet] // GET /api/events/watchtournamentupdates
  [ProducesResponseType(
    typeof(IEnumerable<ITournamentStateUpdate>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  [Produces("application/x-ndjson")]
  public async Task<IActionResult> WatchTournamentUpdates()
  {
    // Check if client is ready before starting stream
    if (!clientMonitor.IsClientReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = "MTGO client is not ready" });
    }

    async Task tournamentStateCallback(Tournament tournament, TournamentState state)
    {
      // Serialize the event to the response stream
      await StreamResponse([tournament.SerializeAs<ITournamentStateUpdate>()]);
    }

    return await StreamNdjsonEvent<Tournament, TournamentState>(
      e => Tournament.StateChanged += e,
      e => Tournament.StateChanged -= e,
      tournamentStateCallback,
      clientMonitor.Token);
  }

  /// <summary>
  /// Stream real-time player count updates
  /// </summary>
  /// <returns>Server-sent events stream of player count changes as NDJSON</returns>
  [HttpGet] // GET /api/events/watchplayercount
  [ProducesResponseType(
    typeof(IEnumerable<ITournamentPlayerUpdate>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  [Produces("application/x-ndjson")]
  public async Task<IActionResult> WatchPlayerCount()
  {
    // Check if client is ready before starting stream
    if (!clientMonitor.IsClientReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = "MTGO client is not ready" });
    }

    async Task playerCountCallback(object? sender, IEnumerable<Event> events)
    {
      // Serialize the event to the response stream
      await StreamResponse([events.SerializeAs<ITournamentPlayerUpdate>()]);
    }

    return await StreamNdjsonEventHandler<IEnumerable<Event>>(
      e => GameAPIService.PlayerCountUpdated += e,
      e => GameAPIService.PlayerCountUpdated -= e,
      playerCountCallback,
      clientMonitor.Token);
  }

  //
  // Action Endpoints
  //

  /// <summary>
  /// Open a tournament in the MTGO client
  /// </summary>
  /// <param name="id">Tournament ID</param>
  [HttpPost("{id}")] // POST /api/events/openevent/{id}
  [ProducesResponseType(StatusCodes.Status204NoContent)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public IActionResult OpenEvent(int id)
  {
    EventManager.NavigateToEvent(id);
    RemoteClient.FocusWindow();
    return NoContent();
  }
}
