/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Play.Games.Processors;
using MTGOSDK.Core;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting;
using MTGOSDK.Core.Reflection;
using static MTGOSDK.Core.Reflection.DLRWrapper;

using Tracker.Services.Base;
using Tracker.Services.MTGO.Events;
using Tracker.Database.Extensions;

using MTGOSDK.API.Play.Leagues;
using MTGOSDK.API.Play.Tournaments;
using static MTGOSDK.API.Events;


namespace Tracker.Services.MTGO;

/// <summary>
/// A singleton service that manages the Tracker's monitoring of MTGO events,
/// matches, and games.
/// </summary>
/// <remarks>
/// This class serves as a global service for all instances of the GameService,
/// and provides access to global events and state related to MTGO events,
/// regardless of context.
/// <para>
/// It is designed to be used in conjunction with the ClientAPIService to
/// ensure a consistent connection to the MTGO client and recovery when the
/// MTGO process stops or restarts.
/// </para>
/// </remarks>
public static class GameAPIService
{
  /// <summary>
  /// An event that is raised when the player count of any event changes.
  /// </summary>
  public static EventHandler<IEnumerable<Event>>? PlayerCountUpdated;

  /// <summary>
  /// An event that is raised when any tournament standings change.
  /// </summary>
  public static EventHandler<(Tournament Tournament, IList<StandingRecord> Standings)>? StandingsUpdated;

  /// <summary>
  /// An event that is raised when any tournament round changes.
  /// </summary>
  public static EventHandler<(Tournament Tournament, TournamentRound Round)>? RoundUpdated;

  /// <summary>
  /// An event that is raised when any tournament state changes.
  /// </summary>
  public static EventHandler<(Tournament Tournament, TournamentState State)>? StateUpdated;


  /// <summary>
  /// An event that is raised when any game log is received.
  /// </summary>
  public static event EventHandler<GameLogEntry>? GameLogReceived;

  /// <summary>
  /// An event that is raised when a non-match event (e.g. tournament) is joined.
  /// The event argument is the event ID.
  /// </summary>
  public static EventHandler<int>? EventCreated;

  /// <summary>
  /// An event that is raised when a match is first tracked.
  /// The event argument is the match ID.
  /// </summary>
  public static EventHandler<int>? MatchCreated;

  /// <summary>
  /// An event that is raised when match results are persisted to the database.
  /// The event argument is the match ID.
  /// </summary>
  public static EventHandler<int>? MatchResultUpdated;

  private static volatile GameService? s_currentInstance;

  /// <summary>
  /// The IDs of all currently active (in-progress) events.
  /// </summary>
  public static IReadOnlyCollection<int> ActiveEventIds =>
    s_currentInstance?._activeEvents.Keys as IReadOnlyCollection<int>
      ?? (IReadOnlyCollection<int>)Array.Empty<int>();

  /// <summary>
  /// The IDs of all currently active (in-progress) matches.
  /// </summary>
  public static IReadOnlyCollection<int> ActiveMatchIds =>
    s_currentInstance?._activeMatches.Keys as IReadOnlyCollection<int>
      ?? (IReadOnlyCollection<int>)Array.Empty<int>();

  internal static void RemoveActiveEvent(int eventId) =>
    s_currentInstance?._activeEvents.TryRemove(eventId, out _);

  internal static void RemoveActiveMatch(int matchId) =>
    s_currentInstance?._activeMatches.TryRemove(matchId, out _);

  /// <summary>
  /// Forces any pending (unflushed) game data to be written to the database
  /// for the specified game. Called before DB queries to ensure data freshness.
  /// </summary>
  internal static void FlushPendingGameData(int gameId)
  {
    if (s_currentInstance?._gameTrackers.TryGetValue(gameId, out var tracker) == true)
    {
      tracker.ForceFlush();
    }
  }

  /// <summary>
  /// Cache of all discovered tournaments to preserve access after they finish and are removed by MTGO.
  /// </summary>
  public static readonly ConcurrentDictionary<int, Tournament> DiscoveredTournaments = new();

  public static EventHandler<Tournament>? LoadedTournamentDiscovered;

  private static long s_lastLoadedTournamentRefreshTicks;
  private static long s_clientScopeVersion;
  private static readonly object s_loadedTournamentRefreshLock = new();
  private static Task? s_loadedTournamentRefreshTask;

