/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

using MTGOSDK.API.Chat;
using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Games;
using MTGOSDK.Core;
using MTGOSDK.Core.Logging;
using static MTGOSDK.API.Events;

using Tracker.Services.MTGO.Events;


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
    private readonly BlockingCollection<EventLogEntry> _eventLog = new();

    private readonly EventDatabaseWriter _dbWriter = new(serviceProvider);

    // private void Game_PromptChanged(GameEventArgs args)
    // {
    //   GamePrompt prompt = args.Game.Prompt!;
    //   Game game = args.Game;
    //   _eventLog.Add(new EventLogEntry(
    //     args, // extract timestamp
    //     EventType.PromptChange,
    //     prompt.ToJSON()
    //   ));
    // }

    private void Game_CurrentPhaseChanged(Game game, CurrentPlayerPhase playerPhase)
    {
      GamePlayer activePlayer = playerPhase.ActivePlayer;
      GamePhase currentPhase = playerPhase.CurrentPhase;
      _eventLog.Add(new EventLogEntry(
        game.Id,
        activePlayer, // extract timestamp
        EventType.PhaseChange,
        JsonSerializer.Serialize(new
        {
          Phase = currentPhase.ToString(),
          Player = activePlayer.Name
        })
      ));
    }

    private void Game_CurrentTurnChanged(GameEventArgs e)
    {
      int currentTurn = e.Game.CurrentTurn;
      GamePlayer activePlayer = e.Game.ActivePlayer!;
      Game game = e.Game;
      _eventLog.Add(new EventLogEntry(
        game.Id,
        e, // extract timestamp
        EventType.TurnChange,
        JsonSerializer.Serialize(new
        {
          Turn = currentTurn,
          Player = activePlayer.Name
        })
      ));
    }

    // private void Game_OnCardCreated(Game game, GameCard card)
    // {
    //   _eventLog.Add(new EventLogEntry(
    //     game.Id,
    //     card, // extract timestamp
    //     EventType.CardCreated,
    //     JsonSerializer.Serialize(new
    //     {
    //       Card = card.ToString(),
    //       Owner = card.Owner?.ToString(),
    //       Zone = card.Zone?.Name,
    //       ActualZone = card.ActualZone?.Name,
    //       IsActivatedAbility = card.IsActivatedAbility,
    //       IsTriggeredAbility = card.IsTriggeredAbility,
    //       IsDelayedTrigger = card.IsDelayedTrigger,
    //       IsReplacementEffect = card.IsReplacementEffect,
    //     })
    //   ));
    // }

    private void Game_OnZoneChange(Game game, GameCard card)
    {
      GameZone? oldZone = card.PreviousZone;
      GameZone? newZone = card.Zone;
      _eventLog.Add(new EventLogEntry(
        game.Id,
        card, // extract timestamp
        EventType.ZoneChange,
        JsonSerializer.Serialize(new
        {
          Card = card.ToString(),
          OldZone = oldZone?.Name,
          NewZone = newZone?.Name
        })
      ));
    }

    private void Game_OnGameAction(Game game, GameAction action)
    {
      if (action is CardAction cardAction &&
          cardAction.RequiresTargets && !cardAction.IsTargetsSet)
      {
        AutoResetEvent targetsProcessed = new(false);
        cardAction.OnTargetsSet += (_) => targetsProcessed.Set();
        targetsProcessed.WaitOne(1_000); // Wait up to 1s for targets to be set
      }

      _eventLog.Add(new EventLogEntry(
        game.Id,
        action, // extract timestamp
        EventType.GameAction,
        // action.ToJSON()
        $"{action.Name} ({(action as CardAction)?.Card?.ToString()})"
      ));
    }

    private void Game_OnLifeChange(Game game, GamePlayer player)
    {
      int life = player.Life;
      _eventLog.Add(new EventLogEntry(
        game.Id,
        player, // extract timestamp
        EventType.LifeChange,
        JsonSerializer.Serialize(new
        {
          Life = life,
          Player = player.Name
        })
      ));
    }

    private void Game_OnLogMessage(Game game, Message message)
    {
      _eventLog.Add(new EventLogEntry(
        game.Id,
        message.Timestamp,
        EventType.LogMessage,
        message.Text
      ));
    }

    private void Game_GameStatusChange(Game game)
    {
      // Unsubscribe from all events when the game is over
      GameStatus status = game.Status;
      if (status == GameStatus.Finished)
      {
        game.ClearEvents();
      }
    }

    protected void ConfigureGameEvents(Game game)
    {
      Log.Debug("Configuring game {Id} events", game.Id);

      #region Game Initialization
      if (game.IsPreGame)
      {
        foreach (GamePlayer player in game.Players)
        {
          Game_OnLifeChange(game, player);
        }

        //
        // If the game is currently pre-game, manually trigger the ZoneChange and
        // CardCreated events for each card in the Hand zone as these events are
        // fired too early to be caught by our event hooking.
        //
        foreach (GamePlayer player in game.Players)
        {
          GameZone handZone = game.GetGameZone(player, CardZone.Hand);
          if (handZone.Count == 0) continue;

          foreach (GameCard card in handZone.Cards)
          {
            Game_OnZoneChange(game, card);
            // Game_OnCardCreated(game, card);
          }
        }
      }
      #endregion

      #region Global Callbacks
      // game.PromptChanged += Game_PromptChanged;
      game.OnGamePhaseChange += (CurrentPlayerPhase playerPhase) => Game_CurrentPhaseChanged(game, playerPhase);
      game.CurrentTurnChanged += Game_CurrentTurnChanged;
      // game.OnCardCreated += (GameCard card) => Game_OnCardCreated(game, card);
      game.OnZoneChange += (GameCard card) => Game_OnZoneChange(game, card);
      // game.OnGameAction += (GameAction action) => Game_OnGameAction(game, action);
      game.OnLifeChange += (GamePlayer player) => Game_OnLifeChange(game, player);
      game.OnLogMessage += (Message message) => Game_OnLogMessage(game, message);
      game.GameStatusChanged += () => Game_GameStatusChange(game);

      // Ensure that the global event is initialized before doing any processing
      CardAction.TargetSetChanged.EnsureInitialize();
      #endregion

      #region GameAction Callbacks
      // Queue any game actions to defer processing after it has been finalized.
      ConcurrentStack<GameAction> pendingActions = new();
      AutoResetEvent gameActionProcessed = new(false);
      object actionLock = new object();
      ConcurrentQueue<GameAction> undoActions = new();
      uint lastTimestamp = 0;
      game.OnGameAction += (GameAction action) =>
      {
        lock (actionLock)
        {
          gameActionProcessed.Set();

          // Undo the last action on the stack if the action is an undo action.
          if (action is UndoAction)
          {
            undoActions.Enqueue(action);
            pendingActions.TryPop(out GameAction? _);
            lastTimestamp = action.Timestamp;
            return;
          }
          // Otherwise, cancel all actions on the stack from the current
          // interaction timestamp if the action is a cancel action.
          if (action is PrimitiveAction primitiveAction &&
              primitiveAction.Name == "Cancel")
          {
            while (pendingActions.TryPeek(out GameAction? pendingAction) &&
                   pendingAction.Timestamp == action.Timestamp)
            {
              pendingActions.TryPop(out GameAction? _);
            }
            return;
          }

          // Push the action onto the stack
          pendingActions.Push(action);
          lastTimestamp = action.Timestamp;
        }
      };

      // Only process pending actions once the interaction state has advanced
      // (i.e. the action has been finalized and committed to the game state).
      game.OnPromptChanged += delegate(GamePrompt prompt)
      {
        gameActionProcessed.WaitOne(500); // Wait up to 500ms for a new action.

        // Process a local copy of the pending actions stack to process.
        Stack<GameAction> actions;
        lock(actionLock)
        {
          // If the interaction timestamp has advanced due to an undo action,
          // skip processing any pending actions until it is updated again.
          if (undoActions.TryPeek(out GameAction? undoAction) &&
              undoAction.Timestamp >= lastTimestamp)
          {
            undoActions.TryDequeue(out GameAction? _);
            lastTimestamp = undoAction.Timestamp;
            return;
          }

          // If there are no pending actions or if the current interaction
          // timestamp has not advanced, skip processing any actions.
          uint timestamp = prompt.Timestamp;
          if (pendingActions.Count == 0 || timestamp <= lastTimestamp) return;

          // Otherwise, start processing all pending actions in a local stack.
          actions = new();
          while (pendingActions.TryPeek(out GameAction? action) &&
                 // Ensure the action was committed after the updated prompt.
                 // Use the updated prompt timestamp as we process each action.
                 action.Timestamp < timestamp)
          {
            pendingActions.TryPop(out GameAction? _);
            actions.Push(action);
          }
        }
        // Process the actions in reverse order (i.e. the original push order).
        while (actions.TryPop(out GameAction? action))
        {
          Game_OnGameAction(game, action);
        }
      };

      game.GameStatusChanged += delegate()
      {
        // If the game state changes, flush any pending actions to the event log.
        Stack<GameAction> actions;
        lock(actionLock)
        {
          actions = new();
          while (pendingActions.TryPop(out GameAction? action))
          {
            actions.Push(action);
          }
        }
        while (actions.TryPop(out GameAction? action))
        {
          Game_OnGameAction(game, action);
        }

        Game_GameStatusChange(game);
      };
      #endregion

      Log.Debug("Game {Id} events configured", game.Id);
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
                _dbWriter.TryAddGame(game, match.Id, out var _);
                ConfigureGameEvents(game);
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
            _dbWriter.TryAddGame(game, match.Id, out var _);
            ConfigureGameEvents(game);
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
              _dbWriter.TryAddGame(game, match.Id, out var _);
              ConfigureGameEvents(game);
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
      foreach (EventLogEntry entry in _eventLog.GetConsumingEnumerable(stoppingToken))
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
