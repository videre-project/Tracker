/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using MTGOSDK.API.Interface.Controls;
using MTGOSDK.API.Chat;
using MTGOSDK.API.Play.Games;
using MTGOSDK.API.Play.Games.Processors;
using MTGOSDK.API.Play.Games.Processors.Events;
using MTGOSDK.API.Play.Games.Processors.Partials;
using MTGOSDK.API.Play.Games.Processors.Snapshots;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Serialization;
using static MTGOSDK.Core.Reflection.DLRWrapper;

using MTGOTracker.Database.Models.Events;


namespace MTGOTracker.Services.MTGO.Events;

/// <summary>
/// Publishes live game logs and optionally persists structured replay states.
/// Completed SDK publications pair gameplay changes with captured
/// actions and client selection state.
/// </summary>
public class GameTracker : IDisposable
{
  private readonly Game _game;
  private readonly int _gameId;
  private readonly BlockingCollection<GameLogEntry> _eventLog;
  private readonly EventDatabaseWriter _dbWriter;
  private readonly MTGOContext _context;
  private readonly bool _recordReplay;

  private readonly HashSet<int> _seenCardIds = new();
  private readonly HashSet<int> _seenPlayerIndices = new();
  private int? _lastPublishedNonce;
  private int _lastTurnNumber;
  private string? _lastPhase;

  // Gameplay events accumulate by snapshot until the SDK publishes its
  // completed state. Later local captures use that snapshot without its deltas.
  private readonly object _flushLock = new();
  private readonly Dictionary<GameStateSnapshot, PendingState> _pendingStates =
    new(ReferenceEqualityComparer.Instance);

  private sealed class PendingState(GameStateSnapshot snapshot)
  {
    public GameStateSnapshot Snapshot { get; } = snapshot;
    public HashSet<int> ZoneTransferCardIds { get; } = new();
    public List<GameCardModel> Cards { get; } = new();
    public List<GamePlayerModel> Players { get; } = new();
    public List<ZoneTransferModel> ZoneTransfers { get; } = new();
    public List<CardStateChangeModel> CardChanges { get; } = new();
    public List<PlayerStateChangeModel> PlayerChanges { get; } = new();
    public List<GameAction> Actions { get; } = new();
    public List<GameLogModel> Logs { get; } = new();
    public List<GameLogEntry> Events { get; } = new();
  }

  // Background writer — decouples processor thread from DB I/O
  private readonly BlockingCollection<object> _flushQueue = new();
  private readonly Thread _writerThread;

  /// <summary>
  /// Queued for serialization and SSE emission on the writer
  /// thread when an action is finalized, and reused when its state is persisted.
  /// </summary>
  private sealed record ActionEmit(
    int GameId,
    GameAction Action,
    int AssignedNonce,
    DateTime SnapshotTimestamp);

  // A queue barrier lets API callers wait for all preceding database writes.
  private sealed record FlushComplete(TaskCompletionSource Signal);

  // Writer-thread-local cache of action models serialized via ActionEmit.
  // Keyed by GameAction reference identity to avoid serializing it twice.
  private readonly Dictionary<GameAction, GameActionModel>
    _preSerializedActions = new();

  // Writer-thread state: an SDK UI update can leave all recorded values unchanged.
  private GameStateSnapshot? _lastRecordedSnapshot;
  private IReadOnlyList<PromptOptionData>? _lastRecordedPromptOptions;
  private GameInteractionData? _lastRecordedInteraction;
  private int? _lastRecordedNonce;
  private int? _lastSkippedNonce;

  private sealed record FlushBatch(
    PendingState State,
    GameStateChangedEventArgs? Update,
    DateTime CapturedAt);

