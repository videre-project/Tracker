/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;

using MTGOSDK.API.Chat;
using MTGOSDK.API.Play.Games;
using MTGOSDK.Core.Logging;
using static MTGOSDK.API.Events;


namespace Tracker.Services.MTGO.Events;

/// <summary>
/// Tracks game events and logs them to a blocking collection for processing.
/// </summary>
public class GameTracker
{
  private readonly BlockingCollection<GameLogEntry> _eventLog;

  /// <summary>
  /// Initializes a new instance of the <see cref="GameTracker"/> class.
  /// </summary>
  /// <param name="game">The game to track.</param>
  /// <param name="eventLog">The event log to write events to.</param>
  /// <param name="isInitialized">Whether the game was initialized previously.</param>
  /// <remarks>
  /// Specify <paramref name="isInitialized"/> as <c>true</c> if the game was
  /// initialized previously and the game state should be restored from the
  /// event log.
  /// </remarks>
  public GameTracker(
    Game game,
    BlockingCollection<GameLogEntry> eventLog,
    bool isInitialized = false)
  {
    _eventLog = eventLog;

    #region Game Initialization
    if (!isInitialized && game.IsPreGame)
    {
      Log.Debug("Initializing game {Id} data", game.Id);

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
        }
      }
    }
    #endregion

    Log.Debug("Configuring game {Id} events", game.Id);
    #region Global Callbacks
    game.OnGamePhaseChange += (CurrentPlayerPhase playerPhase) => Game_CurrentPhaseChanged(game, playerPhase);
    game.CurrentTurnChanged += Game_CurrentTurnChanged;
    game.OnZoneChange += (GameCard card) => Game_OnZoneChange(game, card);
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

  private void Game_CurrentPhaseChanged(Game game, CurrentPlayerPhase playerPhase)
  {
    GamePlayer activePlayer = playerPhase.ActivePlayer;
    GamePhase currentPhase = playerPhase.CurrentPhase;
    _eventLog.Add(new GameLogEntry(
      game.Id,
      activePlayer, // extract timestamp
      GameLogType.PhaseChange,
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
    _eventLog.Add(new GameLogEntry(
      game.Id,
      e, // extract timestamp
      GameLogType.TurnChange,
      JsonSerializer.Serialize(new
      {
        Turn = currentTurn,
        Player = activePlayer.Name
      })
    ));
  }

  private void Game_OnZoneChange(Game game, GameCard card)
  {
    GameZone? oldZone = card.PreviousZone;
    GameZone? newZone = card.Zone;
    _eventLog.Add(new GameLogEntry(
      game.Id,
      card, // extract timestamp
      GameLogType.ZoneChange,
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

    _eventLog.Add(new GameLogEntry(
      game.Id,
      action, // extract timestamp
      GameLogType.GameAction,
      // action.ToJSON()
      $"{action.Name} ({(action as CardAction)?.Card?.ToString()})"
    ));
  }

  private void Game_OnLifeChange(Game game, GamePlayer player)
  {
    int life = player.Life;
    _eventLog.Add(new GameLogEntry(
      game.Id,
      player, // extract timestamp
      GameLogType.LifeChange,
      JsonSerializer.Serialize(new
      {
        Life = life,
        Player = player.Name
      })
    ));
  }

  private void Game_OnLogMessage(Game game, Message message)
  {
    _eventLog.Add(new GameLogEntry(
      game.Id,
      message.Timestamp,
      GameLogType.LogMessage,
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
}
