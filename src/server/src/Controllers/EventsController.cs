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
public class EventsController : APIController
{
  //
  // Serialization Interfaces
  //

  public interface ITournament
  {
    int Id { get; }
    string Description { get; }
    string Format { get; }
    int MinimumPlayers { get; }
    int TotalPlayers { get; }
    int TotalRounds { get; }
    DateTime StartTime { get; }
    DateTime EndTime { get; }
  }

  public interface ITournamentStateUpdate
  {
    int Id { get; }
    TournamentState State { get; }
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
  /// <param name="stream">Whether to stream results as NDJSON</param>
  /// <returns>List of tournaments</returns>
  [HttpGet] // GET /api/events/geteventslist
  [ProducesResponseType(
    typeof(IEnumerable<ITournament>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public IActionResult GetEventsList([FromQuery] bool stream = false)
  {
    IEnumerable<ITournament> events = EventManager.FeaturedEvents
      .Where(e => e.MinimumPlayers > 2)
      .OrderBy(e => e.StartTime)
      .SerializeAs<ITournament>();

    if (stream)
    {
      return NdjsonStream(events);
    }
    else
    {
      return Ok(events);
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
  [Produces("application/x-ndjson")]
  public async Task<IActionResult> WatchTournamentUpdates()
  {
    async Task tournamentStateCallback(Tournament tournament)
    {
      // Serialize the event to the response stream
      await StreamResponse([tournament.SerializeAs<ITournamentStateUpdate>()]);
    }

    return await StreamNdjsonEvent<Tournament>(
      e => Tournament.StateChanged += e,
      e => Tournament.StateChanged -= e,
      tournamentStateCallback);
  }

  /// <summary>
  /// Stream real-time player count updates
  /// </summary>
  /// <returns>Server-sent events stream of player count changes as NDJSON</returns>
  [HttpGet] // GET /api/events/watchplayercount
  [ProducesResponseType(
    typeof(IEnumerable<ITournamentPlayerUpdate>), StatusCodes.Status200OK)]
  [Produces("application/x-ndjson")]
  public async Task<IActionResult> WatchPlayerCount()
  {
    async Task playerCountCallback(object? sender, IEnumerable<Event> events)
    {
      // Serialize the event to the response stream
      await StreamResponse([events.SerializeAs<ITournamentPlayerUpdate>()]);
    }

    return await StreamNdjsonEventHandler<IEnumerable<Event>>(
      e => GameAPIService.PlayerCountUpdated += e,
      e => GameAPIService.PlayerCountUpdated -= e,
      playerCountCallback);
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