  /// <summary>
  /// Initializes a new instance of the <see cref="GameTracker"/> class.
  /// </summary>
  /// <param name="game">The game to track.</param>
  /// <param name="eventLog">The event log for SSE notifications.</param>
  /// <param name="dbWriter">The database writer.</param>
  /// <param name="context">The host's initialized game catalog.</param>
  /// <param name="recordReplay">Persist replay states while continuing to publish live logs.</param>
  public GameTracker(
    Game game,
    BlockingCollection<GameLogEntry> eventLog,
    EventDatabaseWriter dbWriter,
    MTGOContext context,
    bool recordReplay = true)
  {
    _game = game;
    _gameId = game.Id;
    _eventLog = eventLog;
    _dbWriter = dbWriter;
    _context = context;
    _recordReplay = recordReplay;
    // Reattachment can finalize an action before its first new publication.
    _lastPublishedNonce = _dbWriter.GetLastRecordedNonce(_gameId);

    // Start the background writer thread
    _writerThread = new Thread(ProcessFlushQueue)
    {
      Name = $"GameTracker-Writer-{_gameId}",
      IsBackground = true
    };
    _writerThread.Start();

    Log.Debug("Configuring game {Id} GameProcessor events", _gameId);

    // Register gameplay processors before subscribing to completed publications.
    game.OnRevealedCards += OnRevealedCards;
    game.OnZoneChanged += OnZoneChanged;
    game.OnCardChanged += OnCardChanged;
    game.OnPlayerChanged += OnPlayerChanged;
    game.OnActionFinalized += OnActionFinalized;
    game.OnLogMessage += OnLogMessage;
    game.OnDamageAssignment += OnDamageAssignment;
    game.OnStateChanged += OnGameStateChanged;

    // Still need direct Game events for lifecycle
    game.GameStatusChanged += OnGameStatusChanged;

    // All processors are registered — allow the drain loop to start
    // processing queued hooks. Before this call, hooks are buffered but
    // not drained, ensuring no snapshots are processed with an incomplete
    // processor set.
    GameProcessor.ForGame(game).Ready();

    Log.Debug("Game {Id} GameProcessor events configured", _gameId);
  }

  /// <summary>
  /// Waits for completed SDK updates to reach the database before API queries.
  /// Incomplete gameplay buffers are retained until their publication arrives.
  /// </summary>
  internal void ForceFlush()
  {
    if (_disposed) return;

    // Let the SDK finish pending gameplay before placing the writer barrier.
    GameProcessor.ForGame(_game).WaitForPendingProcessing(TimeSpan.FromMilliseconds(200));

    var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    lock (_flushLock)
    {
      if (_disposed) return;
      _flushQueue.Add(new FlushComplete(signal));
    }
    if (!signal.Task.Wait(TimeSpan.FromSeconds(5)))
      throw new TimeoutException("Timed out waiting for game state persistence.");
  }

  private volatile bool _disposed = false;

  public void Dispose()
  {
    if (_disposed) return;

    // Drain any remaining hooks before shutting down event handlers.
    // This also runs when the service shuts down before a game finishes.
    GameProcessor.ForGame(_game).WaitForPendingProcessing(TimeSpan.FromSeconds(30));

    lock (_flushLock)
    {
      if (_disposed) return;
      _disposed = true;
      // Final messages can contain gameplay changes without a prompt.
      foreach (var pending in _pendingStates.Values)
        _flushQueue.Add(new FlushBatch(pending, null, pending.Snapshot.ClientTimestamp));
      _pendingStates.Clear();
      _flushQueue.CompleteAdding();
    }
    _writerThread.Join();

    // Clears EventProxy, EventHookWrapper, ProcessorEvent handlers and
    // deactivates the GameProcessor from the static routing table.
    _context.RemoveGame(_gameId);
    _game.ClearEvents();

    GC.SuppressFinalize(this);
  }

  private PendingState EnsureGameState(GameEventArgs args)
  {
    if (_pendingStates.TryGetValue(args.Snapshot, out var pending))
      return pending;

    pending = new PendingState(args.Snapshot);
    _pendingStates.Add(args.Snapshot, pending);
    return pending;
  }

