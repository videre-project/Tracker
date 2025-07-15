/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;

using Microsoft.AspNetCore.Mvc;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.Core.Reflection.Serialization;
using static MTGOSDK.Core.Reflection.DLRWrapper;
using MTGOSDK.Core.Remoting;

using Tracker.Services.MTGO;


namespace Tracker.Controllers;

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

    // dynamic EntryFee { get; }
  }

  public interface ITournamentPlayerUpdate
  {
    int Id { get; }
    int TotalPlayers { get; }
    int TotalRounds { get; }
    DateTime EndTime { get; }
  }

  //
  // API Endpoints
  //

  [HttpGet] // GET /api/events/geteventslist
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

  [HttpGet("{id}")] // GET /api/events/getevent/{id}
  public Event GetEvent(int id)
  {
    return EventManager.GetEvent(id);
  }

  [HttpGet("{id}")] // GET /api/events/getentryfee/{id}
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

  [HttpGet("{id}")] // GET /api/events/getstandings/{id}
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

  [HttpGet] // GET /api/events/watchplayercount
  public async Task<IActionResult> WatchPlayerCount()
  {
    DisableBuffering();
    SetNdjsonContentType();

    using var semaphore = new SemaphoreSlim(1, 1);
    async void playerCountCallback(object? sender, IEnumerable<Event> events)
    {
      await semaphore.WaitAsync(HttpContext.RequestAborted);
      try
      {
        // Serialize the events to the response stream
        await StreamResponse(events.SerializeAs<ITournamentPlayerUpdate>());
      }
      finally
      {
        semaphore.Release();
      }
    }

    // Subscribe to the player count updates
    GameAPIService.PlayerCountUpdated += playerCountCallback;

    // Keep the request alive until cancellation is requested
    var cts = new TaskCompletionSource<bool>();
    HttpContext.RequestAborted.Register(() =>
    {
      GameAPIService.PlayerCountUpdated -= playerCountCallback;
      cts.SetResult(true);
    });
    await cts.Task;

    return Ok();
  }

  [HttpGet("{id}")] // GET /api/events/openevent/{id}
  public IActionResult OpenEvent(int id)
  {
    EventManager.NavigateToEvent(id);
    RemoteClient.FocusWindow();
    return Ok();
  }
}