  private static readonly ConcurrentDictionary<int, (
    Tournament Tournament,
    Action<PropertyValueChangedEventArgs<int>> Handler)> s_playerCountSubscriptions = new();
  private static readonly object s_playerCountSubscriptionLock = new();
  private static Action<PlayerEventsCreatedEventArgs>? s_playerEventsCreatedHandler;
  private static Action<PlayerEventsRemovedEventArgs>? s_playerEventsRemovedHandler;
  private static Action<Tournament, IList<StandingRecord>>? s_standingsChangedHandler;

  private static Action<Tournament, TournamentRound>? s_roundChangedHandler;
  private static Action<Tournament, TournamentState>? s_stateChangedHandler;

  private static bool CacheDiscoveredTournament(Tournament tournament)
  {
    try
    {
      if (tournament.EntryFee == null || tournament.EntryFee.Count == 0)
      {
        return false;
      }
    }
    catch
    {
      return false;
    }

    int tournamentId = tournament.Id;
    bool added = DiscoveredTournaments.TryAdd(tournamentId, tournament);
    if (!added)
    {
      DiscoveredTournaments[tournamentId] = tournament;
      return false;
    }

    LoadedTournamentDiscovered?.Invoke(null, tournament);
    return true;
  }

  private static void ResetClientScopedTournamentCache(string reason)
  {
    int cachedCount = DiscoveredTournaments.Count;
    DiscoveredTournaments.Clear();
    ClearPlayerCountSubscriptions();
    Interlocked.Increment(ref s_clientScopeVersion);
    Interlocked.Exchange(ref s_lastLoadedTournamentRefreshTicks, 0);

    lock (s_loadedTournamentRefreshLock)
    {
      s_loadedTournamentRefreshTask = null;
    }

    if (cachedCount > 0)
    {
      Log.Information(
        "Cleared discovered tournament cache: reason={Reason} cached={CachedCount}",
        reason,
        cachedCount);
    }
  }

  private static void ClearPlayerCountSubscriptions()
  {
    lock (s_playerCountSubscriptionLock)
    {
      foreach (var subscription in s_playerCountSubscriptions.Values)
      {
        Try(() =>
          subscription.Tournament.OnTotalPlayersChanged -= subscription.Handler);
      }

      s_playerCountSubscriptions.Clear();
    }
  }

  private static void RemovePlayerCountSubscription(int tournamentId)
  {
    lock (s_playerCountSubscriptionLock)
    {
      if (s_playerCountSubscriptions.TryRemove(tournamentId, out var subscription))
      {
        Try(() =>
          subscription.Tournament.OnTotalPlayersChanged -= subscription.Handler);
      }
    }
  }

  public static Task StartLoadedTournamentRefresh(TimeSpan? minInterval = null)
  {
    var interval = minInterval ?? TimeSpan.FromSeconds(30);
    long nowTicks = DateTime.UtcNow.Ticks;
    long lastTicks = Interlocked.Read(ref s_lastLoadedTournamentRefreshTicks);
    if (lastTicks != 0 && new TimeSpan(nowTicks - lastTicks) < interval)
    {
      return Task.CompletedTask;
    }

    lock (s_loadedTournamentRefreshLock)
    {
      if (s_loadedTournamentRefreshTask is { IsCompleted: false })
      {
        return s_loadedTournamentRefreshTask;
      }

      long clientScopeVersion = Interlocked.Read(ref s_clientScopeVersion);
      s_loadedTournamentRefreshTask = Task.Factory.StartNew(
        () => RefreshLoadedTournaments(clientScopeVersion),
        CancellationToken.None,
        TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning,
        TaskScheduler.Default);
      return s_loadedTournamentRefreshTask;
    }
  }

  private static void RefreshLoadedTournaments(long clientScopeVersion)
  {
    long nowTicks = DateTime.UtcNow.Ticks;
    try
    {
      if (!RemoteClient.IsInitialized || RemoteClient.IsDisposed)
      {
        Log.Debug("Skipped loaded tournament refresh because MTGO client is not ready.");
        return;
      }

      int discoveredCount = 0;
      int newCount = 0;
      int errorCount = 0;
      foreach (var eventObj in EventManager.Events)
      {
        if (clientScopeVersion != Interlocked.Read(ref s_clientScopeVersion))
        {
          Log.Debug("Stopped loaded tournament refresh because MTGO client scope changed.");
          return;
        }

        try
        {
          if (eventObj is not Tournament tournament) continue;

          if (CacheDiscoveredTournament(tournament))
          {
            newCount++;
          }
          discoveredCount++;
        }
        catch (Exception ex)
        {
          errorCount++;
          Log.Debug(
            ex,
            "Skipped loaded MTGO event while refreshing tournament cache.");
        }
      }

      Interlocked.Exchange(ref s_lastLoadedTournamentRefreshTicks, nowTicks);
      Log.Debug(
        "Refreshed loaded tournaments from MTGO client: count={Count} new={NewCount} skipped={SkippedCount} cached={CachedCount}",
        discoveredCount,
        newCount,
        errorCount,
        DiscoveredTournaments.Count);
    }
    catch (Exception ex)
    {
      Log.Warning(
        ex,
        "Failed to refresh loaded tournaments from MTGO client.");
    }
  }