  /// <summary>
  /// Serializes completed SDK updates for live logs and optional persistence, in order.
  /// </summary>
  private void ProcessFlushQueue()
  {
    foreach (var item in _flushQueue.GetConsumingEnumerable())
    {
      if (item is FlushComplete fc)
      {
        fc.Signal.TrySetResult();
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
          // A skipped UI update has no replay row; retain the preceding state's nonce.
          int actionNonce = ae.AssignedNonce == _lastSkippedNonce
            ? _lastRecordedNonce ?? ae.AssignedNonce : ae.AssignedNonce;
          _eventLog.Add(new GameLogEntry(
              ae.GameId, actionTs, GameLogType.GameAction,
              model.Data, actionNonce));
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
      var state = batch.State;
      try
      {
        // Reuse pre-serialized action models from ActionEmit processing.
        // Fall back to serializing here if the ActionEmit failed.
        var actionModels = new List<GameActionModel>(state.Actions.Count);
        foreach (var action in state.Actions)
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
              _gameId);
          }
        }

        var update = batch.Update;
        var promptOptions = update is null ? null : CapturePromptOptions(update.Options);
        var interaction = update is null ? null : GameInteractionData.FromSnapshot(update.Interaction);
        if (update is not null && ShouldSkipRecording(state, promptOptions, interaction))
        {
          _lastSkippedNonce = update.Nonce;
          continue;
        }
        string? interactionJson = interaction is null ? null
          : JsonSerializer.Serialize(interaction, JsonSerializerOptions.Web);

        // Live notifications use the same SDK publication even when replay storage is disabled.
        if (_recordReplay && _dbWriter.FlushStateData(
          _gameId,
          state.Snapshot,
          state.Cards,
          state.Players,
          state.ZoneTransfers,
          state.CardChanges,
          state.PlayerChanges,
          actionModels,
          state.Logs,
          promptOptions: promptOptions,
          nonce: update?.Nonce,
          interaction: interaction,
          capturedAt: batch.CapturedAt) < 0) continue;
        int nonce = update?.Nonce ?? state.Snapshot.Nonce;
        _lastRecordedSnapshot = state.Snapshot;
        _lastRecordedPromptOptions = promptOptions;
        _lastRecordedInteraction = interaction;
        _lastRecordedNonce = nonce;
        _lastSkippedNonce = null;
        foreach (var notification in state.Events)
        {
          var entry = notification;
          entry.Nonce = nonce;
          _eventLog.Add(entry);
        }
        // Reattachment can replace initial observations with reconciled deltas.
        // When recording, publish the final change lists after their transaction commits.
        if (state.CardChanges.Count > 0)
          _eventLog.Add(new GameLogEntry(_gameId,
            state.Snapshot.ClientTimestamp, GameLogType.CardChange,
            JsonSerializer.Serialize(state.CardChanges.Select(change => new CardChangeData
            {
              CardId = change.CardId, CardName = change.CardName,
              Property = change.Property, OldValue = change.OldValue,
              NewValue = change.NewValue
            }), JsonSerializerOptions.Web), nonce));
        if (state.PlayerChanges.Count > 0)
          _eventLog.Add(new GameLogEntry(_gameId,
            state.Snapshot.ClientTimestamp, GameLogType.PlayerChange,
            JsonSerializer.Serialize(state.PlayerChanges.Select(change => new PlayerChangeData
            {
              PlayerIndex = change.PlayerIndex, PlayerName = change.PlayerName,
              Property = change.Property, OldValue = change.OldValue,
              NewValue = change.NewValue
            }), JsonSerializerOptions.Web), nonce));
        // Local interaction captures also notify viewers when no server log changed.
        if (update is not null)
          _eventLog.Add(new GameLogEntry(_gameId, batch.CapturedAt,
            GameLogType.Interaction, interactionJson!, update.Nonce));

      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error writing game state batch for game {Id}",
          _gameId);
      }
    }
  }

  private bool ShouldSkipRecording(PendingState state, IReadOnlyList<PromptOptionData>? promptOptions, GameInteractionData? interaction)
  {
    var previous = _lastRecordedSnapshot;
    var current = state.Snapshot;
    return previous is not null
      && state.Cards.Count == 0 && state.Players.Count == 0
      && state.ZoneTransfers.Count == 0 && state.CardChanges.Count == 0
      && state.PlayerChanges.Count == 0 && state.Actions.Count == 0
      && state.Logs.Count == 0 && state.Events.Count == 0
      && previous.Nonce == current.Nonce
      && previous.TurnNumber == current.TurnNumber
      && previous.CurrentPhase == current.CurrentPhase
      && previous.PromptedPlayer == current.PromptedPlayer
      && previous.PromptText == current.PromptText
      && SamePromptOptions(_lastRecordedPromptOptions, promptOptions)
      && _lastRecordedInteraction is not null && interaction is not null
      && _lastRecordedInteraction.HasSameDecision(interaction);
  }

  private static bool SamePromptOptions(IReadOnlyList<PromptOptionData>? left, IReadOnlyList<PromptOptionData>? right) =>
    left is not null && right is not null
      ? left.Count == right.Count && left.Zip(right).All(pair => pair.First.HasSameValue(pair.Second))
      : left is null && right is null;

  //
  // GameProcessor event handlers
  //

  private void OnRevealedCards(RevealedCardsEventArgs e)
  {
    lock (_flushLock)
    {
      if (_disposed) return;
      try
      {
        var pending = EnsureGameState(e);

        var newTransfers = new List<ZoneTransferModel>();

        foreach (var card in e.Arrived)
        {
          pending.ZoneTransferCardIds.Add(card.Id);
          EnsureCardTracked(pending, card, e.Snapshot, initialZoneOverride: "Revealed");

          var zt = new ZoneTransferModel
          {
            CardId = card.Id,
            CardName = card.Name,
            FromZone = card.Zone?.Name,
            ToZone = "Revealed",
            SourceId = null,
            Type = "Arrived"
          };
          pending.ZoneTransfers.Add(zt);
          newTransfers.Add(zt);
        }

        foreach (var card in e.Departed)
        {
          pending.ZoneTransferCardIds.Add(card.Id);

          var zt = new ZoneTransferModel
          {
            CardId = card.Id,
            CardName = card.Name,
            FromZone = "Revealed",
            ToZone = card.Zone?.Name,
            SourceId = null,
            Type = "Departed"
          };
          pending.ZoneTransfers.Add(zt);
          newTransfers.Add(zt);
        }

        if (newTransfers.Count > 0)
        {
          pending.Events.Add(new GameLogEntry(_gameId,
            e.Snapshot.ClientTimestamp, GameLogType.Reveal,
            JsonSerializer.Serialize(newTransfers.Select(zt => new ZoneTransferData
            {
              CardId = zt.CardId, CardName = zt.CardName,
              FromZone = zt.FromZone, ToZone = zt.ToZone,
              SourceId = zt.SourceId, Type = zt.Type
            }), JsonSerializerOptions.Web), e.Nonce));
        }

        Log.Trace("[Game {Id}] {Count} revealed zone transfers (nonce {Nonce})",
          _gameId, newTransfers.Count, e.Nonce);
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error processing revealed cards for game {Id}", _gameId);
      }
    }
  }

  private void OnZoneChanged(ZoneChangeEventArgs e)
  {
    lock (_flushLock)
    {
      if (_disposed) return;
      try
      {
        var pending = EnsureGameState(e);

        var newTransfers = new List<ZoneTransferModel>();

        // Arrived = new card entering the board
        foreach (var card in e.Arrived)
        {
          pending.ZoneTransferCardIds.Add(card.Id);
          EnsureCardTracked(pending, card, e.Snapshot);

          var zt = new ZoneTransferModel
          {
            CardId = card.Id,
            CardName = card.Name,
            FromZone = null,
            ToZone = card.Zone?.Name,
            SourceId = card.SourceId > 0 ? card.SourceId : null,
            Type = "Arrived"
          };
          pending.ZoneTransfers.Add(zt);
          newTransfers.Add(zt);
        }

        // Departed = card leaving the board
        foreach (var card in e.Departed)
        {
          pending.ZoneTransferCardIds.Add(card.Id);

          var zt = new ZoneTransferModel
          {
            CardId = card.Id,
            CardName = card.Name,
            FromZone = card.Zone?.Name,
            ToZone = null,
            SourceId = null,
            Type = "Departed"
          };
          pending.ZoneTransfers.Add(zt);
          newTransfers.Add(zt);
        }

        // Moved = card changed zones (gets a new ThingID)
        foreach (var (from, to) in e.Moved)
        {
          pending.ZoneTransferCardIds.Add(to.Id);
          EnsureCardTracked(pending, to, e.Snapshot);

          var zt = new ZoneTransferModel
          {
            CardId = to.Id,
            CardName = to.Name,
            FromZone = from.Zone?.Name,
            ToZone = to.Zone?.Name,
            SourceId = from.Id,
            Type = "Moved"
          };
          pending.ZoneTransfers.Add(zt);
          newTransfers.Add(zt);
        }

        // Buffer notifications with the completed state.
        if (newTransfers.Count > 0)
        {
          pending.Events.Add(new GameLogEntry(_gameId,
            e.Snapshot.ClientTimestamp, GameLogType.ZoneChange,
            JsonSerializer.Serialize(newTransfers.Select(zt => new ZoneTransferData
            {
              CardId = zt.CardId, CardName = zt.CardName,
              FromZone = zt.FromZone, ToZone = zt.ToZone,
              SourceId = zt.SourceId, Type = zt.Type
            }), JsonSerializerOptions.Web), e.Nonce));
        }

        Log.Trace("[Game {Id}] {Count} zone transfers (nonce {Nonce})",
          _gameId, pending.ZoneTransfers.Count, e.Nonce);
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error processing zone change for game {Id}", _gameId);
      }
    }
  }

  private void OnCardChanged(CardChangedEventArgs e)
  {
    lock (_flushLock)
    {
      if (_disposed) return;
      try
      {
        var pending = EnsureGameState(e);
        EnsureCardTracked(pending, e.Current, e.Snapshot);

        bool hasZoneTransfer = pending.ZoneTransferCardIds.Contains(e.Current.Id);
        var changes = DiffCard(e.Previous, e.Current, hasZoneTransfer);

        if (changes.Count > 0)
        {
          pending.CardChanges.AddRange(changes);

          Log.Trace("[Game {Id}] {Count} card changes for {Card} (nonce {Nonce})",
            _gameId, changes.Count, e.Current.Name, e.Nonce);
        }
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error processing card change for game {Id}", _gameId);
      }
    }
  }

  private void OnPlayerChanged(PlayerChangedEventArgs e)
  {
    lock (_flushLock)
    {
      if (_disposed) return;
      try
      {
        var pending = EnsureGameState(e);

        // Track new player (PropertyChangeProcessor emits prev==curr for new players)
        bool isNewPlayer = _seenPlayerIndices.Add(e.PlayerIndex);
        if (isNewPlayer)
        {
          pending.Players.Add(
            EventDatabaseWriter.BuildGamePlayerModel(
              _gameId, e.Current, e.PlayerIndex));
          Log.Trace("[Game {Id}] New player {Name} (index {Index})",
            _gameId, e.Current.Name, e.PlayerIndex);
        }

        var changes = isNewPlayer
          ? BuildInitialPlayerChanges(e.Current, e.PlayerIndex)
          : DiffPlayer(e.Previous, e.Current, e.PlayerIndex);
        if (changes.Count > 0)
        {
          pending.PlayerChanges.AddRange(changes);

          Log.Trace("[Game {Id}] {Count} player changes for {Name} (nonce {Nonce})",
            _gameId, changes.Count, e.Current.Name, e.Nonce);
        }
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error processing player change for game {Id}", _gameId);
      }
    }
  }

  private void OnActionFinalized(ActionFinalizedEventArgs e)
  {
    lock (_flushLock)
    {
      if (_disposed) return;
      try
      {
        var pending = EnsureGameState(e);

        // Buffer the raw action for DB persistence in the next flush batch.
        pending.Actions.Add(e.Action);

        // Serialize off the processor thread and reuse the model for persistence.
        // The action was performed in the last published decision state.
        var assignedNonce = _lastPublishedNonce ?? e.Nonce;
        _flushQueue.Add(new ActionEmit(
            _gameId, e.Action, assignedNonce, e.Snapshot.ClientTimestamp));

        Log.Trace("[Game {Id}] Action finalized: {Name} (nonce {Nonce})",
          _gameId, e.Action.Name, e.Nonce);
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error processing action for game {Id}", _gameId);
      }
    }
  }

  private void OnLogMessage(LogMessageCorrelatedEventArgs e)
  {
    lock (_flushLock)
    {
      if (_disposed) return;

      try
      {
        var pending = EnsureGameState(e);

        // The SDK preserves the original channel timestamp during backfill.
        var timestamp = e.MessageClientTimestamp;
        var messageText = ChatTextNormalizer.Normalize(e.Message.Text);

        pending.Logs.Add(new GameLogModel
        {
          Timestamp = timestamp,
          GameLogType = "LogMessage",
          Data = messageText
        });
        Log.Trace("[Game {Id}] Log: {Text} (nonce {Nonce})",
          _gameId, messageText, e.Nonce);

        // Buffer the notification with the completed state.
        pending.Events.Add(new GameLogEntry(
          _gameId,
          timestamp,
          GameLogType.LogMessage,
          messageText,
          e.Nonce));
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error processing log message for game {Id}", _gameId);
      }
    }
  }

  private void OnDamageAssignment(DamageAssignmentEventArgs e)
  {
    lock (_flushLock)
    {
      if (_disposed) return;

      try
      {
        var pending = EnsureGameState(e);

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

        pending.Logs.Add(new GameLogModel
        {
          Timestamp = e.Snapshot.ClientTimestamp,
          GameLogType = "DamageAssignment",
          Data = data
        });

        pending.Events.Add(new GameLogEntry(
          _gameId,
          e.Snapshot.ClientTimestamp,
          GameLogType.DamageAssignment,
          data,
          e.Nonce));

        Log.Trace("[Game {Id}] {Count} damage assignments (nonce {Nonce})",
          _gameId, e.Assignments.Count, e.Nonce);
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Error processing damage assignment for game {Id}", _gameId);
      }
    }
  }

  private void OnGameStateChanged(GameStateChangedEventArgs update)
  {
    lock (_flushLock)
    {
      if (_disposed) return;
      var pending = EnsureGameState(update);
      _pendingStates.Remove(update.Snapshot);
      string phase = update.Snapshot.CurrentPhase.ToString();
      if (update.Snapshot.TurnNumber != _lastTurnNumber || phase != _lastPhase)
      {
        pending.Events.Insert(0, new GameLogEntry(_gameId,
          update.Snapshot.ClientTimestamp, GameLogType.GameState,
          JsonSerializer.Serialize(new GameStateData
          {
            Turn = update.Snapshot.TurnNumber,
            Phase = phase,
            PreviousTurn = _lastTurnNumber,
            PreviousPhase = _lastPhase
          }, JsonSerializerOptions.Web), update.Nonce));
        _lastTurnNumber = update.Snapshot.TurnNumber;
        _lastPhase = phase;
      }
      _lastPublishedNonce = update.Nonce;
      _flushQueue.Add(new FlushBatch(pending, update, DateTime.UtcNow));
    }
  }

  /// <summary>
  /// Captures typed prompt options, retaining native action-specific JSON fields.
  /// </summary>
  private static IReadOnlyList<PromptOptionData> CapturePromptOptions(PromptOptionsSnapshot captured)
  {
    var options = new List<PromptOptionData>();
    foreach (var kvp in captured.Options)
    {
      foreach (IPromptOption option in kvp.Value)
      {
        try
        {
          if (option is GameAction action)
          {
            options.Add(PromptOptionData.FromAction(action));
          }
          else if (option is PromptControl control)
          {
            options.Add(new PromptOptionData
            {
              Type = control.Type,
              Name = control.Name,
              Enabled = control.Enabled,
              ControlId = control.Id,
              ButtonType = control.ButtonType,
              Color = control.Color,
              PromptNonce = captured.PromptNonce,
              ClientRevision = captured.ClientRevision.ToString(CultureInfo.InvariantCulture)
            });
          }
        }
        catch (Exception ex)
        {
          Log.Error(ex, "[PromptOptions] serialization threw for {Type}",
            kvp.Key);
        }
      }
    }

    return options;
  }

  //
  // Direct Game event handlers (lifecycle)
  //
  private void OnGameStatusChanged()
  {
    if (_game.Status == GameStatus.Finished)
    {
      // Result subscriptions belong to the service and survive recorder teardown.
      // Dispose drains the processor and writer without delaying result delivery.
      _ = Task.Run(Dispose);
    }
  }

  //
  // Card tracking helpers
  //

  private void EnsureCardTracked(
    PendingState pending, GameCard card, GameStateSnapshot snapshot,
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

    pending.Cards.Add(
      EventDatabaseWriter.BuildGameCardModel(
        _gameId, card, ownerIdx, ctrlIdx, initialZoneOverride));
    Log.Trace("[Game {Id}] New card {Name} (id {CardId}, zone {Zone})",
      _gameId, card.Name, card.Id, card.Zone?.Name);

    // Emit initial card changes for non-default properties that the frontend
    // needs but can't derive from the card model (e.g. associations set at
    // creation time, which DiffCard would never see change).
    EmitInitialCardChanges(pending, card);
  }

  /// <summary>
  /// Emits card change records for properties that are non-default when a card
  /// first appears. Without this, properties like Associations that are set at
  /// card creation and never change would be invisible to the frontend.
  /// </summary>
  private void EmitInitialCardChanges(PendingState pending, GameCard card)
  {
    try
    {
      var assoc = EventDatabaseWriter.BuildCardAssociations(card);
      if (assoc.Count > 0)
      {
        pending.CardChanges.Add(new CardStateChangeModel
        {
          CardId = card.Id,
          CardName = card.Name,
          Property = "Associations",
          OldValue = "{}",
          NewValue = JsonSerializer.Serialize(assoc, JsonSerializerOptions.Web)
        });
      }
    }
    catch { }

    // Emit initial typeline
    try
    {
      var typeLine = card.TypeLine;
      if (!string.IsNullOrEmpty(typeLine))
      {
        pending.CardChanges.Add(new CardStateChangeModel
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
        _gameId, card.Name, card.Id);
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

    // Complex: Associations, including the SDK's display attachment parent.
    try
    {
      var prevAssoc = EventDatabaseWriter.BuildCardAssociations(prev);
      var currAssoc = EventDatabaseWriter.BuildCardAssociations(curr);
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
    catch { }

    return changes;
  }

  /// <summary>
  /// Diffs player properties between two snapshots and produces change rows.
  /// </summary>
  private static List<PlayerStateChangeModel> BuildInitialPlayerChanges(
    GamePlayer player,
    int playerIndex)
  {
    var changes = new List<PlayerStateChangeModel>();

    if (player.HasPriority)
    {
      AddPlayerBoolIfChanged(changes, playerIndex, player.Name, "HasPriority",
        false, true);
    }

    AddPlayerCountersIfChanged(changes, playerIndex, player.Name,
      new Dictionary<string, int>(), player.Counters);
    return changes;
  }

  private static List<PlayerStateChangeModel> DiffPlayer(
    GamePlayer prev,
    GamePlayer curr,
    int playerIndex)
  {
    var changes = new List<PlayerStateChangeModel>();
    string playerName = curr.Name;

    AddPlayerIntIfChanged(changes, playerIndex, playerName, "Life",
      prev.Life, curr.Life);
    AddPlayerIntIfChanged(changes, playerIndex, playerName, "HandCount",
      prev.HandCount, curr.HandCount);
    AddPlayerIntIfChanged(changes, playerIndex, playerName, "LibraryCount",
      prev.LibraryCount, curr.LibraryCount);
    AddPlayerIntIfChanged(changes, playerIndex, playerName, "GraveyardCount",
      prev.GraveyardCount, curr.GraveyardCount);
    AddPlayerBoolIfChanged(changes, playerIndex, playerName, "IsActivePlayer",
      prev.IsActivePlayer, curr.IsActivePlayer);
    AddPlayerBoolIfChanged(changes, playerIndex, playerName, "HasPriority",
      prev.HasPriority, curr.HasPriority);
    AddPlayerCountersIfChanged(changes, playerIndex, playerName,
      prev.Counters, curr.Counters);

    double prevClock = prev.ChessClock.TotalSeconds;
    double currClock = curr.ChessClock.TotalSeconds;
    if (Math.Abs(prevClock - currClock) >= 0.5)
    {
      changes.Add(new PlayerStateChangeModel
      {
        PlayerIndex = playerIndex,
        PlayerName = playerName,
        Property = "ClockRemaining",
        OldValue = prevClock.ToString("F1", CultureInfo.InvariantCulture),
        NewValue = currClock.ToString("F1", CultureInfo.InvariantCulture)
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
          PlayerName = playerName,
          Property = "ManaPool",
          OldValue = SerializeManaPool(prevManaByColor),
          NewValue = SerializeManaPool(currManaByColor)
        });
      }
    }
    catch { }

    return changes;
  }

  private static void AddPlayerCountersIfChanged(
    List<PlayerStateChangeModel> changes,
    int playerIndex,
    string playerName,
    IReadOnlyDictionary<string, int> oldValue,
    IReadOnlyDictionary<string, int> newValue)
  {
    string oldJson = JsonSerializer.Serialize(oldValue, JsonSerializerOptions.Web);
    string newJson = JsonSerializer.Serialize(newValue, JsonSerializerOptions.Web);
    if (oldJson == newJson)
      return;

    changes.Add(new PlayerStateChangeModel
    {
      PlayerIndex = playerIndex,
      PlayerName = playerName,
      Property = "Counters",
      OldValue = oldJson,
      NewValue = newJson
    });
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
