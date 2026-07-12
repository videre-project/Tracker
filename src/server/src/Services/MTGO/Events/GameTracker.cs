/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Play.Games.Processors;
using MTGOSDK.API.Play.Games.Processors.EventArgs;
using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Serialization;
using static MTGOSDK.Core.Reflection.DLRWrapper;

using Tracker.Database.Models;


namespace Tracker.Services.MTGO.Events;

/// <summary>
/// Tracks game events via GameProcessor and writes structured data to the
/// database. Replaces the manual event subscription approach with the
/// snapshot-based processor pipeline.
/// </summary>
public class GameTracker : IDisposable
{
  private readonly Game _game;
  private readonly BlockingCollection<GameLogEntry> _eventLog;
  private readonly EventDatabaseWriter _dbWriter;

  // State tracking
  private readonly HashSet<int> _seenCardIds = new();
  private readonly HashSet<int> _seenPlayerIndices = new();
  private readonly HashSet<int> _currentZoneTransferCardIds = new();
  private int _currentNonce;
  private int _previousNonce;
  private int _lastTurnNumber;
  private string? _lastPhase;

  // Pending models buffered per snapshot tick — enqueued for async write
  private GameStateSnapshot? _pendingSnapshot;
  private readonly ConcurrentDictionary<int, GamePrompt> _promptsByNonce = new();

  /// <summary>
  /// Guards <see cref="EnqueueFlush"/> so that <see cref="ForceFlush"/>
  /// (API thread) cannot interleave with drain-loop callers that read/clear
  /// <c>_pending*</c> buffers.
  /// </summary>
  private readonly object _flushLock = new object();
  private readonly List<GameCardModel> _pendingCards = new();
  private readonly List<GamePlayerModel> _pendingPlayers = new();
  private readonly List<ZoneTransferModel> _pendingZoneTransfers = new();
  private readonly List<CardStateChangeModel> _pendingCardChanges = new();
  private readonly List<PlayerStateChangeModel> _pendingPlayerChanges = new();
  private readonly List<GameAction> _pendingActions = new();
  private readonly List<GameLogModel> _pendingLogs = new();

  // Background writer — decouples processor thread from DB I/O
  private readonly BlockingCollection<object> _flushQueue = new();
  private readonly Thread _writerThread;

  /// <summary>
  /// Queued for immediate serialization + SSE emission on the writer
  /// thread when an action is finalized. The serialized model is cached
  /// in <see cref="_preSerializedActions"/> for reuse by the next
  /// <see cref="FlushBatch"/>, avoiding double serialization.
  /// </summary>
  private sealed record ActionEmit(
    int GameId,
    GameAction Action,
    int AssignedNonce,
    DateTime SnapshotTimestamp);

  /// <summary>
  /// Sentinel enqueued by <see cref="ForceFlush"/> after the flush batch.
  /// The writer thread signals the event after processing all preceding
  /// items, guaranteeing that DB writes have committed before the API
  /// query runs.
  /// </summary>
  private sealed record FlushComplete(ManualResetEventSlim Signal);


  // Writer-thread-local cache of action models serialized via ActionEmit.
  // Keyed by GameAction reference identity so the FlushBatch can look up
  // the pre-serialized model instead of re-serializing.
  private readonly Dictionary<GameAction, GameActionModel>
    _preSerializedActions = new();

  /// <summary>
  /// A snapshot of all buffered models for a single snapshot tick,
  /// transferred to the writer thread for async DB insertion.
  /// </summary>
  private sealed record FlushBatch(
    int GameId,
    GameStateSnapshot Snapshot,
    int PreviousNonce,
    List<GameCardModel> Cards,
    List<GamePlayerModel> Players,
    List<ZoneTransferModel> ZoneTransfers,
    List<CardStateChangeModel> CardChanges,
    List<PlayerStateChangeModel> PlayerChanges,
    List<GameAction> Actions,
    List<GameLogModel> Logs,
    GamePrompt? Prompt);

  /// <summary>
  /// Eagerly initializes the static hooks used by GameTracker so that
  /// the IPC type-dump cost is paid at startup, not on first game join.
  /// </summary>
  public static void EnsureHooksInitialized()
  {
    Game.GameResultsChanged.EnsureInitialize();
  }

  /// <summary>
  /// Initializes a new instance of the <see cref="GameTracker"/> class.
  /// </summary>
  /// <param name="game">The game to track.</param>
  /// <param name="eventLog">The event log for SSE notifications.</param>
  /// <param name="dbWriter">The database writer.</param>
  public GameTracker(
    Game game,
    BlockingCollection<GameLogEntry> eventLog,
    EventDatabaseWriter dbWriter)
  {
    _game = game;
    _eventLog = eventLog;
    _dbWriter = dbWriter;

    // Start the background writer thread
    _writerThread = new Thread(ProcessFlushQueue)
    {
      Name = $"GameTracker-Writer-{game.Id}",
      IsBackground = true
    };
    _writerThread.Start();

    Log.Debug("Configuring game {Id} GameProcessor events", game.Id);

    // Subscribe to GameProcessor events (lazy activation via ProcessorEvent).
    // Registration order determines processing order within each snapshot:
    //   RevealedZoneTracker → ZoneChangeTracker → PropertyChangeTracker →
    //   ActionProcessor → LogMessageProcessor → PromptProcessor
    game.OnRevealedCards += OnRevealedCards;
    game.OnZoneChanged += OnZoneChanged;
    game.OnCardChanged += OnCardChanged;
    game.OnPlayerChanged += OnPlayerChanged;
    game.OnActionFinalized += OnActionFinalized;
    game.OnLogMessage += OnLogMessage;
    game.OnDamageAssignment += OnDamageAssignment;
    game.OnPromptChanged += OnPromptChanged;

    // Still need direct Game events for lifecycle
    game.GameStatusChanged += OnGameStatusChanged;
    game.OnGameResultsChanged += OnGameResultsChanged;

    // All processors are registered — allow the drain loop to start
    // processing queued hooks. Before this call, hooks are buffered but
    // not drained, ensuring no snapshots are processed with an incomplete
    // processor set.
    game.ReadyProcessor();

    Log.Debug("Game {Id} GameProcessor events configured", game.Id);
  }