  /// <summary>
  /// Sets up event discovery to watch for player count changes across all
  /// discovered events without polling.
  /// </summary>
  private static void StartEventDiscovery(Action<IEnumerable<Event>> callback)
  {
    void subscribeToTournament(Tournament tournament)
    {
      int tournamentId = tournament.Id;
      CacheDiscoveredTournament(tournament);

      lock (s_playerCountSubscriptionLock)
      {
        if (s_playerCountSubscriptions.ContainsKey(tournamentId))
        {
          return;
        }

        Action<PropertyValueChangedEventArgs<int>> handler = args =>
        {
          callback(new[] { tournament });
        };

        tournament.OnTotalPlayersChanged += handler;
        s_playerCountSubscriptions.TryAdd(tournamentId, (tournament, handler));
      }

      // Tournament state/round/standings updates are bridged by the static
      // hooks below. Per-tournament standings hooks compute full tables before
      // tracker streams can filter by target tournament, so discovery only
      // attaches the cheap player-count watcher.
    }


    // 1. Subscribe to new events as they are created
    s_playerEventsCreatedHandler = args =>
    {
      foreach (var eventObj in args.Events)
      {
        if (eventObj is Tournament t) subscribeToTournament(t);
      }
    };
    EventManager.PlayerEventsCreated += s_playerEventsCreatedHandler;

    // 2. Clean up subscriptions when events are removed
    s_playerEventsRemovedHandler = args =>
    {
      foreach (var eventObj in args.Events)
      {
        if (eventObj is Event e)
        {
           int eventId = e.Id;
           RemovePlayerCountSubscription(eventId);
        }
      }
    };
    EventManager.PlayerEventsRemoved += s_playerEventsRemovedHandler;

    // 3. Initial crawl of featured events
    foreach (var tournament in EventManager.FeaturedEvents)
    {
      subscribeToTournament(tournament);
    }
  }

  /// <summary>
  /// Initializes the game service.
  /// </summary>
  /// <param name="builder">The host builder.</param>
  /// <returns>The host builder.</returns>
  public static IHostApplicationBuilder RegisterGameService(
    this IHostApplicationBuilder builder)
  {
    builder.Services.AddHostedService<GameService>();

    return builder;
  }

