/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.Core.Logging;
using static MTGOSDK.Core.Reflection.DLRWrapper;

using Tracker.Controllers.Base;
using Tracker.Controllers.Models.Events;
using Tracker.Services.MTGO;


using static Tracker.Services.MTGO.Events.TournamentSerialization;
namespace Tracker.Controllers;

/// <summary>
/// Events and tournaments management API
/// </summary>
[ApiController]
[Route("api/[controller]/[action]")]
public class EventsController(ClientStateMonitor clientMonitor) : APIController
{
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
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  [ProducesResponseType(StatusCodes.Status500InternalServerError)]
  public IActionResult GetEventsList(
    [FromQuery] bool stream = false,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = 50,
    [FromQuery] bool includeCount = true)
  {
    if (!clientMonitor.IsClientReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = "MTGO client is not ready" });
    }

    // Validate and clamp pagination parameters
    page = Math.Max(1, page);
    pageSize = Math.Clamp(pageSize, 1, 200);



    // Set basic pagination headers
    Response.Headers["X-Page"] = page.ToString();
    Response.Headers["X-Page-Size"] = pageSize.ToString();

    if (stream)
    {
      Response.Headers["X-Discovered-Tournaments"] =
        GameAPIService.DiscoveredTournaments.Count.ToString();
      return NdjsonStream(StreamEventsList(HttpContext.RequestAborted));
    }

    GameAPIService.StartLoadedTournamentRefresh();
    var events = GameAPIService.DiscoveredTournaments
      .ToArray()
      .Select(item => SerializeTournamentForEventsList(item.Value, item.Key))
      .Where(item => item is not null)
      .Cast<object>()
      .ToList();

    Response.Headers["X-Events"] = events.Count.ToString();
    Response.Headers["X-Discovered-Tournaments"] =
      GameAPIService.DiscoveredTournaments.Count.ToString();

    // Optionally include count metadata (requires full enumeration)
    if (includeCount && !stream)
    {
      var totalCount = events.Count;
      var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

      Response.Headers["X-Total-Count"] = totalCount.ToString();
      Response.Headers["X-Total-Pages"] = totalPages.ToString();
      Response.Headers["X-Has-Next-Page"] = (page < totalPages).ToString();
      Response.Headers["X-Has-Previous-Page"] = (page > 1).ToString();
    }

    var serializedEvents = events
      .Skip((page - 1) * pageSize)
      .Take(pageSize);
    return NdjsonStream(serializedEvents);
  }

  /// <summary>
  /// Watch tournament list updates that affect sidebar event state.
  /// </summary>
  /// <returns>NDJSON stream of updated tournaments</returns>
  [HttpGet] // GET /api/events/watchtournamentlistupdates
  [ProducesResponseType(
    typeof(IEnumerable<ITournament>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  [Produces("application/x-ndjson")]
  public async Task<IActionResult> WatchTournamentListUpdates()
  {
    if (!clientMonitor.IsClientReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = "MTGO client is not ready" });
    }

    using var linkedCts = BeginNDJSONStream(clientMonitor.Token);
    var streamToken = linkedCts.Token;

    var updateQueue =
      new CoalescingUpdateQueue<int, (
        Tournament Tournament,
        string Source,
        object Detail)>(() => RecordStreamCoalesce());
    var fingerprints = new Dictionary<int, string>();

    void queueTournamentUpdate(Tournament tournament, string source, object detail)
    {
      int tournamentId = tournament.Id;
      if (tournamentId <= 0) return;

      updateQueue.Enqueue(tournamentId, (tournament, source, detail));
    }

    async Task writeTournamentUpdate(Tournament tournament, string source, object detail)
    {
      int tournamentId = tournament.Id;
      var serialized = SerializeTournamentForEventsList(
        tournament,
        tournamentId,
        source == "round" && detail is int roundNumber && roundNumber > 0
          ? roundNumber
          : null);
      if (serialized == null) return;

      string fingerprint = JsonSerializer.Serialize(serialized);
      if (fingerprints.TryGetValue(tournamentId, out var lastFingerprint) &&
          fingerprint == lastFingerprint)
      {
        return;
      }

      fingerprints[tournamentId] = fingerprint;
      await StreamResponse([serialized], streamToken);
    }

    void onLoadedTournamentDiscovered(object? _, Tournament tournament)
    {
      queueTournamentUpdate(tournament, "loaded", tournament.Id);
    }

    void onRoundUpdated(object? _, (Tournament Tournament, TournamentRound Round) args)
    {
      queueTournamentUpdate(args.Tournament, "round", args.Round.Number);
    }

    void onStateUpdated(object? _, (Tournament Tournament, TournamentState State) args)
    {
      queueTournamentUpdate(args.Tournament, "state", args.State);
    }

    GameAPIService.LoadedTournamentDiscovered += onLoadedTournamentDiscovered;
    GameAPIService.RoundUpdated += onRoundUpdated;
    GameAPIService.StateUpdated += onStateUpdated;

    using var cancellationRegistration = streamToken.Register(updateQueue.Complete);

    try
    {
      foreach (var item in GameAPIService.DiscoveredTournaments.ToArray())
      {
        await writeTournamentUpdate(item.Value, "initial", item.Key);
      }

      _ = GameAPIService.StartLoadedTournamentRefresh();

      await foreach (var update in updateQueue.ReadAllAsync(streamToken))
      {
        await writeTournamentUpdate(update.Tournament, update.Source, update.Detail);
      }
    }
    catch (OperationCanceledException) when (streamToken.IsCancellationRequested)
    {
      // Stream cancelled gracefully.
    }
    finally
    {
      GameAPIService.LoadedTournamentDiscovered -= onLoadedTournamentDiscovered;
      GameAPIService.RoundUpdated -= onRoundUpdated;
      GameAPIService.StateUpdated -= onStateUpdated;
      updateQueue.Complete();
    }

    return new EmptyResult();
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
      Tournament tournament;
      try
      {
        tournament = EventManager.GetEvent(id);
      }
      catch (KeyNotFoundException) when (
        GameAPIService.DiscoveredTournaments.TryGetValue(id, out var discoveredTournament))
      {
        tournament = discoveredTournament;
      }
      return Ok(SerializeTournamentState(tournament));
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
            if (name != null && name.Contains("QP"))
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
  /// Get prize information for a tournament
  /// </summary>
  /// <param name="id">Tournament ID</param>
  /// <returns>Prize breakdown by bracket</returns>
  [HttpGet("{id}")] // GET /api/events/getprizes/{id}
  [ProducesResponseType(typeof(Dictionary<string, string>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  public IActionResult GetPrizes(int id)
  {
    return Retry<IActionResult>(() =>
    {
      Tournament tournament = EventManager.GetEvent(id);
      var prizes = tournament.Prizes;

      var result = new Dictionary<string, string>();
      foreach (var bracket in prizes)
      {
        IList<string> prizeNames = [];
        foreach (var prize in bracket.Value)
        {
          switch (prize.Id)
          {
            case 1:     // Event Ticket
              prizeNames.Add($"{prize.Count}x TIX");
              break;
            case 45195: // Play Point
              prizeNames.Add($"{prize.Count}x Player Points (PP)");
              break;
            default:
              var item = prize.Item;
              if (item != null)
              {
                var name = item.Name;
                if (name.Contains("QP"))
                {
                  prizeNames.Add($"{prize.Count}x Qualifier Points (QP)");
                }
                else
                {
                  // Replace set name with set code when available
                  var setCode = item.Set?.Code;
                  var setName = item.Set?.Name;
                  if (setCode != null && setName != null && name.Contains(setName))
                    name = name.Replace(setName, setCode);

                  name = name
                    .Replace("Magic Online Championship Series", "MOCS")
                    .Replace("Treasure Chest Booster", "Treasure Chests")
                    .Replace("Leaderboard Point", "Points")
                    .Replace("Booster", "Boosters");

                  prizeNames.Add($"{prize.Count}x {name}");
                }
              }
              break;
          }
        }
        result[bracket.Key] = string.Join(" / ", prizeNames);
      }

      return Ok(result);
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
    typeof(IEnumerable<IStandingResult>), StatusCodes.Status200OK)]
  public IActionResult GetStandings(
    int id,
    [FromQuery] bool stream = false)
  {
    return Retry<IActionResult>(() =>
    {
      Tournament tournament;
      try
      {
        tournament = EventManager.GetEvent(id);
      }
      catch (KeyNotFoundException) when (
        GameAPIService.DiscoveredTournaments.TryGetValue(id, out var discoveredTournament))
      {
        tournament = discoveredTournament;
      }
      IList<StandingRecord> standings = tournament.ComputeStandings();

      // Use SerializeAs for batch property hydration, then combine
      // with Player name (User is [NonSerializable] and SerializeAs
      // can't convert User→string, so we map it explicitly).
      var result = SerializeStandings(standings);

      if (stream)
      {
        return NdjsonStream(result);
      }
      else
      {
        return Ok(result);
      }
    }, raise: true);
  }

  //
  // Event Streaming Endpoints
  private async IAsyncEnumerable<object> StreamEventsList(
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    var queue =
      new CoalescingUpdateQueue<int, Tournament>(() => RecordStreamCoalesce());
    var seen = new HashSet<int>();

    void onLoadedTournamentDiscovered(object? _, Tournament tournament)
    {
      int tournamentId = tournament.Id;
      if (tournamentId <= 0) return;

      queue.Enqueue(tournamentId, tournament);
    }

    GameAPIService.LoadedTournamentDiscovered += onLoadedTournamentDiscovered;
    Task refreshTask;
    try
    {
      foreach (var item in GameAPIService.DiscoveredTournaments.ToArray())
      {
        if (!seen.Add(item.Key)) continue;

        var serialized = SerializeTournamentForEventsList(item.Value, item.Key);
        if (serialized != null) yield return serialized;
      }

      refreshTask = GameAPIService.StartLoadedTournamentRefresh();
      _ = refreshTask.ContinueWith(
        task =>
        {
          if (task.Exception != null)
          {
            Log.Warning(
              task.Exception,
              "Loaded tournament refresh failed while streaming events list.");
          }
          queue.Complete();
        },
        CancellationToken.None,
        TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

      await foreach (var tournament in queue.ReadAllAsync(cancellationToken))
      {
        int tournamentId = tournament.Id;
        if (tournamentId <= 0 || !seen.Add(tournamentId)) continue;

        var serialized = SerializeTournamentForEventsList(tournament, tournamentId);
        if (serialized != null) yield return serialized;
      }
    }
    finally
    {
      GameAPIService.LoadedTournamentDiscovered -= onLoadedTournamentDiscovered;
      queue.Complete();
    }
  }

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
    return NoContent();
  }
}
