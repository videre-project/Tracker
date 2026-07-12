/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Serialization;

using Tracker.Controllers.Base;
using Tracker.Models.API.Events;
using Tracker.Services.MTGO;

using static Tracker.Services.MTGO.Events.TournamentSerialization;


namespace Tracker.Controllers;

[ApiController]
public sealed class EventStreamsController : APIController
{
  private readonly ClientStateMonitor clientMonitor;

  public EventStreamsController(ClientStateMonitor clientMonitor)
  {
    this.clientMonitor = clientMonitor;
  }

  //

  [HttpGet("/api/events/watchtournamentupdates/{id}")]
  [ProducesResponseType(
    typeof(IEnumerable<ITournamentStateUpdate>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  [Produces("application/x-ndjson")]
  public async Task<IActionResult> WatchTournamentUpdates(int id)
  {
    if (!clientMonitor.IsClientReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = "MTGO client is not ready" });
    }

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

      using var linkedCts = BeginNDJSONStream(clientMonitor.Token);
      var streamToken = linkedCts.Token;

      string? lastStateFingerprint = null;

      async Task handleTournamentUpdate(
        Tournament t,
        bool force = false,
        int? roundNumberOverride = null,
        string source = "update")
      {
        var stateUpdate = SerializeTournamentState(t, roundNumberOverride);
        string stateFingerprint = JsonSerializer.Serialize(stateUpdate);
        if (!force && stateFingerprint == lastStateFingerprint)
        {
          return;
        }

        lastStateFingerprint = stateFingerprint;
        await StreamResponse([stateUpdate], streamToken);
      }

      // Stream initial state
      await handleTournamentUpdate(tournament, force: true);

      // If this only exists in the retained cache, write the current state once.
      bool isLiveFeaturedTournament;
      try
      {
        isLiveFeaturedTournament = EventManager.GetEvent(id) != null;
      }
      catch (KeyNotFoundException)
      {
        isLiveFeaturedTournament = false;
      }

      if (!isLiveFeaturedTournament)
      {
        return new EmptyResult();
      }

      // Stream round/state updates via GameAPIService. Standings deltas
      // are streamed by WatchStandings; standings callbacks can carry stale round
      // metadata and should not overwrite the tournament header state.
      var updateQueue =
        new CoalescingUpdateQueue<int, object>(() => RecordStreamCoalesce());

      void queueUpdate(Tournament updatedTournament, string source, object detail)
      {
        int tournamentId = updatedTournament.Id;
        if (tournamentId != id)
        {
          return;
        }

        object stateUpdate;
        try
        {
          stateUpdate = SerializeTournamentState(
            updatedTournament,
            source == "round" && detail is int roundNumber && roundNumber > 0
              ? roundNumber
              : null);
        }
        catch (Exception ex)
        {
          Log.Debug(
            ex,
            "Skipped tournament state stream update tournament={TournamentId}",
            tournamentId);
          return;
        }

        updateQueue.Enqueue(tournamentId, stateUpdate);
      }

      void onRoundUpdated(object? _, (Tournament Tournament, TournamentRound Round) args)
      {
        queueUpdate(args.Tournament, "round", args.Round.Number);
      }

      void onStateUpdated(object? _, (Tournament Tournament, TournamentState State) args)
      {
        queueUpdate(args.Tournament, "state", args.State);
      }

      GameAPIService.RoundUpdated += onRoundUpdated;
      GameAPIService.StateUpdated += onStateUpdated;

      using var cancellationRegistration = streamToken.Register(updateQueue.Complete);

      try
      {
        await foreach (var update in
          updateQueue.ReadAllAsync(streamToken))
        {
          string stateFingerprint = JsonSerializer.Serialize(update);
          if (stateFingerprint == lastStateFingerprint)
          {
            continue;
          }

          lastStateFingerprint = stateFingerprint;
          await StreamResponse([update], streamToken);
        }
      }
      catch (OperationCanceledException) when (streamToken.IsCancellationRequested)
      {
        // Stream cancelled gracefully.
      }
      finally
      {
        GameAPIService.RoundUpdated -= onRoundUpdated;
        GameAPIService.StateUpdated -= onStateUpdated;
        updateQueue.Complete();
      }

      return new EmptyResult();
    }
    catch (KeyNotFoundException)
    {
      return NotFound(new { error = $"Tournament {id} not found. It may have ended or not yet loaded." });
    }
  }