  /// <summary>
  /// A background service that listens for when new games are created in the client.
  /// </summary>
  public sealed class GameService(IServiceProvider serviceProvider)
      : PooledBackgroundService, IHostedService, IDisposable
  {
    internal readonly ConcurrentDictionary<int, Event> _activeEvents = new();
    internal readonly ConcurrentDictionary<int, Match> _activeMatches = new();
    private readonly ConcurrentDictionary<int, Game> _activeGames = new();

    private readonly ConcurrentDictionary<int, EventTracker> _eventTrackers = new();
    private readonly ConcurrentDictionary<int, MatchTracker> _matchTrackers = new();
    internal readonly ConcurrentDictionary<int, GameTracker> _gameTrackers = new();

    private volatile bool _isDisposed;

    private readonly IClientAPIProvider _clientProvider =
      serviceProvider.GetRequiredService<IClientAPIProvider>();

    /// <inheritdoc />
    public override void Dispose()
    {
      if (_isDisposed) return;
      _isDisposed = true;

      ResetService();
      base.Dispose();
    }

    private readonly BlockingCollection<GameLogEntry> _eventLog = new();
    private readonly EventDatabaseWriter _dbWriter = new(serviceProvider);

    private void CreateEventTracker(Event eventObj)
    {
      EventTracker tracker = new(eventObj, _dbWriter);
      _eventTrackers.TryAdd(eventObj.GetDatabaseId(), tracker);
    }

    private void CreateMatchTracker(Match match, int eventId)
    {
      _dbWriter.TryAddMatch(match, eventId, out var _);
      MatchCreated?.Invoke(null, match.Id);
      MatchTracker tracker = new(match, _dbWriter);
      _matchTrackers.TryAdd(match.Id, tracker);
    }

    private void CreateGameTracker(Game game, int? matchId = null)
    {
      matchId ??= game.Match.Id;

      _dbWriter.TryAddGame(game, matchId.Value, out var _);
      GameTracker tracker = new(game, _eventLog, _dbWriter);
      _gameTrackers.TryAdd(game.Id, tracker);
    }

    private void OnEventJoined(Event e, DateTime timestamp)
    {
      if (e is Match match)
      {
        if (_activeMatches.TryAdd(match.Id, match))
        {
          Log.Debug("Match Joined {Id}", match.Id);
          var parentEvent = EventManager.FindParentEvent(EventManager.JoinedEvents, match);
          Event targetEvent = parentEvent ?? match;
          int parentEventId = targetEvent.GetDatabaseId();

          if (_activeEvents.TryAdd(parentEventId, targetEvent) &&
              _dbWriter.TryAddEvent(targetEvent, out var _))
          {
            Log.Debug("Event Joined {Id} at {Timestamp}", parentEventId, timestamp);
            CreateEventTracker(targetEvent);
            if (targetEvent is not Match)
              EventCreated?.Invoke(null, parentEventId);
          }

          CreateMatchTracker(match, parentEventId);
        }
      }
      else
      {
        int eventId = e.GetDatabaseId();
        if (_activeEvents.TryAdd(eventId, e) &&
            _dbWriter.TryAddEvent(e, out var _))
        {
          Log.Debug("Event Joined {Id} at {Timestamp}", eventId, timestamp);
          CreateEventTracker(e);
          EventCreated?.Invoke(null, eventId);
        }
      }
    }

    private void OnGameJoined(Event parentEvent, Game game)
    {
      int parentEventId = parentEvent.GetDatabaseId();

      // First add the parent event if needed
      if (_activeEvents.TryAdd(parentEventId, parentEvent))
      {
        Log.Debug("Found event {Id}", parentEventId);
        if (_dbWriter.TryAddEvent(parentEvent, out var _))
        {
          CreateEventTracker(parentEvent);
        }
      }

      // Track the match and game
      Match match = game.Match;
      if (_activeMatches.TryAdd(match.Id, match))
      {
        Log.Debug("Match Joined {Id}", match.Id);
        CreateMatchTracker(match, parentEventId);
      }

      if (_activeGames.TryAdd(game.Id, game))
      {
        Log.Debug("Game Joined {Id}", game.Id);
        CreateGameTracker(game, match.Id);
      }
    }

    private void OnEventRemoved(Event e, DateTime timestamp)
    {
      int eventId = e.GetDatabaseId();
      if (_eventTrackers.TryRemove(eventId, out var tracker))
      {
        Log.Information("Event Removed {Id} at {Timestamp}", eventId, timestamp);
        tracker.Dispose(timestamp);
      }
      else if (_activeEvents.TryRemove(eventId, out _))
      {
        Log.Information("Event Removed {Id} at {Timestamp}", eventId, timestamp);
      }
    }

    /// <summary>
    /// Initializes the game service by subscribing to MTGO events and setting
    /// up state trackers for matches and games.
    /// </summary>
    public void InitializeGameService()
    {
      s_currentInstance = this;
      Log.Debug("Initializing Game API service...");
      ResetClientScopedTournamentCache("MTGO client ready");

      // Install all static hooks eagerly so that IPC type dumps and
      // Harmony patching happen at startup rather than blocking the
      // UI thread on first match/game join.
      EnsureCoreHooksInitialized();

      // Hook up the GameAPIService's EventManager event handlers
      SyncThread.Enqueue(() =>
      {
        EventManager.EventJoined += OnEventJoined;
        EventManager.EventRemoved += OnEventRemoved;
        EventManager.GameJoined += OnGameJoined;
        
        // Monitor all featured events for player count changes using event discovery.
        StartEventDiscovery(events => PlayerCountUpdated?.Invoke(null, events));

        // Subscribe to global tournament standings and round changes.
        s_standingsChangedHandler = (t, s) =>
        {
          CacheDiscoveredTournament(t);
          StandingsUpdated?.Invoke(null, (t, s));
        };
        s_roundChangedHandler = (t, r) =>
        {
          CacheDiscoveredTournament(t);
          RoundUpdated?.Invoke(null, (t, r));
        };
        s_stateChangedHandler = (t, state) =>
        {
          CacheDiscoveredTournament(t);
          StateUpdated?.Invoke(null, (t, state));

          if (state == TournamentState.Finished)
          {
            int tournamentId = t.Id;
            if (_eventTrackers.TryRemove(tournamentId, out var tracker))
            {
              Log.Information("Tournament {Id} finished", tournamentId);
              tracker.Dispose();
            }
            else if (_activeEvents.TryRemove(tournamentId, out _))
            {
              Log.Information("Tournament {Id} finished", tournamentId);
            }
          }
        };
        Tournament.StandingsChanged += s_standingsChangedHandler;
        Tournament.RoundChanged += s_roundChangedHandler;
        Tournament.StateChanged += s_stateChangedHandler;


        Log.Information("Game API service listener has started.");
      });

      // Filter events list to get all joined events and open leagues
      IEnumerable<Event> joinedEvents = EventManager.JoinedEvents.Cast<Event>();
      IEnumerable<Event> openLeagues = LeagueManager.OpenLeagues.Cast<Event>();
      IEnumerable<Event> initialEvents = joinedEvents.Concat(openLeagues);

      Log.Information("Finding all joined events, open leagues, and matches...");
      foreach (Event joinedEvent in initialEvents)
      {
        int eventId = joinedEvent.GetDatabaseId();
        if (_activeEvents.TryAdd(eventId, joinedEvent))
        {
          Log.Information("Found event {Id}", eventId);
          SyncThread.Enqueue(() =>
          {
            // Add the event to the database
            if (_dbWriter.TryAddEvent(joinedEvent, out var _))
            {
              Log.Debug("Added event {Id} to database", eventId);
              CreateEventTracker(joinedEvent);
            }
          });
        }
        // Add any active games from any joined matches
        if (joinedEvent is Match match)
        {
          if (_activeMatches.TryAdd(match.Id, match))
          {
            Log.Information("Found match {Id}", match.Id);
            var parentEvent = EventManager.FindParentEvent(EventManager.JoinedEvents, match);
            int parentEventId = parentEvent?.GetDatabaseId() ?? match.Id;
            SyncThread.Enqueue(() => CreateMatchTracker(match, parentEventId));
          }

          Game? game = match.CurrentGame;
          if (game == null) continue;

          if (_activeGames.TryAdd(game.Id, game))
          {
            Log.Information("Found game {Id}", game.Id);
            SyncThread.Enqueue(() => CreateGameTracker(game, match.Id));
          }
        }
          else if (joinedEvent is League league && league.ActiveMatch is Match activeMatch)
          {
            if (_activeMatches.TryAdd(activeMatch.Id, activeMatch))
            {
              Log.Information("Found active league match {Id}", activeMatch.Id);
              int parentEventId = league.CourseId;
              SyncThread.Enqueue(() => CreateMatchTracker(activeMatch, parentEventId));
            }

            Game? game = activeMatch.CurrentGame;
            if (game != null && _activeGames.TryAdd(game.Id, game))
            {
              Log.Information("Found game {Id}", game.Id);
              SyncThread.Enqueue(() => CreateGameTracker(game, activeMatch.Id));
            }
          }
          else if (joinedEvent is Tournament tournament && tournament.ActiveMatch is Match activeTourneyMatch)
          {
            if (_activeMatches.TryAdd(activeTourneyMatch.Id, activeTourneyMatch))
            {
              Log.Information("Found active tournament match {Id}", activeTourneyMatch.Id);
              int parentEventId = tournament.Id;
              SyncThread.Enqueue(() => CreateMatchTracker(activeTourneyMatch, parentEventId));
            }

            Game? game = activeTourneyMatch.CurrentGame;
            if (game != null && _activeGames.TryAdd(game.Id, game))
            {
              Log.Information("Found game {Id}", game.Id);
              SyncThread.Enqueue(() => CreateGameTracker(game, activeTourneyMatch.Id));
            }
          }
      }
      Log.Trace("Game API service finished initializing");
    }

    private static void EnsureCoreHooksInitialized()
    {
      GameProcessor.EnsureHookInitialized();
      ActionProcessor.EnsureHookInitialized();
      PromptProcessor.EnsureHookInitialized();
      LogMessageProcessor.EnsureHookInitialized();
      MatchTracker.EnsureHooksInitialized();
      GameTracker.EnsureHooksInitialized();
      Tournament.StandingsChanged.EnsureInitialize();
      Tournament.RoundChanged.EnsureInitialize();
      Tournament.StateChanged.EnsureInitialize();
    }

    private void ResetService()
    {
      Log.Information("Resetting Game API service state...");

      // Unsubscribe from events — safe even if the remote process is dead.
      Try(() => EventManager.EventJoined -= OnEventJoined);
      Try(() => EventManager.GameJoined -= OnGameJoined);

      if (s_playerEventsCreatedHandler != null)
      {
        Try(() => EventManager.PlayerEventsCreated -= s_playerEventsCreatedHandler);
        s_playerEventsCreatedHandler = null;
      }

      if (s_playerEventsRemovedHandler != null)
      {
        Try(() => EventManager.PlayerEventsRemoved -= s_playerEventsRemovedHandler);
        s_playerEventsRemovedHandler = null;
      }
      
      if (s_standingsChangedHandler != null)

      {
        Try(() => Tournament.StandingsChanged -= s_standingsChangedHandler);
        s_standingsChangedHandler = null;
      }

      if (s_roundChangedHandler != null)
      {
        Try(() => Tournament.RoundChanged -= s_roundChangedHandler);
        s_roundChangedHandler = null;
      }

      if (s_stateChangedHandler != null)
      {
        Try(() => Tournament.StateChanged -= s_stateChangedHandler);
        s_stateChangedHandler = null;
      }

      // Clear discovery subscriptions
      ClearPlayerCountSubscriptions();

      // Dispose trackers individually so one failure doesn't skip the rest.
      // Remote objects may be stale if the MTGO process exited.
      foreach (var tracker in _eventTrackers.Values) Try(tracker.Dispose);
      _eventTrackers.Clear();

      foreach (var tracker in _matchTrackers.Values) Try(tracker.Dispose);
      _matchTrackers.Clear();

      foreach (var tracker in _gameTrackers.Values) Try(tracker.Dispose);
      _gameTrackers.Clear();

      _activeEvents.Clear();
      _activeMatches.Clear();
      _activeGames.Clear();

      ResetClientScopedTournamentCache("MTGO client disconnect");
      s_currentInstance = null;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      try
      {
        Log.Information("Game API background service started");

        // Start processing logs in a separate thread
        _ = Task.Run(() => ProcessLogsAsync(stoppingToken));

        // Main service loop
        while (!stoppingToken.IsCancellationRequested)
        {
          try
          {
            Log.Information("Waiting for client to signal ready...");
            await _clientProvider.WaitForClientReadyAsync(stoppingToken);

            Log.Information("Client ready, initializing game service...");
            InitializeGameService();

            Log.Information("Game service initialized, waiting for disconnect...");
            await _clientProvider.WaitForClientDisconnectAsync(stoppingToken);

            Log.Information("Client disconnected, resetting game service...");
            ResetService();
          }
          catch (OperationCanceledException)
          {
            break;
          }
          catch (Exception ex)
          {
            Log.Error(ex, "Error in GameService loop");
            Log.Debug(ex.StackTrace);
            ResetService();
            await Task.Delay(1000, stoppingToken);
          }
        }
      }
      catch (OperationCanceledException)
      {
        Log.Information("Game service stopped");
      }
      catch (Exception ex)
      {
        Log.Critical("Error in game service: {Message}", ex.Message);
        Log.Debug(ex.StackTrace);
      }
      finally
      {
        ResetService();
      }
    }

    private Task ProcessLogsAsync(CancellationToken stoppingToken)
    {
      // GameTracker writes structured data to the DB directly.
      // This loop processes the notification stream for SSE clients.
      foreach (GameLogEntry entry in _eventLog.GetConsumingEnumerable(stoppingToken))
      {
        try
        {
          Log.Trace("[Game {Id}] {Timestamp:O}: {Type} {Data}",
              entry.GameId, entry.Timestamp, entry.Type, entry.Data);

          GameLogReceived?.Invoke(null, entry);
        }
        catch (OperationCanceledException)
        {
          throw;
        }
        catch (Exception ex)
        {
          Log.Error(ex, "Error processing log entry for game {GameId}: {Message}",
            entry.GameId, ex.Message);
        }
      }

      return Task.CompletedTask;
    }
  }
}
