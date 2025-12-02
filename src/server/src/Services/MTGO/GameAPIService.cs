/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;
using MTGOSDK.Core;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting;
using static MTGOSDK.Core.Reflection.DLRWrapper;

using Tracker.Services.Base;
using Tracker.Services.MTGO.Events;


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
  /// Watches changes to the total player count of the given events and invokes
  /// the callback with the updated events whenever a change is detected.
  /// </summary>
  /// <param name="events">The events to watch.</param>
  /// <param name="callback">The callback to invoke with the updated events.</param>
  /// <param name="pollingInterval">The interval at which to poll for changes.</param>
  private static void WatchPlayerCountAsync(
    IEnumerable<Event> events,
    Action<IEnumerable<Event>> callback,
    TimeSpan pollingInterval = default)
  {
    IList<(Event, Func<int>, int)> eventCollection = [];
    foreach (var eventObj in events)
    {
      int getTotalPlayers() => Try<int>(() => eventObj.TotalPlayers);
      eventCollection.Add((eventObj, getTotalPlayers, getTotalPlayers()));
    }

    // Every 5 seconds, check each event's total players to see if it changed.
    var timer = new System.Timers.Timer(
      pollingInterval == default
        ? TimeSpan.FromSeconds(5).TotalMilliseconds
        : pollingInterval.TotalMilliseconds);

    bool _isDisposed = false;
    timer.Elapsed += (sender, e) =>
    {
      if (_isDisposed || PlayerCountUpdated == null) return;

      // Enumerate with the index of the event in the collection.
      IList<Event> updatedEvents = [];
      for (int i = 0; i < eventCollection.Count; i++)
      {
        var (eventObj, getTotalPlayers, previousTotal) = eventCollection[i];
        int currentTotal = getTotalPlayers();
        if (currentTotal != previousTotal)
        {
          // Update the collection with the new total.
          eventCollection[i] = (eventObj, getTotalPlayers, currentTotal);
          updatedEvents.Add(eventObj);
        }
      }
      if (updatedEvents.Count == 0) return;

      // Call the callback with the updated events.
      callback(updatedEvents);
    };
    timer.Start();

    RemoteClient.Disposed += delegate
    {
      if (_isDisposed) return;
      _isDisposed = true;
      timer.Dispose();
    };
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
    private readonly ConcurrentDictionary<int, Event> _activeEvents = new();
    private readonly ConcurrentDictionary<int, Match> _activeMatches = new();
    private readonly ConcurrentDictionary<int, Game> _activeGames = new();

    private readonly ConcurrentDictionary<int, MatchTracker> _matchTrackers = new();
    private readonly ConcurrentDictionary<int, GameTracker> _gameTrackers = new();

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

    private void CreateMatchTracker(Match match, int eventId)
    {
      _dbWriter.TryAddMatch(match, eventId, out var _);
      MatchTracker tracker = new(match, _dbWriter);
      _matchTrackers.TryAdd(match.Id, tracker);
    }

    private void CreateGameTracker(Game game, int? matchId = null)
    {
      matchId ??= game.Match.Id;

      bool isNewGame = _dbWriter.TryAddGame(game, matchId.Value, out var _);
      GameTracker tracker = new(game, _eventLog, _dbWriter, !isNewGame);
      _gameTrackers.TryAdd(game.Id, tracker);
    }

    private void OnEventJoined(Event e, object _)
    {
      if (_activeEvents.TryAdd(e.Id, e) &&
          _dbWriter.TryAddEvent(e, out var _))
      {
        Log.Debug("Event Joined {Id}", e.Id);
      }
      if (e is Match match && _activeMatches.TryAdd(match.Id, match))
      {
        Log.Debug("Match Joined {Id}", match.Id);
        CreateMatchTracker(match, e.Id);
      }
    }

    private void OnGameJoined(Event parentEvent, Game game)
    {
      // First add the parent event if needed
      if (_activeEvents.TryAdd(parentEvent.Id, parentEvent))
      {
        Log.Debug("Found event {Id}", parentEvent.Id);
        _dbWriter.TryAddEvent(parentEvent, out var _);

        // If it's a match, add it to its parent event
        if (parentEvent is Match match)
        {
          // Add the match connected to its parent event
          if (_activeMatches.TryAdd(match.Id, match))
          {
            Log.Debug("Match Joined {Id}", match.Id);
            CreateMatchTracker(match, parentEvent.Id);
          }

          // Now add the game connected to its parent match
          if (_activeGames.TryAdd(game.Id, game))
          {
            Log.Debug("Game Joined {Id}", game.Id);
            CreateGameTracker(game, match.Id);
          }
        }
      }
      // Parent event already exists, just add the game to the match
      else if (parentEvent is Match match &&
                _activeGames.TryAdd(game.Id, game))
      {
        // Add the match connected to its parent event
        if (_activeMatches.TryAdd(match.Id, match))
        {
          Log.Debug("Match Joined {Id}", match.Id);
          CreateMatchTracker(match, parentEvent.Id);
        }

        // Event already exists, just add the game to the match
        Log.Debug("Game Joined {Id} for existing event", game.Id);
        CreateGameTracker(game, match.Id);
      }
    }

    /// <summary>
    /// Initializes the game service by subscribing to MTGO events and setting
    /// up state trackers for matches and games.
    /// </summary>
    public void InitializeGameService()
    {
      Log.Debug("Initializing Game API service...");

      EventManager.EventJoined += OnEventJoined;
      EventManager.GameJoined += OnGameJoined;
      
      Log.Information("Game API service listener has started.");

      // Filter events list to get all joined events
      Log.Information("Finding all joined events and matches...");
      foreach (Event joinedEvent in EventManager.JoinedEvents)
      {
        if (_activeEvents.TryAdd(joinedEvent.Id, joinedEvent))
        {
          Log.Information("Found event {Id}", joinedEvent.Id);
          SyncThread.Enqueue(() =>
          {
            // Add the event to the database
            if (_dbWriter.TryAddEvent(joinedEvent, out var _))
            {
              Log.Debug("Added event {Id} to database", joinedEvent.Id);
            }
          });
        }
        // Add any active games from any joined matches
        if (joinedEvent is Match match)
        {
          if (_activeMatches.TryAdd(match.Id, match))
          {
            Log.Information("Found match {Id}", match.Id);
            SyncThread.Enqueue(() => CreateMatchTracker(match, joinedEvent.Id));
          }

          Game? game = match.CurrentGame;
          if (game == null) continue;

          if (_activeGames.TryAdd(game.Id, game))
          {
            Log.Information("Found game {Id}", game.Id);
            SyncThread.Enqueue(() => CreateGameTracker(game, match.Id));
          }
        }
      }
      Log.Trace("Game API service finished initializing");

      // Poll active events every 5 seconds to check for player count changes.
      WatchPlayerCountAsync(
        EventManager.FeaturedEvents,
        events => PlayerCountUpdated?.Invoke(this, events));
    }

    private void ResetService()
    {
      Log.Information("Resetting Game API service state...");
      
      // Unsubscribe from events
      EventManager.EventJoined -= OnEventJoined;
      EventManager.GameJoined -= OnGameJoined;

      foreach (var tracker in _matchTrackers.Values) tracker.Dispose();
      _matchTrackers.Clear();
      
      foreach (var tracker in _gameTrackers.Values) tracker.Dispose();
      _gameTrackers.Clear();
      
      _activeEvents.Clear();
      _activeMatches.Clear();
      _activeGames.Clear();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      try
      {
        Log.Information("Game API background service started");
        
        // Start log processing in a separate task
        var logTask = ProcessLogsAsync(stoppingToken);

        // Main service loop
        while (!stoppingToken.IsCancellationRequested)
        {
          try 
          {
            Log.Information("GameService: Waiting for client to be ready...");
            await _clientProvider.WaitForClientReadyAsync(stoppingToken);
            
            Log.Information("GameService: Client ready, initializing service...");
            InitializeGameService();
            
            Log.Information("GameService: Service initialized, waiting for disconnect...");
            await _clientProvider.WaitForClientDisconnectAsync(stoppingToken);
            
            Log.Information("GameService: Client disconnected, resetting service...");
            ResetService();
          }
          catch (OperationCanceledException) 
          { 
            break; 
          }
          catch (Exception ex) 
          { 
            Log.Error(ex, "Error in GameService loop");
            // Wait a bit before retrying to avoid tight loops on error
            await Task.Delay(1000, stoppingToken);
          }
        }
        
        await logTask;
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

    private async Task ProcessLogsAsync(CancellationToken stoppingToken)
    {
      // Process events in the eventlog blocking collection
      foreach (GameLogEntry entry in _eventLog.GetConsumingEnumerable(stoppingToken))
      {
        try
        {
          Log.Trace("[Game {Id}] {Timestamp:O}: {Type} {Data}",
              entry.GameId, entry.Timestamp, entry.Type, entry.Data);

          if (!await _dbWriter.WaitForGameModelAsync(entry.GameId))
          {
            throw new InvalidOperationException(
              $"Cannot add log entry for game {entry.GameId} " +
              $"as the game model was not created in time.");
          }
          await _dbWriter.TryAddGameLogAsync(entry, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
          Log.Error(ex, "Error adding log entry for game {GameId}: {Message}",
            entry.GameId, ex.Message);
          Log.Debug(ex.StackTrace);
        }
      }
    }
  }
}
