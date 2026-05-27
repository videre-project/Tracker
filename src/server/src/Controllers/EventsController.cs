/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using MTGOSDK.API.Users;
using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Serialization;
using static MTGOSDK.Core.Reflection.DLRWrapper;
using MTGOSDK.Core.Remoting;

using Tracker.Controllers.Base;
using Tracker.Database;
using Tracker.Services.MTGO;
using TournamentMatch = MTGOSDK.API.Play.Match;


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
    TournamentState State { get; }
    int RoundNumber { get; }
    DateTime RoundEndTime { get; }
    bool InPlayoffs { get; }
  }

  public interface ITournamentStateCore
  {
    int Id { get; }
    TournamentState State { get; }
    int RoundNumber { get; }
    DateTime RoundEndTime { get; }
    bool InPlayoffs { get; }
  }

  public interface ITournamentStateUpdate : ITournamentStateCore
  {
    IEnumerable<string> ActivePlayerNames { get; }
    IEnumerable<string> PlayerNamesWithMatchesInProgress { get; }
  }

  public interface ITournamentPlayerUpdate
  {
    int Id { get; }
    int TotalPlayers { get; }
    int TotalRounds { get; }
    DateTime EndTime { get; }
  }

  public interface IStandingResult
  {
    int Rank { get; }
    int Points { get; }
    string Record { get; }
    string OpponentMatchWinPercentage { get; }
    string GameWinPercentage { get; }
    string OpponentGameWinPercentage { get; }
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
    var events = SerializeCachedEventsList().ToList();

    Response.Headers["X-Events"] = events.Count.ToString();
    Response.Headers["X-Discovered-Tournaments"] = GameAPIService.DiscoveredTournaments.Count.ToString();

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

    DisableBuffering();
    SetNdjsonContentType();
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
      HttpContext.RequestAborted,
      clientMonitor.Token);
    var streamToken = linkedCts.Token;

    var updateChannel = Channel.CreateUnbounded<(Tournament Tournament, string Source, object Detail)>(
      new UnboundedChannelOptions
      {
        SingleReader = true,
        SingleWriter = false,
      });
    var fingerprints = new Dictionary<int, string>();

    void queueTournamentUpdate(Tournament tournament, string source, object detail)
    {
      int tournamentId = SafeTournamentId(tournament);
      if (tournamentId <= 0) return;

      updateChannel.Writer.TryWrite((tournament, source, detail));
    }

      async Task writeTournamentUpdate(Tournament tournament, string source, object detail)
      {
        int tournamentId = SafeTournamentId(tournament);
        var serialized = SerializeTournamentForEventsList(
          tournament,
          tournamentId,
          TryGetRoundNumberOverride(source, detail));
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
      queueTournamentUpdate(tournament, "loaded", SafeTournamentId(tournament));
    }

    void onRoundUpdated(object? _, (Tournament Tournament, TournamentRound Round) args)
    {
      queueTournamentUpdate(args.Tournament, "round", SafeRoundNumber(args.Round));
    }

    void onStateUpdated(object? _, (Tournament Tournament, TournamentState State) args)
    {
      queueTournamentUpdate(args.Tournament, "state", args.State);
    }

    GameAPIService.LoadedTournamentDiscovered += onLoadedTournamentDiscovered;
    GameAPIService.RoundUpdated += onRoundUpdated;
    GameAPIService.StateUpdated += onStateUpdated;

    using var cancellationRegistration = streamToken.Register(() =>
      updateChannel.Writer.TryComplete());

    try
    {
      foreach (var item in GameAPIService.DiscoveredTournaments.ToArray())
      {
        await writeTournamentUpdate(item.Value, "initial", item.Key);
      }

      _ = GameAPIService.StartLoadedTournamentRefresh();

      await foreach (var update in updateChannel.Reader.ReadAllAsync(streamToken))
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
      updateChannel.Writer.TryComplete();
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
      Tournament tournament = GetTournamentOrDiscovered(id);
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
      Tournament tournament = GetTournamentOrDiscovered(id);
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
  //

  [HttpGet("{id}")] // GET /api/events/watchtournamentupdates/{id}
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
      Tournament tournament = GetTournamentOrDiscovered(id);

      DisableBuffering();
      SetNdjsonContentType();
      using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
        HttpContext.RequestAborted,
        clientMonitor.Token);
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

      // Phase 1: Stream initial state
      await handleTournamentUpdate(tournament, force: true);

      // If this only exists in the retained cache, write the current state once.
      if (!IsLiveFeaturedTournament(id))
      {
        return new EmptyResult();
      }

      // Phase 2: Stream round/state updates via GameAPIService. Standings deltas
      // are streamed by WatchStandings; standings callbacks can carry stale round
      // metadata and should not overwrite the tournament header state.
      var updateChannel = Channel.CreateUnbounded<(
        Tournament Tournament,
        string Source,
        object Detail)>(new UnboundedChannelOptions
      {
        SingleReader = true,
        SingleWriter = false,
      });

      void queueUpdate(Tournament updatedTournament, string source, object detail)
      {
        int tournamentId = SafeTournamentId(updatedTournament);
        if (tournamentId != id)
        {
          return;
        }

        updateChannel.Writer.TryWrite((updatedTournament, source, detail));
      }

      void onRoundUpdated(object? _, (Tournament Tournament, TournamentRound Round) args)
      {
        queueUpdate(args.Tournament, "round", SafeRoundNumber(args.Round));
      }

      void onStateUpdated(object? _, (Tournament Tournament, TournamentState State) args)
      {
        queueUpdate(args.Tournament, "state", args.State);
      }

      GameAPIService.RoundUpdated += onRoundUpdated;
      GameAPIService.StateUpdated += onStateUpdated;

      using var cancellationRegistration = streamToken.Register(() =>
        updateChannel.Writer.TryComplete());

      try
      {
        await foreach (var update in
          updateChannel.Reader.ReadAllAsync(streamToken))
        {
          await handleTournamentUpdate(
            update.Tournament,
            roundNumberOverride: TryGetRoundNumberOverride(update.Source, update.Detail),
            source: update.Source);
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
        updateChannel.Writer.TryComplete();
      }

      return new EmptyResult();
    }
    catch (KeyNotFoundException)
    {
      return NotFound(new { error = $"Tournament {id} not found. It may have ended or not yet loaded." });
    }
  }

  private static IEnumerable<object> SerializeCachedEventsList()
  {
    foreach (var item in GameAPIService.DiscoveredTournaments.ToArray())
    {
      var serialized = SerializeTournamentForEventsList(item.Value, item.Key);
      if (serialized != null) yield return serialized;
    }
  }

  private static object SerializeTournamentState(
    Tournament tournament,
    int? roundNumberOverride = null)
  {
    var dto = tournament.SerializeAs<ITournamentStateCore>();
    int roundNumber = ResolveRoundNumber(dto.RoundNumber, roundNumberOverride);

    return new
    {
      dto.Id,
      dto.State,
      RoundNumber = roundNumber,
      dto.RoundEndTime,
      dto.InPlayoffs,
      ActivePlayerNames = GetActivePlayerNames(tournament),
      PlayerNamesWithMatchesInProgress =
        GetPlayerNamesWithMatchesInProgress(tournament, roundNumber),
    };
  }

  private static string[] GetActivePlayerNames(Tournament tournament)
  {
    return tournament.ActivePlayers
      .Select(player => player.Name)
      .OrderBy(name => name, StringComparer.Ordinal)
      .ToArray();
  }

  private static string[] GetPlayerNamesWithMatchesInProgress(
    Tournament tournament,
    int roundNumber)
  {
    if (tournament.State != TournamentState.RoundInProgress)
    {
      return [];
    }

    TournamentRound? currentRound = GetCurrentRound(tournament, roundNumber);
    if (currentRound == null) return [];

    return Try(
      () => currentRound.PlayerNamesWithMatchesInProgress.ToArray(),
      () => ComputePlayerNamesWithMatchesInProgress(tournament, roundNumber));
  }

  private static string[] ComputePlayerNamesWithMatchesInProgress(
    Tournament tournament,
    int roundNumber)
  {
    if (tournament.State != TournamentState.RoundInProgress ||
        roundNumber <= 0)
    {
      return [];
    }

    TournamentRound? currentRound = GetCurrentRound(tournament, roundNumber);
    if (currentRound == null) return [];

    TournamentMatch[] currentRoundMatches = Try(
      () => ((IEnumerable<TournamentMatch>)((dynamic)currentRound)
          .MatchesInProgress).ToArray(),
      () => currentRound.Matches
        .Where(match => !match.IsComplete)
        .ToArray(),
      () => Array.Empty<TournamentMatch>());

    return currentRoundMatches
      .SelectMany(match => match.Players)
      .Select(player => player.Name)
      .Where(name => !string.IsNullOrEmpty(name))
      .Distinct(StringComparer.Ordinal)
      .OrderBy(name => name, StringComparer.Ordinal)
      .ToArray();
  }

  private static TournamentRound? GetCurrentRound(
    Tournament tournament,
    int? roundNumberOverride = null)
  {
    int roundNumber = ResolveRoundNumber(
      tournament.RoundNumber,
      roundNumberOverride);
    if (roundNumber <= 0) return null;

    TournamentRound? currentRound = TryGetCurrentRound(tournament);
    if (currentRound?.Number == roundNumber)
    {
      return currentRound;
    }

    TournamentRound[] rounds = Try(
      () => tournament.Rounds.ToArray(),
      () => Array.Empty<TournamentRound>());

    int roundIndex = roundNumber - 1;
    if (roundIndex >= 0 &&
        roundIndex < rounds.Length &&
        rounds[roundIndex].Number == roundNumber)
    {
      return rounds[roundIndex];
    }

    return rounds.FirstOrDefault(round => round.Number == roundNumber);
  }

  private static TournamentRound? TryGetCurrentRound(Tournament tournament)
  {
    try
    {
      return tournament.CurrentRound;
    }
    catch
    {
      return null;
    }
  }

  private static object? SerializeTournamentForEventsList(
    Tournament tournament,
    int tournamentId,
    int? roundNumberOverride = null)
  {
    try
    {
      var dto = tournament.SerializeAs<ITournament>();
      int roundNumber = ResolveRoundNumber(dto.RoundNumber, roundNumberOverride);

      return new
      {
        dto.Id,
        dto.Description,
        Format = NormalizeFormatName(dto.Format),
        dto.MinimumPlayers,
        dto.TotalPlayers,
        dto.TotalRounds,
        dto.EventStructure,
        dto.StartTime,
        dto.EndTime,
        dto.State,
        RoundNumber = roundNumber,
        dto.RoundEndTime,
        dto.InPlayoffs,
      };
    }
    catch (Exception ex)
    {
      if (GameAPIService.DiscoveredTournaments.TryGetValue(tournamentId, out var cachedTournament) &&
          ReferenceEquals(cachedTournament, tournament))
      {
        GameAPIService.DiscoveredTournaments.TryRemove(tournamentId, out _);
      }

      Log.Debug(
        ex,
        "Skipped loaded tournament while serializing events list tournament={TournamentId}",
        tournamentId);
      return null;
    }
  }

  private static string NormalizeFormatName(string format)
  {
    if (string.IsNullOrWhiteSpace(format))
    {
      return format;
    }

    string trimmed = format.TrimEnd('\0').Trim();
    string withoutFalseMultiplierSuffix = Regex.Replace(
      trimmed,
      @"(x[36])\s*[0o]$",
      "$1",
      RegexOptions.IgnoreCase);

    if (Regex.IsMatch(trimmed, @"^[^\d]*[A-Za-z]0$"))
    {
      return trimmed[..^1];
    }

    return withoutFalseMultiplierSuffix;
  }

  private static int? TryGetRoundNumberOverride(string source, object detail)
  {
    return source == "round" && detail is int roundNumber && roundNumber > 0
      ? roundNumber
      : null;
  }

  private static int ResolveRoundNumber(int serializedRoundNumber, int? roundNumberOverride)
  {
    return roundNumberOverride.HasValue
      ? Math.Max(serializedRoundNumber, roundNumberOverride.Value)
      : serializedRoundNumber;
  }

  private static void EnsureTournamentHooksInitialized()
  {
    Tournament.StandingsChanged.EnsureInitialize();
    Tournament.RoundChanged.EnsureInitialize();
    Tournament.StateChanged.EnsureInitialize();
  }

  private static async IAsyncEnumerable<object> StreamEventsList(
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    var channel = Channel.CreateUnbounded<Tournament>(new UnboundedChannelOptions
    {
      SingleReader = true,
      SingleWriter = false,
    });
    var seen = new HashSet<int>();

    void onLoadedTournamentDiscovered(object? _, Tournament tournament)
    {
      channel.Writer.TryWrite(tournament);
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
          channel.Writer.TryComplete();
        },
        CancellationToken.None,
        TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

      await foreach (var tournament in channel.Reader.ReadAllAsync(cancellationToken))
      {
        int tournamentId = SafeTournamentId(tournament);
        if (tournamentId <= 0 || !seen.Add(tournamentId)) continue;

        var serialized = SerializeTournamentForEventsList(tournament, tournamentId);
        if (serialized != null) yield return serialized;
      }
    }
    finally
    {
      GameAPIService.LoadedTournamentDiscovered -= onLoadedTournamentDiscovered;
    }
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

    async Task playerCountCallback(IEnumerable<Event> events)
    {
      // Use batch serialization for efficient cross-event property fetching
      await StreamResponse(events.SerializeAs<ITournamentPlayerUpdate>());
    }

    return await StreamNdjsonEventHandler<IEnumerable<Event>>(
      e => GameAPIService.PlayerCountUpdated += e,
      e => GameAPIService.PlayerCountUpdated -= e,
      (_, events) => playerCountCallback(events),
      clientMonitor.Token);
  }

  /// <summary>
  /// Stream tournament standings. Emits all current standings first,
  /// then streams deltas using a (Rank, Points) fingerprint per standing
  /// to detect and emit only changed records.
  /// </summary>
  /// <param name="id">Tournament ID</param>
  /// <returns>NDJSON stream: initial standings then live deltas</returns>
  [HttpGet("{id}")] // GET /api/events/watchstandings/{id}
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
      Tournament tournament = GetTournamentOrDiscovered(id);
      initial = tournament.ComputeStandings();

      DisableBuffering();
      SetNdjsonContentType();
      using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
        HttpContext.RequestAborted,
        clientMonitor.Token);
      var streamToken = linkedCts.Token;

      // Phase 1: Emit all current standings
      await StreamResponse(SerializeStandings(initial), streamToken);
      IList<StandingRecord>? lastWrittenFullStandings = initial;
      int? lastRoundHash = tournament.GetRoundHash(includeTournamentId: true);

      // If this only exists in the retained cache, write the current standings once.
      if (!IsLiveFeaturedTournament(id))
      {
        return new EmptyResult();
      }

      // Phase 2: Subscribe to changes and stream deltas via GameAPIService.
      // Round/state transitions can change the coherent computed table without
      // emitting a standings delta, so those stream a fresh computed snapshot.
      var updateChannel = Channel.CreateUnbounded<(
        Tournament Tournament,
        IList<StandingRecord>? Standings,
        string Source,
        object Detail)>(new UnboundedChannelOptions
      {
        SingleReader = true,
        SingleWriter = false,
      });

      void queueStandingsUpdate(
        Tournament updatedTournament,
        IList<StandingRecord>? standings,
        string source,
        object detail)
      {
        int tournamentId = SafeTournamentId(updatedTournament);
        if (tournamentId != id)
        {
          return;
        }

        updateChannel.Writer.TryWrite((updatedTournament, standings, source, detail));
      }

      void onStandingsUpdated(object? _, (Tournament Tournament, IList<StandingRecord> Standings) args)
      {
        queueStandingsUpdate(args.Tournament, args.Standings, "standings", args.Standings.Count);
      }

      void onRoundUpdated(object? _, (Tournament Tournament, TournamentRound Round) args)
      {
        queueStandingsUpdate(args.Tournament, null, "round", SafeRoundNumber(args.Round));
      }

      void onStateUpdated(object? _, (Tournament Tournament, TournamentState State) args)
      {
        queueStandingsUpdate(args.Tournament, null, "state", args.State);
      }

      GameAPIService.StandingsUpdated += onStandingsUpdated;
      GameAPIService.RoundUpdated += onRoundUpdated;
      GameAPIService.StateUpdated += onStateUpdated;

      using var cancellationRegistration = streamToken.Register(() =>
        updateChannel.Writer.TryComplete());

      try
      {
        await foreach (var update in updateChannel.Reader.ReadAllAsync(streamToken))
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
        updateChannel.Writer.TryComplete();
      }

      return new EmptyResult();
    }
    catch (KeyNotFoundException)
    {
      return NotFound(new { error = $"Tournament {id} not found. It may have ended or not yet loaded." });
    }
  }

  private static IEnumerable<object> SerializeStandings(IList<StandingRecord> standings)
  {
    var serialized = standings
      .SerializeAs<IStandingResult>()
      .ToList();

    return serialized
      .Zip(standings, (dto, s) => (object)new
      {
        dto.Rank,
        Player = s.Player.Name,
        dto.Points,
        dto.Record,
        dto.OpponentMatchWinPercentage,
        dto.GameWinPercentage,
        dto.OpponentGameWinPercentage,
      })
      .ToList();
  }

  private static Tournament GetTournamentOrDiscovered(int id)
  {
    try
    {
      return EventManager.GetEvent(id);
    }
    catch (KeyNotFoundException) when (GameAPIService.DiscoveredTournaments.TryGetValue(id, out var tournament))
    {
      return tournament;
    }
  }

  private static bool IsLiveFeaturedTournament(int id)
  {
    try
    {
      return EventManager.GetEvent(id) != null;
    }
    catch (KeyNotFoundException)
    {
      return false;
    }
  }

  private static int SafeTournamentId(Tournament tournament)
  {
    try
    {
      return tournament.Id;
    }
    catch (Exception)
    {
      return -1;
    }
  }

  private static int SafeRoundNumber(TournamentRound round)
  {
    try
    {
      return round.Number;
    }
    catch (Exception)
    {
      return -1;
    }
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
    return NoContent();
  }
}
