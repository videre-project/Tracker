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

using Tracker.Services.MTGO.Events;


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
    builder.Services.AddSingleton<GameService>();
    builder.Services.AddHostedService(p => p.GetRequiredService<GameService>());

    return builder;
  }

  public class GameService(IServiceProvider serviceProvider)
      : BackgroundService, IHostedService
  {
    private readonly ConcurrentDictionary<int, Event> _activeEvents = new();
    private readonly ConcurrentDictionary<int, Game> _activeGames = new();
    private readonly ConcurrentDictionary<int, GameTracker> _trackers = new();

    private readonly BlockingCollection<GameLogEntry> _eventLog = new();
    private readonly EventDatabaseWriter _dbWriter = new(serviceProvider);

    private void CreateGameTracker(Game game, int? matchId = null)
    {
      matchId ??= game.Match.Id;

      bool isNewGame = _dbWriter.TryAddGame(game, matchId.Value, out var _);
      GameTracker tracker = new(game, _eventLog, isInitialized: !isNewGame);
      _trackers.TryAdd(game.Id, tracker);
    }

    public override Task StartAsync(CancellationToken cancellationToken)
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
              _dbWriter.TryAddMatch(match, parentEvent.Id, out var _);

              // Now add the game connected to its parent match
              if (_activeGames.TryAdd(game.Id, game))
              {
                Log.Debug("Game Joined {Id}", game.Id);
                CreateGameTracker(game, match.Id);
              }
            }
          }
          // Match event already exists, just add the game to the match
          else if (parentEvent is Match match &&
                   _activeGames.TryAdd(game.Id, game))
          {
            // Add the match connected to its parent event
            // This is a no-op if the match already exists in the database
            _dbWriter.TryAddMatch(match, parentEvent.Id, out var _);

            // Event already exists, just add the game to the match
            Log.Debug("Game Joined {Id} for existing match", game.Id);
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
            _dbWriter.TryAddMatch(match, joinedEvent.Id, out var _);
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
        Log.Critical("Failed to start game service", ex);
        Log.Debug(ex.Message + "\n" + ex.StackTrace);
      }

      return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
