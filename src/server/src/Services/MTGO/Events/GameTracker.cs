/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading;

using MTGOSDK.API.Chat;
using MTGOSDK.API.Play.Games;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Serialization;
using static MTGOSDK.API.Events;


namespace Tracker.Services.MTGO.Events;

/// <summary>
/// Tracks game events and logs them to a blocking collection for processing.
/// </summary>
public class GameTracker: IDisposable
{
  private readonly Game m_game;
  private readonly BlockingCollection<GameLogEntry> _eventLog;
  private readonly EventDatabaseWriter _dbWriter;

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
    EventDatabaseWriter dbWriter,
    bool isInitialized = false)
  {
    m_game = game;
    _eventLog = eventLog;
    _dbWriter = dbWriter;

    #region Game Initialization
    if (!isInitialized && game.IsPreGame)
    {
      Log.Debug("Initializing game {Id} data", game.Id);

      foreach (GamePlayer player in game.Players)
      {
        Game_OnLifeChange(player);
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
          Game_OnZoneChange(card);
        }
      }
    }
    #endregion

    Log.Debug("Configuring game {Id} events", game.Id);
    #region Global Callbacks
    game.OnGamePhaseChange += Game_CurrentPhaseChanged;
    game.CurrentTurnChanged += Game_CurrentTurnChanged;
    game.OnZoneChange += Game_OnZoneChange;
    game.OnLifeChange += Game_OnLifeChange;
    game.OnLogMessage += Game_OnLogMessage;
    game.GameStatusChanged += Game_GameStatusChange;
    game.OnGameResultsChanged += Game_OnGameResultsChanged;

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
        Game_OnGameAction(action);
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
        Game_OnGameAction(action);
      }

      Game_GameStatusChange();
    };
    #endregion

    Log.Debug("Game {Id} events configured", game.Id);
  }

  private bool _disposed = false;

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;

    m_game.ClearEvents();
    GC.SuppressFinalize(this);
  }

  private void Game_CurrentPhaseChanged(CurrentPlayerPhase playerPhase)
  {
    GamePlayer activePlayer = playerPhase.ActivePlayer;
    GamePhase currentPhase = playerPhase.CurrentPhase;
    _eventLog.Add(new GameLogEntry(
      m_game.Id,
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

  private void Game_OnZoneChange(GameCard card)
  {
    GameZone? oldZone = card.PreviousZone;
    GameZone? newZone = card.Zone;
    _eventLog.Add(new GameLogEntry(
      m_game.Id,
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

  private void Game_OnGameAction(GameAction action)
  {
    if (action is CardAction cardAction &&
        cardAction.RequiresTargets && !cardAction.IsTargetsSet)
    {
      AutoResetEvent targetsProcessed = new(false);
      cardAction.OnTargetsSet += (_) => targetsProcessed.Set();
      targetsProcessed.WaitOne(1_000); // Wait up to 1s for targets to be set
    }

    _eventLog.Add(new GameLogEntry(
      m_game.Id,
      action, // extract timestamp
      GameLogType.GameAction,
      action.ToJSON()
    ));
  }

  private void Game_OnLifeChange(GamePlayer player)
  {
    int life = player.Life;
    _eventLog.Add(new GameLogEntry(
      m_game.Id,
      player, // extract timestamp
      GameLogType.LifeChange,
      JsonSerializer.Serialize(new
      {
        Life = life,
        Player = player.Name
      })
    ));
  }

  private void Game_OnLogMessage(Message message)
  {
    if (_disposed) return;

    _eventLog.Add(new GameLogEntry(
      m_game.Id,
      message.Timestamp,
      GameLogType.LogMessage,
      message.Text
    ));
  }

  private void Game_GameStatusChange()
  {
    // Unsubscribe from all events when the game is over
    GameStatus status = m_game.Status;
    if (status == GameStatus.Finished)
    {
      Dispose();
    }
  }

  private void Game_OnGameResultsChanged(IList<GamePlayerResult> results)
  {
    if (_dbWriter.TryUpdateGameResults(m_game, results))
    {
      string jsonRes = JsonSerializer.Serialize(results);
      Log.Debug("Updated game results for {Id}: {Results}", m_game.Id, jsonRes);
    }
  }
}
