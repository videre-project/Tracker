/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting;

using Tracker.Services.Base;
using Tracker.Services.MTGO.Events;
using MTGOSDK.Core;


namespace Tracker.Services.MTGO;

/// <summary>
/// A background service that listens for when new games are created in the client.
/// </summary>
public static class GameAPIService
{
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

  public sealed class GameService(IServiceProvider serviceProvider)
      : PooledBackgroundService, IHostedService, IDisposable
  {
    private readonly ConcurrentDictionary<int, Event> _activeEvents = new();
    private readonly ConcurrentDictionary<int, Match> _activeMatches = new();
    private readonly ConcurrentDictionary<int, Game> _activeGames = new();

    private readonly ConcurrentDictionary<int, MatchTracker> _matchTrackers = new();
    private readonly ConcurrentDictionary<int, GameTracker> _gameTrackers = new();

    private volatile bool _isDisposed;

    public override void Dispose()
    {
      if (_isDisposed) return;
      _isDisposed = true;

      foreach (var tracker in _matchTrackers.Values)
      {
        tracker.Dispose();
      }
      foreach (var tracker in _gameTrackers.Values)
      {
        tracker.Dispose();
      }

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

    public void InitializeGameService()
    {
      try
      {
        Log.Information("Game service started");

        EventManager.EventJoined += (Event e, object _) =>
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
        };
        EventManager.GameJoined += (Event parentEvent, Game game) =>
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
        };

        // Filter events list to get all joined events
        foreach (Event joinedEvent in EventManager.JoinedEvents)
        {
          if (_activeEvents.TryAdd(joinedEvent.Id, joinedEvent))
          {
            Log.Information("Found event {Id}", joinedEvent.Id);
            _dbWriter.TryAddEvent(joinedEvent, out var _);
          }
          // Add any active games from any joined matches
          if (joinedEvent is Match match)
          {
            if (_activeMatches.TryAdd(match.Id, match))
            {
              Log.Information("Found match {Id}", match.Id);
              CreateMatchTracker(match, joinedEvent.Id);
            }

            Game? game = match.CurrentGame;
            if (game == null) continue;

            if (_activeGames.TryAdd(game.Id, game))
            {
              Log.Information("Found game {Id}", game.Id);
              CreateGameTracker(game, match.Id);
            }
          }
        }
      }
      catch (Exception ex)
      {
        if (_isDisposed) return;

        Log.Critical("Failed to start game service", ex);
        Log.Debug(ex.Message + "\n" + ex.StackTrace);
      }
    }

    private void GameService_Disposed(object? sender, EventArgs e)
    {
      SyncThread.Enqueue(async () =>
      {
        Log.Information("Game service disposed, reinitializing...");
        this.Dispose();

        // Wait for a new MTGO process to start before reinitializing
        await ClientAPIService.WaitForRemoteClientAsync(serviceProvider);
        RemoteClient.Disposed += GameService_Disposed;

        InitializeGameService();
      });
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      try
      {
        // Wait for the game service events to be initialized
        RemoteClient.Disposed += GameService_Disposed;
        InitializeGameService();

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
          catch (Exception ex)
          {
            Log.Error(ex, "Error adding log entry for game {GameId}: {Message}",
              entry.GameId, ex.Message);
            Log.Debug(ex.StackTrace);
          }
        }
      }
      catch (OperationCanceledException)
      {
        // The service was stopped, exit gracefully
        Log.Information("Game service stopped");
      }
      catch (Exception ex)
      {
        Log.Critical("Error in game service: {Message}", ex.Message);
        Log.Debug(ex.StackTrace);
      }
      finally
      {
        // Clean up any remaining trackers
        foreach (var tracker in _matchTrackers.Values)
        {
          tracker.Dispose();
        }
        foreach (var tracker in _gameTrackers.Values)
        {
          tracker.Dispose();
        }
      }
    }
  }
}