  /// <summary>
  /// Forces the current tick's buffered data to be flushed to the database
  /// synchronously. Called from API endpoints to ensure data is available
  /// before querying the DB. Safe to call from any thread.
  /// </summary>
  internal void ForceFlush()
  {
    if (_disposed) return;

    // Wait for the GameProcessor drain loop to finish processing all
    // currently-pending hooks. This ensures that all processor events
    // (including PromptProcessor) have fired and their data is buffered
    // before we flush. Without this, ForceFlush can race ahead of the
    // 25ms drain-loop holdback and flush an empty or incomplete snapshot.
    _game.WaitForPendingProcessing(TimeSpan.FromMilliseconds(200));

    using var signal = new ManualResetEventSlim(false);
    EnqueueFlush();
    _flushQueue.Add(new FlushComplete(signal));
    signal.Wait(TimeSpan.FromSeconds(5));
  }

  private volatile bool _disposed = false;

  public void Dispose()
  {
    if (_disposed) return;

    // Drain any remaining hooks before shutting down event handlers.
    // OnGameStatusChanged already waits up to 30s, but Dispose() may
    // also be called directly (e.g. service shutdown).
    _game.WaitForPendingProcessing(TimeSpan.FromSeconds(30));

    _disposed = true;

    // Enqueue final batch, then signal the writer thread to finish.
    EnqueueFlush();
    _flushQueue.CompleteAdding();
    _writerThread.Join();

    // Clears EventProxy, EventHookWrapper, ProcessorEvent handlers and
    // deactivates the GameProcessor from the static routing table.
    _game.ClearEvents();

    GC.SuppressFinalize(this);
  }

  /// <summary>
  /// Ensures the current snapshot tick matches the incoming event's nonce.
  /// When the nonce changes, enqueues the previous tick's buffered data
  /// for async DB write.
  /// </summary>
  private void EnsureGameState(GameEventArgs args)
  {
    int nonce = args.Nonce;
    if (nonce == _currentNonce && _pendingSnapshot != null)
      return;

    // Same nonce but snapshot was flushed mid-processing (by ForceFlush).
    // Recreate the pending snapshot so subsequent events continue accumulating.
    // FlushStateData handles the existing DB record by adding child rows and
    // merging PromptOptions. Don't touch _previousNonce or zone-transfer
    // tracking — earlier events in this nonce already set them correctly.
    if (nonce == _currentNonce && _pendingSnapshot == null)
    {
      _pendingSnapshot = args.Snapshot;
      return;
    }

    // Nonce changed — enqueue previous tick's data for async write
    EnqueueFlush();

    // New nonce — clear zone transfer tracking for new snapshot tick
    _currentZoneTransferCardIds.Clear();
    _previousNonce = _currentNonce;
    _currentNonce = nonce;
    _pendingSnapshot = args.Snapshot;

    // Emit GameState SSE immediately for turn/phase changes
    string phase = _pendingSnapshot.CurrentPhase.ToString();
    if (_pendingSnapshot.TurnNumber != _lastTurnNumber || phase != _lastPhase)
    {
      _eventLog.Add(new GameLogEntry(_game.Id,
        _pendingSnapshot.ClientTimestamp, GameLogType.GameState,
        JsonSerializer.Serialize(new GameStateData
        {
          Turn = _pendingSnapshot.TurnNumber,
          Phase = phase,
          PreviousTurn = _lastTurnNumber,
          PreviousPhase = _lastPhase
        }, JsonSerializerOptions.Web), _currentNonce));

      _lastTurnNumber = _pendingSnapshot.TurnNumber;
      _lastPhase = phase;
    }
  }

  /// <summary>
  /// Transfers all buffered models into a <see cref="FlushBatch"/> and
  /// enqueues it for the background writer thread. Returns immediately.
  /// Also emits SSE events for all buffered data — zone/card/player/state
  /// changes are POCO serializations (no IPC), safe on the processor thread.
  /// </summary>
  private void EnqueueFlush()
  {
    lock (_flushLock)
    {
      if (_pendingSnapshot == null) return;

      // Look up prompt for this snapshot's nonce — just a reference, no IPC.
      // Serialization happens on the writer thread.
      _promptsByNonce.TryRemove(_currentNonce, out var prompt);

      // Ownership transfer: copy current buffers into the batch
      var batch = new FlushBatch(
        _game.Id,
        _pendingSnapshot,
        _previousNonce,
        new List<GameCardModel>(_pendingCards),
        new List<GamePlayerModel>(_pendingPlayers),
        new List<ZoneTransferModel>(_pendingZoneTransfers),
        new List<CardStateChangeModel>(_pendingCardChanges),
        new List<PlayerStateChangeModel>(_pendingPlayerChanges),
        new List<GameAction>(_pendingActions),
        new List<GameLogModel>(_pendingLogs),
        prompt);

      _flushQueue.Add(batch);

      // Clear local buffers for the next tick
      _pendingSnapshot = null;
      _pendingCards.Clear();
      _pendingPlayers.Clear();
      _pendingZoneTransfers.Clear();
      _pendingCardChanges.Clear();
      _pendingPlayerChanges.Clear();
      _pendingActions.Clear();
      _pendingLogs.Clear();
    }
  }