  /// <summary>
  /// Stream real-time player count updates
  /// </summary>
  /// <returns>Server-sent events stream of player count changes as NDJSON</returns>
  [HttpGet("/api/events/watchplayercount")]
  [ProducesResponseType(
    typeof(IEnumerable<ITournamentPlayerUpdate>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  [Produces("application/x-ndjson")]
  public async Task<IActionResult> WatchPlayerCount()
  {
    if (!clientMonitor.IsClientReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = "MTGO client is not ready" });
    }

    using var linkedCts = BeginNDJSONStream(clientMonitor.Token);
    var streamToken = linkedCts.Token;

    var updateQueue =
      new CoalescingUpdateQueue<int, Event>(() => RecordStreamCoalesce());

    void onPlayerCountUpdated(object? _, IEnumerable<Event> events)
    {
      foreach (var eventObj in events)
      {
        int eventId = eventObj.Id;
        if (eventId <= 0) continue;

        updateQueue.Enqueue(eventId, eventObj);
      }
    }

    GameAPIService.PlayerCountUpdated += onPlayerCountUpdated;
    using var cancellationRegistration = streamToken.Register(updateQueue.Complete);

    try
    {
      await foreach (var eventObj in updateQueue.ReadAllAsync(streamToken))
      {
        await StreamResponse(
          new[] { eventObj }.SerializeAs<ITournamentPlayerUpdate>(),
          streamToken);
      }
    }
    catch (OperationCanceledException) when (streamToken.IsCancellationRequested)
    {
      // Stream cancelled gracefully.
    }
    finally
    {
      GameAPIService.PlayerCountUpdated -= onPlayerCountUpdated;
      updateQueue.Complete();
    }

    return new EmptyResult();
  }

/// <summary>
  /// Stream tournament standings. Emits all current standings first,
  /// then streams deltas using a (Rank, Points) fingerprint per standing
  /// to detect and emit only changed records.
  /// </summary>
  /// <param name="id">Tournament ID</param>
  /// <returns>NDJSON stream: initial standings then live deltas</returns>
  [HttpGet("/api/events/watchstandings/{id}")]
  [ProducesResponseType(
    typeof(IEnumerable<IStandingResult>), StatusCodes.Status200OK)]
  [ProducesResponseType(StatusCodes.Status404NotFound)]
  [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
  [Produces("application/x-ndjson")]
  public async Task<IActionResult> WatchStandings(int id)
  {
    if (!clientMonitor.IsClientReady)
    {
      return StatusCode(StatusCodes.Status503ServiceUnavailable,
        new { error = "MTGO client is not ready" });
    }

    IList<StandingRecord> initial;
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
      initial = tournament.ComputeStandings();

      using var linkedCts = BeginNDJSONStream(clientMonitor.Token);
      var streamToken = linkedCts.Token;

      // Emit all current standings
      await StreamResponse(SerializeStandings(initial), streamToken);
      IList<StandingRecord>? lastWrittenFullStandings = initial;
      int? lastRoundHash = tournament.GetRoundHash(includeTournamentId: true);

      // If this only exists in the retained cache, write the current standings once.
      bool isLiveFeaturedTournament;
      try
      {
        isLiveFeaturedTournament = EventManager.GetEvent(id) != null;
      }
      catch (KeyNotFoundException)
      {
        isLiveFeaturedTournament = false;
      }

      if (!isLiveFeaturedTournament)
      {
        return new EmptyResult();
      }

      // Subscribe to changes and stream deltas via GameAPIService.
      // Round/state transitions can change the coherent computed table without
      // emitting a standings delta, so those stream a fresh computed snapshot.
      var updateQueue = new CoalescingUpdateQueue<int, (
        Tournament Tournament,
        IList<StandingRecord>? Standings,
        string Source,
        object Detail)>(() => RecordStreamCoalesce());

      void queueStandingsUpdate(
        Tournament updatedTournament,
        IList<StandingRecord>? standings,
        string source,
        object detail)
      {
        int tournamentId = updatedTournament.Id;
        if (tournamentId != id)
        {
          return;
        }

        updateQueue.Enqueue(
          tournamentId,
          (updatedTournament, standings, source, detail));
      }

      void onStandingsUpdated(object? _, (Tournament Tournament, IList<StandingRecord> Standings) args)
      {
        queueStandingsUpdate(args.Tournament, args.Standings, "standings", args.Standings.Count);
      }

      void onRoundUpdated(object? _, (Tournament Tournament, TournamentRound Round) args)
      {
        queueStandingsUpdate(args.Tournament, null, "round", args.Round.Number);
      }

      void onStateUpdated(object? _, (Tournament Tournament, TournamentState State) args)
      {
        queueStandingsUpdate(args.Tournament, null, "state", args.State);
      }

      GameAPIService.StandingsUpdated += onStandingsUpdated;
      GameAPIService.RoundUpdated += onRoundUpdated;
      GameAPIService.StateUpdated += onStateUpdated;

      using var cancellationRegistration = streamToken.Register(updateQueue.Complete);

      try
      {
        await foreach (var update in updateQueue.ReadAllAsync(streamToken))
        {
          if (update.Source == "standings" &&
              update.Standings is not { Count: > 0 })
          {
            int? roundHash = update.Tournament.GetRoundHash(includeTournamentId: true);
            if (roundHash.HasValue && roundHash == lastRoundHash)
            {
              continue;
            }

            if (roundHash.HasValue)
            {
              lastRoundHash = roundHash;
            }
          }

          bool isFullSnapshot = update.Standings is not { Count: > 0 };
          IList<StandingRecord> standings =
            !isFullSnapshot
              ? update.Standings!
              : update.Tournament.ComputeStandings();

          if (isFullSnapshot && update.Source != "standings")
          {
            lastRoundHash =
              update.Tournament.GetRoundHash(includeTournamentId: true) ??
              lastRoundHash;
          }

          if (standings.Count == 0)
          {
            continue;
          }

          if (isFullSnapshot &&
              ReferenceEquals(standings, lastWrittenFullStandings))
          {
            continue;
          }

          try
          {
            await StreamResponse(SerializeStandings(standings), streamToken);
            if (isFullSnapshot)
            {
              lastWrittenFullStandings = standings;
            }
          }
          catch (OperationCanceledException) when (streamToken.IsCancellationRequested)
          {
          }
          catch (ObjectDisposedException) when (streamToken.IsCancellationRequested)
          {
          }
          catch (Exception)
          {
            throw;
          }
        }
      }
      catch (OperationCanceledException) when (streamToken.IsCancellationRequested)
      {
        // Stream cancelled gracefully.
      }
      finally
      {
        GameAPIService.StandingsUpdated -= onStandingsUpdated;
        GameAPIService.RoundUpdated -= onRoundUpdated;
        GameAPIService.StateUpdated -= onStateUpdated;
        updateQueue.Complete();
      }

      return new EmptyResult();
    }
    catch (KeyNotFoundException)
    {
      return NotFound(new { error = $"Tournament {id} not found. It may have ended or not yet loaded." });
    }
  }

}