  /// <summary>
  /// Background writer thread entry point. Drains <see cref="_flushQueue"/>
  /// and processes items sequentially. Item types:
  /// <list type="bullet">
  ///   <item><see cref="ActionEmit"/> — serialize the action (IPC-heavy),
  ///   emit SSE immediately, and cache the model for DB reuse.</item>
  ///   <item><see cref="FlushBatch"/> — write the snapshot's data
  ///   (including prompt options) to the database, reusing any
  ///   pre-serialized action models.</item>
  ///   <item><see cref="FlushComplete"/> — signal that all preceding
  ///   items have been processed (used by ForceFlush).</item>
  /// </list>
  /// ActionEmit items are always enqueued before their FlushBatch, so
  /// <see cref="_preSerializedActions"/> is populated before the batch
  /// needs them.
  /// </summary>
  private void ProcessFlushQueue()
  {
    foreach (var item in _flushQueue.GetConsumingEnumerable())
    {
      if (item is FlushComplete fc)
      {
        fc.Signal.Set();
        continue;
      }

      if (item is ActionEmit ae)
      {
        try
        {
          var model = new GameActionModel(ae.Action);
          var actionTs = ae.Action.ClientTimestamp != default
              ? ae.Action.ClientTimestamp
              : ae.SnapshotTimestamp;
          _eventLog.Add(new GameLogEntry(
              ae.GameId, actionTs, GameLogType.GameAction,
              model.Data, ae.AssignedNonce));
          _preSerializedActions[ae.Action] = model;
        }
        catch (Exception ex)
        {
          Log.Error(ex, "Error emitting action SSE for game {Id}",
            ae.GameId);
        }
        continue;
      }

      if (item is not FlushBatch batch) continue;
      try
      {
        // Reuse pre-serialized action models from ActionEmit processing.
        // Fall back to serializing here if the ActionEmit failed.
        var actionModels = new List<GameActionModel>(batch.Actions.Count);
        foreach (var action in batch.Actions)
        {
          try
          {
            if (_preSerializedActions.Remove(action, out var cached))
            {
              actionModels.Add(cached);
            }
            else
            {
              actionModels.Add(
                new GameActionModel(action));
            }
          }
          catch (Exception ex)
          {
            Log.Error(ex, "Error building action model for game {Id}",
              batch.GameId);
          }
        }

        // Serialize prompt options on the writer thread (IPC-heavy, ~500ms).
        // Deferred from OnPromptChanged to avoid racing with EnqueueFlush.
        string? promptOptions = null;
        if (batch.Prompt != null)
        {
          try
          {
            promptOptions = SerializePromptOptions(batch.Prompt);
          }
          catch (Exception ex)
          {
            Log.Error(ex, "Error serializing prompt options for game {Id}",
              batch.GameId);
          }
        }

        _dbWriter.FlushStateData(
          batch.GameId,
          batch.Snapshot,
          batch.Cards,
          batch.Players,
          batch.ZoneTransfers,
          batch.CardChanges,
          batch.PlayerChanges,
          actionModels,
          batch.Logs,
          promptOptions);
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error writing game state batch for game {Id}",
          batch.GameId);
      }
    }
  }

  //
  // GameProcessor event handlers
  //

  private void OnRevealedCards(RevealedCardsEventArgs e)
  {
    if (_disposed) return;
    try
    {
      EnsureGameState(e);

      var newTransfers = new List<ZoneTransferModel>();

      foreach (var card in e.Arrived)
      {
        _currentZoneTransferCardIds.Add(card.Id);
        EnsureCardTracked(card, e.Snapshot, initialZoneOverride: "Revealed");

        var zt = new ZoneTransferModel
        {
          CardId = card.Id,
          CardName = card.Name,
          FromZone = card.Zone?.Name,
          ToZone = "Revealed",
          SourceId = null,
          Type = "Arrived"
        };
        _pendingZoneTransfers.Add(zt);
        newTransfers.Add(zt);
      }

      foreach (var card in e.Departed)
      {
        _currentZoneTransferCardIds.Add(card.Id);

        var zt = new ZoneTransferModel
        {
          CardId = card.Id,
          CardName = card.Name,
          FromZone = "Revealed",
          ToZone = card.Zone?.Name,
          SourceId = null,
          Type = "Departed"
        };
        _pendingZoneTransfers.Add(zt);
        newTransfers.Add(zt);
      }

      if (newTransfers.Count > 0)
      {
        _eventLog.Add(new GameLogEntry(_game.Id,
          e.Snapshot.ClientTimestamp, GameLogType.Reveal,
          JsonSerializer.Serialize(newTransfers.Select(zt => new ZoneTransferData
          {
            CardId = zt.CardId, CardName = zt.CardName,
            FromZone = zt.FromZone, ToZone = zt.ToZone,
            SourceId = zt.SourceId, Type = zt.Type
          }), JsonSerializerOptions.Web), e.Nonce));
      }

      Log.Trace("[Game {Id}] {Count} revealed zone transfers (nonce {Nonce})",
        _game.Id, newTransfers.Count, e.Nonce);
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Error processing revealed cards for game {Id}", _game.Id);
    }
  }

  private void OnZoneChanged(ZoneChangeEventArgs e)
  {
    if (_disposed) return;
    try
    {
      EnsureGameState(e);

      var newTransfers = new List<ZoneTransferModel>();

      // Arrived = new card entering the board
      foreach (var card in e.Arrived)
      {
        _currentZoneTransferCardIds.Add(card.Id);
        EnsureCardTracked(card, e.Snapshot);

        var zt = new ZoneTransferModel
        {
          CardId = card.Id,
          CardName = card.Name,
          FromZone = null,
          ToZone = card.Zone?.Name,
          SourceId = card.SourceId > 0 ? card.SourceId : null,
          Type = "Arrived"
        };
        _pendingZoneTransfers.Add(zt);
        newTransfers.Add(zt);
      }

      // Departed = card leaving the board
      foreach (var card in e.Departed)
      {
        _currentZoneTransferCardIds.Add(card.Id);

        var zt = new ZoneTransferModel
        {
          CardId = card.Id,
          CardName = card.Name,
          FromZone = card.Zone?.Name,
          ToZone = null,
          SourceId = null,
          Type = "Departed"
        };
        _pendingZoneTransfers.Add(zt);
        newTransfers.Add(zt);
      }

      // Moved = card changed zones (gets a new ThingID)
      foreach (var (from, to) in e.Moved)
      {
        _currentZoneTransferCardIds.Add(to.Id);
        EnsureCardTracked(to, e.Snapshot);

        var zt = new ZoneTransferModel
        {
          CardId = to.Id,
          CardName = to.Name,
          FromZone = from.Zone?.Name,
          ToZone = to.Zone?.Name,
          SourceId = from.Id,
          Type = "Moved"
        };
        _pendingZoneTransfers.Add(zt);
        newTransfers.Add(zt);
      }

      // Emit SSE immediately (don't wait for next nonce)
      if (newTransfers.Count > 0)
      {
        _eventLog.Add(new GameLogEntry(_game.Id,
          e.Snapshot.ClientTimestamp, GameLogType.ZoneChange,
          JsonSerializer.Serialize(newTransfers.Select(zt => new ZoneTransferData
          {
            CardId = zt.CardId, CardName = zt.CardName,
            FromZone = zt.FromZone, ToZone = zt.ToZone,
            SourceId = zt.SourceId, Type = zt.Type
          }), JsonSerializerOptions.Web), e.Nonce));
      }

      Log.Trace("[Game {Id}] {Count} zone transfers (nonce {Nonce})",
        _game.Id, _pendingZoneTransfers.Count, e.Nonce);
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Error processing zone change for game {Id}", _game.Id);
    }
  }

  private void OnCardChanged(CardChangedEventArgs e)
  {
    if (_disposed) return;
    try
    {
      EnsureGameState(e);
      EnsureCardTracked(e.Current, e.Snapshot);

      bool hasZoneTransfer = _currentZoneTransferCardIds.Contains(e.Current.Id);
      var changes = DiffCard(e.Previous, e.Current, hasZoneTransfer);

      if (changes.Count > 0)
      {
        _pendingCardChanges.AddRange(changes);

        // Emit SSE immediately (don't wait for next nonce)
        _eventLog.Add(new GameLogEntry(_game.Id,
          e.Snapshot.ClientTimestamp, GameLogType.CardChange,
          JsonSerializer.Serialize(changes.Select(cc => new CardChangeData
          {
            CardId = cc.CardId, CardName = cc.CardName,
            Property = cc.Property, OldValue = cc.OldValue,
            NewValue = cc.NewValue
          }), JsonSerializerOptions.Web), e.Nonce));

        Log.Trace("[Game {Id}] {Count} card changes for {Card} (nonce {Nonce})",
          _game.Id, changes.Count, e.Current.Name, e.Nonce);
      }
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Error processing card change for game {Id}", _game.Id);
    }
  }

  private void OnPlayerChanged(PlayerChangedEventArgs e)
  {
    if (_disposed) return;
    try
    {
      EnsureGameState(e);

      // Track new player (PropertyChangeTracker emits prev==curr for new players)
      if (_seenPlayerIndices.Add(e.PlayerIndex))
      {
        _pendingPlayers.Add(
          EventDatabaseWriter.BuildGamePlayerModel(
            _game.Id, e.Current, e.PlayerIndex));
        Log.Trace("[Game {Id}] New player {Name} (index {Index})",
          _game.Id, e.Current.Name, e.PlayerIndex);
      }

      // Diff player properties (skip if prev and curr are the same object = new player)
      if (!ReferenceEquals(e.Previous, e.Current))
      {
        var changes = DiffPlayer(e.Previous, e.Current, e.PlayerIndex);
        if (changes.Count > 0)
        {
          _pendingPlayerChanges.AddRange(changes);

          // Emit SSE immediately (don't wait for next nonce)
          _eventLog.Add(new GameLogEntry(_game.Id,
            e.Snapshot.ClientTimestamp, GameLogType.PlayerChange,
            JsonSerializer.Serialize(changes.Select(pc => new PlayerChangeData
            {
              PlayerIndex = pc.PlayerIndex, PlayerName = pc.PlayerName,
              Property = pc.Property, OldValue = pc.OldValue,
              NewValue = pc.NewValue
            }), JsonSerializerOptions.Web), e.Nonce));

          Log.Trace("[Game {Id}] {Count} player changes for {Name} (nonce {Nonce})",
            _game.Id, changes.Count, e.Current.Name, e.Nonce);
        }
      }
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Error processing player change for game {Id}", _game.Id);
    }
  }

  private void OnActionFinalized(ActionFinalizedEventArgs e)
  {
    if (_disposed) return;
    try
    {
      EnsureGameState(e);

      // Buffer the raw action for DB persistence in the next flush batch.
      _pendingActions.Add(e.Action);

      // Queue for immediate serialization + SSE emission on the writer
      // thread. This avoids blocking the processor drain loop with IPC
      // (ToJSON, Card, Targets) while still emitting SSE as soon as the
      // writer thread picks up the item — before the next FlushBatch.
      //
      // ActionProcessor only finalizes pending actions when it encounters
      // a TurnStep — so every action was performed during the *previous*
      // state. Assign the previous nonce so the action groups visually
      // with the state it was executed in rather than the one it produced.
      var assignedNonce = _previousNonce != 0 ? _previousNonce : e.Nonce;
      _flushQueue.Add(new ActionEmit(
          _game.Id, e.Action, assignedNonce, e.Snapshot.ClientTimestamp));

      Log.Trace("[Game {Id}] Action finalized: {Name} (nonce {Nonce})",
        _game.Id, e.Action.Name, e.Nonce);
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Error processing action for game {Id}", _game.Id);
    }
  }

  private void OnLogMessage(LogMessageCorrelatedEventArgs e)
  {
    if (_disposed) return;

    try
    {
      EnsureGameState(e);

      // Use the message's own client-side arrival timestamp rather than
      // the correlated snapshot's timestamp. Both are in the same client
      // time domain (__timestamp), but the message's timestamp reflects
      // when the chat channel actually received the message — placing it
      // at its true chronological position instead of inheriting the
      // (potentially wrong) correlated snapshot's time.
      var timestamp = e.MessageClientTimestamp;

      _pendingLogs.Add(new GameLogModel
      {
        Timestamp = timestamp,
        GameLogType = "LogMessage",
        Data = e.Message.Text
      });
      Log.Trace("[Game {Id}] Log: {Text} (nonce {Nonce})",
        _game.Id, e.Message.Text, e.Nonce);

      // Notify SSE stream
      _eventLog.Add(new GameLogEntry(
        _game.Id,
        timestamp,
        GameLogType.LogMessage,
        e.Message.Text,
        e.Nonce));
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Error processing log message for game {Id}", _game.Id);
    }
  }

  private void OnDamageAssignment(DamageAssignmentEventArgs e)
  {
    if (_disposed) return;

    try
    {
      EnsureGameState(e);

      var data = JsonSerializer.Serialize(e.Assignments.Select(a =>
      {
        string? sourceName = null;
        if (e.Snapshot.Cards.TryGetValue(a.Source.Id, out var srcCard))
          sourceName = srcCard.Name;

        return new
        {
          sourceId = a.Source.Id,
          sourceName,
          totalDamage = a.MinimumTotal,
          targets = a.Distributions.Select(d =>
          {
            string? targetName = null;
            int targetId = d.Target.Id;
            if (e.Snapshot.Cards.TryGetValue(targetId, out var tgtCard))
              targetName = tgtCard.Name;
            else if (e.Snapshot.Players.TryGetValue(targetId, out var tgtPlayer))
              targetName = tgtPlayer.Name;

            return new
            {
              targetId,
              targetName,
              amount = d.Value,
              minimum = d.Minimum,
              maximum = d.Maximum,
            };
          })
        };
      }), JsonSerializerOptions.Web);

      _pendingLogs.Add(new GameLogModel
      {
        Timestamp = e.Snapshot.ClientTimestamp,
        GameLogType = "DamageAssignment",
        Data = data
      });

      _eventLog.Add(new GameLogEntry(
        _game.Id,
        e.Snapshot.ClientTimestamp,
        GameLogType.DamageAssignment,
        data,
        e.Nonce));

      Log.Trace("[Game {Id}] {Count} damage assignments (nonce {Nonce})",
        _game.Id, e.Assignments.Count, e.Nonce);
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Error processing damage assignment for game {Id}", _game.Id);
    }
  }

  private void OnPromptChanged(PromptChangedEventArgs e)
  {
    if (_disposed) return;
    if (e.Prompt == null) return;

    // Store the prompt for the normal flush path — the next
    // EnqueueFlush will pick it up via _promptsByNonce.TryRemove.
    _promptsByNonce[e.Nonce] = e.Prompt;
  }

  /// <summary>
  /// Serializes prompt options to a JSON array using each action's ToJSON().
  /// Preserves full action data (card names, targets, choices, etc.) so the
  /// frontend can render selection prompts correctly.
  /// </summary>
  private static string? SerializePromptOptions(GamePrompt prompt)
  {
    IDictionary<ActionType, IList<GameAction>> options;
    try
    {
      options = prompt.Options;
    }
    catch (Exception ex)
    {
      Log.Error("[PromptOptions] Options getter threw: {Exception}", ex.ToString());
      return null;
    }

    var jsonObjects = new List<string>();
    foreach (var kvp in options)
    {
      foreach (var action in kvp.Value)
      {
        if (action == null) continue;
        try
        {
          jsonObjects.Add(action.ToJSON());
        }
        catch (Exception ex)
        {
          Log.Error(ex, "[PromptOptions] ToJSON() threw for {Type}",
            kvp.Key);
        }
      }
    }

    return jsonObjects.Count > 0
      ? "[" + string.Join(",", jsonObjects) + "]"
      : null;
  }

  //
  // Direct Game event handlers (lifecycle)
  //

  private void OnGameStatusChanged()
  {
    if (_game.Status == GameStatus.Finished)
    {
      Task.Run(() =>
      {
        // Wait for the drain loop to finish processing all remaining
        // hooks. This replaces the old fixed 5-second delay — the drain
        // may take much longer if there's a large hook buffer at game end.
        _game.WaitForPendingProcessing(TimeSpan.FromSeconds(30));

        // Brief grace period for OnGameResultsChanged to fire.
        // Results are computed before the status change in MTGO, but
        // the callbacks arrive from separate hooks and can race.
        Thread.Sleep(2_000);

        Dispose();
      });
    }
  }

  private void OnGameResultsChanged(IList<GamePlayerResult> results)
  {
    if (_dbWriter.TryUpdateGameResults(_game, results))
    {
      string jsonRes = JsonSerializer.Serialize(results);
      Log.Debug("Updated game results for {Id}: {Results}", _game.Id, jsonRes);
    }
  }

  //
  // Card tracking helpers
  //

  private void EnsureCardTracked(
    GameCard card, GameStateSnapshot snapshot,
    string? initialZoneOverride = null)
  {
    if (!_seenCardIds.Add(card.Id)) return;

    // Prefer the raw player index from the card's properties — this reads
    // directly from the MagicProperty dictionary without IPC. Fall back to
    // the GamePlayer wrapper + name matching only if unavailable.
    int ownerIdx = card.OwnerIndex;
    if (ownerIdx < 0)
      ownerIdx = FindPlayerIndex(snapshot, () => card.Owner);

    int ctrlIdx = card.ControllerIndex;
    if (ctrlIdx < 0)
      ctrlIdx = FindPlayerIndex(snapshot, () => card.Controller);

    _pendingCards.Add(
      EventDatabaseWriter.BuildGameCardModel(
        _game.Id, card, ownerIdx, ctrlIdx, initialZoneOverride));
    Log.Trace("[Game {Id}] New card {Name} (id {CardId}, zone {Zone})",
      _game.Id, card.Name, card.Id, card.Zone?.Name);

    // Emit initial card changes for non-default properties that the frontend
    // needs but can't derive from the card model (e.g. associations set at
    // creation time, which DiffCard would never see change).
    EmitInitialCardChanges(card);
  }

  /// <summary>
  /// Emits card change records for properties that are non-default when a card
  /// first appears. Without this, properties like Associations that are set at
  /// card creation and never change would be invisible to the frontend.
  /// </summary>
  private void EmitInitialCardChanges(GameCard card)
  {
    try
    {
      object inner = Unbind(card);
      if (inner is GameCardPartial partial)
      {
        var assoc = partial.ResolveAssociations();
        if (assoc.Count > 0)
        {
          _pendingCardChanges.Add(new CardStateChangeModel
          {
            CardId = card.Id,
            CardName = card.Name,
            Property = "Associations",
            OldValue = "{}",
            NewValue = JsonSerializer.Serialize(assoc, JsonSerializerOptions.Web)
          });
        }
      }
    }
    catch { }

    // Emit initial typeline
    try
    {
      var typeLine = card.TypeLine;
      if (!string.IsNullOrEmpty(typeLine))
      {
        _pendingCardChanges.Add(new CardStateChangeModel
        {
          CardId = card.Id,
          CardName = card.Name,
          Property = "TypeLine",
          OldValue = null,
          NewValue = typeLine
        });
      }
    }
    catch (Exception ex)
    {
      Log.Error(ex, "[Game {Id}] TypeLine failed for {Name} (id {CardId})",
        _game.Id, card.Name, card.Id);
    }
  }

  /// <summary>
  /// Resolves a GamePlayer to a player index using the snapshot's Players
  /// dictionary. Matches by name since that's stable across representations.
  /// </summary>
  private static int FindPlayerIndex(
    GameStateSnapshot snapshot, Func<GamePlayer?> getPlayer)
  {
    try
    {
      var player = getPlayer();
      if (player == null) return -1;
      string name = player.Name;
      foreach (var (idx, p) in snapshot.Players)
      {
        if (string.Equals(p.Name, name, StringComparison.Ordinal))
          return idx;
      }
    }
    catch { }
    return -1;
  }

  //
  // Property diffing
  //

  /// <summary>
  /// Diffs card properties between two snapshots and produces change rows.
  /// Skips Id, SourceId, Zone, PreviousZone when the card has a matching
  /// ZoneTransfer in the same snapshot.
  /// </summary>
  private static List<CardStateChangeModel> DiffCard(
    GameCard prev, GameCard curr, bool hasZoneTransfer)
  {
    var changes = new List<CardStateChangeModel>();
    int cardId = curr.Id;
    string cardName = curr.Name;

    // Zone-related properties — skip if zone transfer covers them
    if (!hasZoneTransfer)
    {
      AddIfChanged(changes, cardId, cardName, "Id",
        prev.Id.ToString(), curr.Id.ToString());
      AddIfChanged(changes, cardId, cardName, "SourceId",
        prev.SourceId.ToString(), curr.SourceId.ToString());
      AddIfChanged(changes, cardId, cardName, "Zone",
        prev.Zone?.Name, curr.Zone?.Name);
      AddIfChanged(changes, cardId, cardName, "PreviousZone",
        prev.PreviousZone?.Name, curr.PreviousZone?.Name);
    }

    // Numeric combat/stat properties
    AddIntIfChanged(changes, cardId, cardName, "Power",
      prev.Power, curr.Power);
    AddIntIfChanged(changes, cardId, cardName, "Toughness",
      prev.Toughness, curr.Toughness);
    AddIntIfChanged(changes, cardId, cardName, "Damage",
      prev.Damage, curr.Damage);
    AddIntIfChanged(changes, cardId, cardName, "Loyalty",
      prev.Loyalty, curr.Loyalty);
    AddIntIfChanged(changes, cardId, cardName, "CurrentLevel",
      prev.CurrentLevel, curr.CurrentLevel);
    AddIntIfChanged(changes, cardId, cardName, "CurrentChapter",
      prev.CurrentChapter, curr.CurrentChapter);
    AddIntIfChanged(changes, cardId, cardName, "CurrentDungeonRoom",
      prev.CurrentDungeonRoom, curr.CurrentDungeonRoom);

    // Boolean state properties
    AddBoolIfChanged(changes, cardId, cardName, "IsTapped",
      prev.IsTapped, curr.IsTapped);
    AddBoolIfChanged(changes, cardId, cardName, "IsAttacking",
      prev.IsAttacking, curr.IsAttacking);
    AddBoolIfChanged(changes, cardId, cardName, "IsBlocking",
      prev.IsBlocking, curr.IsBlocking);
    AddBoolIfChanged(changes, cardId, cardName, "IsBlocked",
      prev.IsBlocked, curr.IsBlocked);
    AddBoolIfChanged(changes, cardId, cardName, "IsFlipped",
      prev.IsFlipped, curr.IsFlipped);
    AddBoolIfChanged(changes, cardId, cardName, "HasSummoningSickness",
      prev.HasSummoningSickness, curr.HasSummoningSickness);
    AddBoolIfChanged(changes, cardId, cardName, "IsActivatedAbility",
      prev.IsActivatedAbility, curr.IsActivatedAbility);
    AddBoolIfChanged(changes, cardId, cardName, "IsTriggeredAbility",
      prev.IsTriggeredAbility, curr.IsTriggeredAbility);
    AddBoolIfChanged(changes, cardId, cardName, "IsDelayedTrigger",
      prev.IsDelayedTrigger, curr.IsDelayedTrigger);
    AddBoolIfChanged(changes, cardId, cardName, "IsReplacementEffect",
      prev.IsReplacementEffect, curr.IsReplacementEffect);
    AddBoolIfChanged(changes, cardId, cardName, "IsCompanion",
      prev.IsCompanion, curr.IsCompanion);
    AddBoolIfChanged(changes, cardId, cardName, "IsEmblem",
      prev.IsEmblem, curr.IsEmblem);
    AddBoolIfChanged(changes, cardId, cardName, "IsYieldAbility",
      prev.IsYieldAbility, curr.IsYieldAbility);
    AddBoolIfChanged(changes, cardId, cardName, "HasAutoTargets",
      prev.HasAutoTargets, curr.HasAutoTargets);

    // Player references (by name)
    try
    {
      AddIfChanged(changes, cardId, cardName, "Owner",
        prev.Owner?.Name, curr.Owner?.Name);
    }
    catch { }
    try
    {
      AddIfChanged(changes, cardId, cardName, "Controller",
        prev.Controller?.Name, curr.Controller?.Name);
    }
    catch { }

    // Rules text (primarily for abilities on the stack)
    try
    {
      AddIfChanged(changes, cardId, cardName, "RulesText",
        prev.RulesText, curr.RulesText);
    }
    catch { }

    // Blue text (granted abilities text)
    try
    {
      AddIfChanged(changes, cardId, cardName, "BlueText",
        prev.BlueText, curr.BlueText);
    }
    catch { }

    // Type line
    try
    {
      AddIfChanged(changes, cardId, cardName, "TypeLine",
        prev.TypeLine, curr.TypeLine);
    }
    catch { }

    // Complex: Abilities
    try
    {
      var prevAbilities = prev.Abilities?
        .OrderBy(a => (int)a).Select(a => a.ToString()).ToList() ?? new();
      var currAbilities = curr.Abilities?
        .OrderBy(a => (int)a).Select(a => a.ToString()).ToList() ?? new();
      if (!prevAbilities.SequenceEqual(currAbilities))
      {
        changes.Add(new CardStateChangeModel
        {
          CardId = cardId,
          CardName = cardName,
          Property = "Abilities",
          OldValue = JsonSerializer.Serialize(prevAbilities, JsonSerializerOptions.Web),
          NewValue = JsonSerializer.Serialize(currAbilities, JsonSerializerOptions.Web)
        });
      }
    }
    catch { }

    // Complex: Counters (aggregate duplicates into {type: count} dictionary)
    try
    {
      var prevCounters = prev.Counters?
        .GroupBy(c => c)
        .ToDictionary(g => g.Key.ToString(), g => g.Count())
        ?? new Dictionary<string, int>();
      var currCounters = curr.Counters?
        .GroupBy(c => c)
        .ToDictionary(g => g.Key.ToString(), g => g.Count())
        ?? new Dictionary<string, int>();
      bool countersChanged = prevCounters.Count != currCounters.Count ||
        prevCounters.Any(kv =>
          !currCounters.TryGetValue(kv.Key, out var v) || v != kv.Value);
      if (countersChanged)
      {
        changes.Add(new CardStateChangeModel
        {
          CardId = cardId,
          CardName = cardName,
          Property = "Counters",
          OldValue = JsonSerializer.Serialize(prevCounters, JsonSerializerOptions.Web),
          NewValue = JsonSerializer.Serialize(currCounters, JsonSerializerOptions.Web)
        });
      }
    }
    catch { }

    // Complex: AttackingOrders (ordered list of blocker card IDs)
    try
    {
      var prevAttacking = prev.AttackingOrders?.Select(c => c.Id).ToList() ?? new();
      var currAttacking = curr.AttackingOrders?.Select(c => c.Id).ToList() ?? new();
      if (!prevAttacking.SequenceEqual(currAttacking))
      {
        changes.Add(new CardStateChangeModel
        {
          CardId = cardId,
          CardName = cardName,
          Property = "AttackingOrders",
          OldValue = JsonSerializer.Serialize(prevAttacking, JsonSerializerOptions.Web),
          NewValue = JsonSerializer.Serialize(currAttacking, JsonSerializerOptions.Web)
        });
      }
    }
    catch { }

    // Complex: BlockingOrders (ordered list of attacker card IDs being blocked)
    try
    {
      var prevBlocking = prev.BlockingOrders?.Select(c => c.Id).ToList() ?? new();
      var currBlocking = curr.BlockingOrders?.Select(c => c.Id).ToList() ?? new();
      if (!prevBlocking.SequenceEqual(currBlocking))
      {
        changes.Add(new CardStateChangeModel
        {
          CardId = cardId,
          CardName = cardName,
          Property = "BlockingOrders",
          OldValue = JsonSerializer.Serialize(prevBlocking, JsonSerializerOptions.Web),
          NewValue = JsonSerializer.Serialize(currBlocking, JsonSerializerOptions.Web)
        });
      }
    }
    catch { }

    // Complex: Associations (attachment/association map from MagicProperty IDs)
    try
    {
      object prevInner = Unbind(prev);
      object currInner = Unbind(curr);
      if (prevInner is GameCardPartial prevPartial
          && currInner is GameCardPartial currPartial)
      {
        var prevAssoc = prevPartial.ResolveAssociations();
        var currAssoc = currPartial.ResolveAssociations();
        var prevJson = JsonSerializer.Serialize(prevAssoc, JsonSerializerOptions.Web);
        var currJson = JsonSerializer.Serialize(currAssoc, JsonSerializerOptions.Web);
        if (prevJson != currJson)
        {
          changes.Add(new CardStateChangeModel
          {
            CardId = cardId,
            CardName = cardName,
            Property = "Associations",
            OldValue = prevJson,
            NewValue = currJson
          });
        }
      }
    }
    catch { }

    return changes;
  }

  /// <summary>
  /// Diffs player properties between two snapshots and produces change rows.
  /// </summary>
  private static List<PlayerStateChangeModel> DiffPlayer(
    GamePlayer prev, GamePlayer curr, int playerIndex)
  {
    var changes = new List<PlayerStateChangeModel>();
    string name = curr.Name;

    AddPlayerIntIfChanged(changes, playerIndex, name, "Life",
      prev.Life, curr.Life);
    AddPlayerIntIfChanged(changes, playerIndex, name, "HandCount",
      prev.HandCount, curr.HandCount);
    AddPlayerIntIfChanged(changes, playerIndex, name, "LibraryCount",
      prev.LibraryCount, curr.LibraryCount);
    AddPlayerIntIfChanged(changes, playerIndex, name, "GraveyardCount",
      prev.GraveyardCount, curr.GraveyardCount);
    AddPlayerBoolIfChanged(changes, playerIndex, name, "IsActivePlayer",
      prev.IsActivePlayer, curr.IsActivePlayer);
    AddPlayerBoolIfChanged(changes, playerIndex, name, "HasPriority",
      prev.HasPriority, curr.HasPriority);

    // Clock remaining (total seconds)
    double prevClock = prev.ChessClock.TotalSeconds;
    double currClock = curr.ChessClock.TotalSeconds;
    if (Math.Abs(prevClock - currClock) >= 0.5)
    {
      changes.Add(new PlayerStateChangeModel
      {
        PlayerIndex = playerIndex,
        PlayerName = name,
        Property = "ClockRemaining",
        OldValue = prevClock.ToString("F1"),
        NewValue = currClock.ToString("F1")
      });
    }

    // ManaPool
    try
    {
      var prevMana = GetManaPool(prev);
      var currMana = GetManaPool(curr);
      var prevManaByColor = NormalizeManaPool(prevMana);
      var currManaByColor = NormalizeManaPool(currMana);
      bool manaChanged = !ManaPoolEquals(prevManaByColor, currManaByColor);
      if (manaChanged)
      {
        changes.Add(new PlayerStateChangeModel
        {
          PlayerIndex = playerIndex,
          PlayerName = name,
          Property = "ManaPool",
          OldValue = SerializeManaPool(prevManaByColor),
          NewValue = SerializeManaPool(currManaByColor)
        });
      }
    }
    catch { }

    return changes;
  }

  private static List<Mana> GetManaPool(GamePlayer player)
  {
    try
    {
      if (Unbind(player) is GamePlayerPartial partial)
        return partial.ManaPool.ToList();
    }
    catch { }

    try
    {
      return player.ManaPool?.ToList() ?? new();
    }
    catch
    {
      return new();
    }
  }

  private static Dictionary<int, int> NormalizeManaPool(IEnumerable<Mana> mana)
  {
    var byColor = new Dictionary<int, int>();
    foreach (var m in mana)
    {
      int color = m.ID;
      if (!byColor.TryGetValue(color, out var amount)) amount = 0;
      byColor[color] = amount + m.Amount;
    }

    return byColor;
  }

  private static bool ManaPoolEquals(
    Dictionary<int, int> left,
    Dictionary<int, int> right)
  {
    if (left.Count != right.Count) return false;
    foreach (var (color, amount) in left)
    {
      if (!right.TryGetValue(color, out var otherAmount) || otherAmount != amount)
        return false;
    }

    return true;
  }

  private static string SerializeManaPool(Dictionary<int, int> manaByColor) =>
    JsonSerializer.Serialize(
      manaByColor
        .OrderBy(kvp => kvp.Key)
        .Select(kvp => new
        {
          Symbol = Mana.ToSymbol((MagicColors)kvp.Key),
          Amount = kvp.Value
        }),
      JsonSerializerOptions.Web);

  //
  // Diff helpers
  //

  private static void AddIfChanged(
    List<CardStateChangeModel> changes, int cardId, string cardName,
    string property, string? oldValue, string? newValue)
  {
    if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
    {
      changes.Add(new CardStateChangeModel
      {
        CardId = cardId,
        CardName = cardName,
        Property = property,
        OldValue = oldValue,
        NewValue = newValue ?? ""
      });
    }
  }

  private static void AddIntIfChanged(
    List<CardStateChangeModel> changes, int cardId, string cardName,
    string property, int oldValue, int newValue)
  {
    if (oldValue != newValue)
    {
      changes.Add(new CardStateChangeModel
      {
        CardId = cardId,
        CardName = cardName,
        Property = property,
        OldValue = oldValue.ToString(),
        NewValue = newValue.ToString()
      });
    }
  }

  private static void AddBoolIfChanged(
    List<CardStateChangeModel> changes, int cardId, string cardName,
    string property, bool oldValue, bool newValue)
  {
    if (oldValue != newValue)
    {
      changes.Add(new CardStateChangeModel
      {
        CardId = cardId,
        CardName = cardName,
        Property = property,
        OldValue = oldValue.ToString(),
        NewValue = newValue.ToString()
      });
    }
  }

  private static void AddPlayerIntIfChanged(
    List<PlayerStateChangeModel> changes, int playerIndex, string name,
    string property, int oldValue, int newValue)
  {
    if (oldValue != newValue)
    {
      changes.Add(new PlayerStateChangeModel
      {
        PlayerIndex = playerIndex,
        PlayerName = name,
        Property = property,
        OldValue = oldValue.ToString(),
        NewValue = newValue.ToString()
      });
    }
  }

  private static void AddPlayerBoolIfChanged(
    List<PlayerStateChangeModel> changes, int playerIndex, string name,
    string property, bool oldValue, bool newValue)
  {
    if (oldValue != newValue)
    {
      changes.Add(new PlayerStateChangeModel
      {
        PlayerIndex = playerIndex,
        PlayerName = name,
        Property = property,
        OldValue = oldValue.ToString(),
        NewValue = newValue.ToString()
      });
    }
  }
}
