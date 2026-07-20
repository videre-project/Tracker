/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MTGOSDK.API.Chat;
using MTGOSDK.API.Collection;
using MTGOSDK.API.Trade;
using MTGOSDK.API.Trade.Enums;
using MTGOSDK.Core.Logging;

using Tracker.Database;
using Tracker.Database.Models.Trades;
using Tracker.Services.MTGO.Collection;
using Tracker.Services.MTGO.Trade;
using static Tracker.Services.DatabaseService;


namespace Tracker.Services.MTGO;

public static class TradeAPIService
{
  public static IHostApplicationBuilder RegisterTradeService(
    this IHostApplicationBuilder builder)
  {
    builder.Services.AddSingleton<TradeHistoryWriter>();
    builder.Services.AddHostedService<TradeService>();
    return builder;
  }

  public sealed class TradeService(
    IClientAPIProvider clientProvider,
    IServiceScopeFactory scopeFactory,
    DatabaseReadiness<TradeContext> databaseReadiness,
    TradeHistoryWriter historyWriter,
    ICollectionStateFeed collectionStateFeed)
      : BackgroundService
  {
    private static readonly TimeSpan s_collectionMaximumDelay =
      TimeSpan.FromSeconds(5);

    private readonly Dictionary<Guid, ActiveEscrow> _active = [];
    private readonly HashSet<Guid> _discardedTokens = [];
    private readonly Dictionary<string, Guid> _chatChannels =
      new(StringComparer.OrdinalIgnoreCase);
    private Channel<TradeSignal>? _signals;
    private bool _hooksInstalled;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      await databaseReadiness.WaitAsync(stoppingToken);
      Log.Information("Trade API background service started.");

      while (!stoppingToken.IsCancellationRequested)
      {
        UserIdentity? identity = null;
        using var sessionCts =
          CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        try
        {
          await clientProvider.WaitForClientReadyAsync(stoppingToken);
          identity = clientProvider.CurrentUser
            ?? throw new InvalidOperationException(
              "Client is ready without an authoritative user identity.");

          await UpsertAccountAsync(identity, sessionCts.Token);
          await DeleteUnassignedClosedEscrowsAsync(
            identity.Id,
            sessionCts.Token);
          await EndSessionAsync(identity.Id, sessionCts.Token);
          _signals = System.Threading.Channels.Channel.CreateUnbounded<TradeSignal>(new()
          {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
          });

          InstallHooks();
          EnqueueOpenTrades();

          Task processor = ProcessSignalsAsync(identity.Id, sessionCts.Token);
          await clientProvider.WaitForClientDisconnectAsync(stoppingToken);

          RemoveHooks();
          _signals.Writer.TryComplete();
          await processor;
          sessionCts.Cancel();
          await EndSessionAsync(identity.Id, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
          break;
        }
        catch (OperationCanceledException)
        {
          // A client-scoped operation was canceled during disconnect.
        }
        catch (Exception ex)
        {
          Log.Error(ex, "Trade service session failed: {Message}", ex.Message);
          try
          {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
          }
          catch (OperationCanceledException)
          {
            break;
          }
        }
        finally
        {
          sessionCts.Cancel();
          RemoveHooks();
          _signals?.Writer.TryComplete();
          _signals = null;
          _active.Clear();
          _discardedTokens.Clear();
          _chatChannels.Clear();

          if (identity != null)
          {
            try
            {
              using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
              await EndSessionAsync(identity.Id, cleanupCts.Token);
            }
            catch (Exception ex)
            {
              Log.Error(ex, "Failed to close interrupted trade session.");
            }
          }
        }
      }
    }

    private async Task UpsertAccountAsync(
      UserIdentity identity,
      CancellationToken cancellationToken)
    {
      await using var scope = scopeFactory.CreateAsyncScope();
      var context = scope.ServiceProvider.GetRequiredService<TradeContext>();
      await historyWriter.UpsertAccountAsync(context, identity, cancellationToken);
    }

    private async Task EndSessionAsync(
      int accountId,
      CancellationToken cancellationToken)
    {
      await using var scope = scopeFactory.CreateAsyncScope();
      var context = scope.ServiceProvider.GetRequiredService<TradeContext>();
      await historyWriter.EndSessionAsync(context, accountId, cancellationToken);
    }

    private async Task DeleteUnassignedClosedEscrowsAsync(
      int accountId,
      CancellationToken cancellationToken)
    {
      await using var scope = scopeFactory.CreateAsyncScope();
      var context = scope.ServiceProvider.GetRequiredService<TradeContext>();
      await historyWriter.DeleteUnassignedClosedAsync(
        context,
        accountId,
        cancellationToken);
    }

    private async Task ProcessSignalsAsync(
      int accountId,
      CancellationToken cancellationToken)
    {
      await foreach (TradeSignal signal in
        _signals!.Reader.ReadAllAsync(cancellationToken))
      {
        try
        {
          switch (signal)
          {
            case EscrowSignal escrow:
              await ApplyEscrowSignalAsync(accountId, escrow, cancellationToken);
              break;
            case ItemSignal items:
              await ApplyItemSignalAsync(accountId, items, cancellationToken);
              break;
            case ErrorSignal error:
              await ApplyErrorSignalAsync(accountId, error, cancellationToken);
              break;
            case MessageSignal message:
              await ApplyMessageSignalAsync(accountId, message, cancellationToken);
              break;
            case AttributionSignal attribution:
              await ApplyAttributionSignalAsync(
                accountId,
                attribution,
                cancellationToken);
              break;
          }
        }
        catch (OperationCanceledException)
        {
          throw;
        }
        catch (Exception ex)
        {
          Log.Error(ex, "Failed to persist trade signal: {Message}", ex.Message);
        }
      }
    }

    private async Task ApplyEscrowSignalAsync(
      int accountId,
      EscrowSignal signal,
      CancellationToken cancellationToken)
    {
      if (_discardedTokens.Contains(signal.Observation.Token))
        return;

      ActiveEscrow active = GetOrCreate(signal.Observation);
      MergeObservation(active, signal.Observation);

      if (active.ClosedAt.HasValue && active.EscrowId == null)
      {
        RemoveChatChannel(active);
        await DeleteUnassignedEscrowAsync(
          accountId,
          active.Token,
          cancellationToken);
        _active.Remove(active.Token);
        _discardedTokens.Add(active.Token);
        return;
      }

      if (signal.ReplaceLocalItems != null)
        ReplaceItems(active.LocalItems, signal.ReplaceLocalItems);
      if (signal.ReplaceRemoteItems != null)
        ReplaceItems(active.RemoteItems, signal.ReplaceRemoteItems);

      if (active.Kind == TradeEscrowKind.NonPlayer && active.Baseline == null)
      {
        CollectionStateSnapshot? baseline = collectionStateFeed.Current;
        if (baseline?.AccountId == accountId)
          active.Baseline = baseline;

      }

      if (active.Kind == null)
        return;

      TradeItemWrite[] bufferedItems = active.LocalItems
        .Select(item => new TradeItemWrite(
          TradeEscrowItemRole.LocalOffer, item.Key, item.Value))
        .Concat(active.RemoteItems.Select(item => new TradeItemWrite(
          TradeEscrowItemRole.RemoteOffer, item.Key, item.Value)))
        .ToArray();
      await PersistAsync(accountId, active, bufferedItems, cancellationToken);
      if (signal.ReplaceLocalItems != null)
      {
        await ReplacePersistedRoleAsync(
          accountId,
          active.Token,
          TradeEscrowItemRole.LocalOffer,
          active.LocalItems,
          cancellationToken);
      }
      if (signal.ReplaceRemoteItems != null)
      {
        await ReplacePersistedRoleAsync(
          accountId,
          active.Token,
          TradeEscrowItemRole.RemoteOffer,
          active.RemoteItems,
          cancellationToken);
      }


      if (active.ClosedAt.HasValue)
      {
        RemoveChatChannel(active);
        if (active.Kind == TradeEscrowKind.NonPlayer &&
            active.Result == TradeEscrowResult.Completed &&
            active.FinalState == TradeFinalState.OpenpackComplete)
        {
          if (!active.AttributionStarted)
          {
            active.AttributionStarted = true;
            active.AttributionStatus = TradeAttributionStatus.Pending;
            await PersistAsync(accountId, active, [], cancellationToken);
            _ = InferProductOutputsAsync(
              accountId,
              active,
              cancellationToken);
          }
        }
        else
        {
          _active.Remove(active.Token);
        }
      }
    }

    private async Task ApplyItemSignalAsync(
      int accountId,
      ItemSignal signal,
      CancellationToken cancellationToken)
    {
      if (_discardedTokens.Contains(signal.Observation.Token))
        return;

      ActiveEscrow active = GetOrCreate(signal.Observation);
      MergeObservation(active, signal.Observation);
      Dictionary<int, int> role = signal.Role == TradeEscrowItemRole.LocalOffer
        ? active.LocalItems
        : active.RemoteItems;
      var writes = new List<TradeItemWrite>();
      foreach (ItemValue item in signal.Items)
      {
        if (item.CatalogId <= 0)
          continue;

        int previous = role.GetValueOrDefault(item.CatalogId);
        if (previous == item.Quantity)
          continue;

        if (item.Quantity > 0)
          role[item.CatalogId] = item.Quantity;
        else
          role.Remove(item.CatalogId);
        writes.Add(new(signal.Role, item.CatalogId, item.Quantity));
      }

      if (active.Kind == TradeEscrowKind.NonPlayer && active.Baseline == null)
      {
        CollectionStateSnapshot? baseline = collectionStateFeed.Current;
        if (baseline?.AccountId == accountId)
          active.Baseline = baseline;

      }

      if (active.Kind != null && writes.Count > 0)
        await PersistAsync(accountId, active, writes, cancellationToken);
    }

    private async Task ApplyErrorSignalAsync(
      int accountId,
      ErrorSignal signal,
      CancellationToken cancellationToken)
    {
      if (_discardedTokens.Contains(signal.Observation.Token))
        return;

      ActiveEscrow active = GetOrCreate(signal.Observation);
      MergeObservation(active, signal.Observation);
      active.PendingErrors.Add(new(signal.ObservedAt, signal.ErrorCode));
      if (active.Kind != null)
        await PersistAsync(accountId, active, [], cancellationToken);
    }

    private async Task ApplyMessageSignalAsync(
      int accountId,
      MessageSignal signal,
      CancellationToken cancellationToken)
    {
      if (!_chatChannels.TryGetValue(signal.ChannelKey, out Guid token) ||
          !_active.TryGetValue(token, out ActiveEscrow? active) ||
          active.ClosedAt.HasValue)
      {
        return;
      }

      active.PendingMessages.Add(new(
        signal.Timestamp,
        signal.SenderId,
        signal.SenderName,
        signal.Text));
      if (active.Kind != null)
        await PersistAsync(accountId, active, [], cancellationToken);
    }

    private async Task ApplyAttributionSignalAsync(
      int accountId,
      AttributionSignal signal,
      CancellationToken cancellationToken)
    {
      await using var scope = scopeFactory.CreateAsyncScope();
      var context = scope.ServiceProvider.GetRequiredService<TradeContext>();
      await historyWriter.ReplaceRoleAsync(
        context,
        accountId,
        signal.Token,
        TradeEscrowItemRole.InferredOutput,
        signal.Outputs,
        signal.Status,
        cancellationToken);
      _active.Remove(signal.Token);
    }

    private async Task PersistAsync(
      int accountId,
      ActiveEscrow active,
      IReadOnlyList<TradeItemWrite> items,
      CancellationToken cancellationToken)
    {
      if (active.Kind == null)
        return;

      var write = new TradeEscrowWrite(
        accountId,
        active.Token,
        active.EscrowId,
        active.Kind.Value,
        active.PartnerId,
        active.PartnerName,
        active.StartedAt,
        active.ClosedAt,
        (int)active.State,
        active.Result,
        active.AttributionStatus,
        items,
        active.PendingMessages.ToArray(),
        active.PendingErrors.ToArray());

      await using var scope = scopeFactory.CreateAsyncScope();
      var context = scope.ServiceProvider.GetRequiredService<TradeContext>();
      await historyWriter.ApplyAsync(context, write, cancellationToken);
      active.PendingMessages.Clear();
      active.PendingErrors.Clear();
    }

    private async Task ReplacePersistedRoleAsync(
      int accountId,
      Guid token,
      TradeEscrowItemRole role,
      IReadOnlyDictionary<int, int> items,
      CancellationToken cancellationToken)
    {
      await using var scope = scopeFactory.CreateAsyncScope();
      var context = scope.ServiceProvider.GetRequiredService<TradeContext>();
      try
      {
        await historyWriter.ReplaceRoleAsync(
          context,
          accountId,
          token,
          role,
          items,
          null,
          cancellationToken);
      }
      catch (InvalidOperationException) { }
    }

    private async Task DeleteUnassignedEscrowAsync(
      int accountId,
      Guid token,
      CancellationToken cancellationToken)
    {
      await using var scope = scopeFactory.CreateAsyncScope();
      var context = scope.ServiceProvider.GetRequiredService<TradeContext>();
      await historyWriter.DeleteUnassignedAsync(
        context,
        accountId,
        token,
        cancellationToken);
    }

    private async Task InferProductOutputsAsync(
      int accountId,
      ActiveEscrow active,
      CancellationToken cancellationToken)
    {
      try
      {
        CollectionStateSnapshot? baseline = active.Baseline;
        if (baseline == null || baseline.AccountId != accountId)
        {
          Enqueue(new AttributionSignal(
            active.Token,
            TradeAttributionStatus.Unavailable,
            new Dictionary<int, int>()));
          return;
        }

        CollectionStateSnapshot? final =
          await collectionStateFeed.WaitForEscrowStateAsync(
            active.Token,
            baseline.Version,
            s_collectionMaximumDelay,
            cancellationToken);
        if (final == null || final.AccountId != accountId)
        {
          Enqueue(new AttributionSignal(
            active.Token,
            TradeAttributionStatus.Unavailable,
            new Dictionary<int, int>()));
          return;
        }

        var outputs = final.Changes
          .Where(item => item.Value > 0)
          .ToDictionary(item => item.Key, item => item.Value);
        var removals = final.Changes
          .Where(item => item.Value < 0)
          .ToDictionary(item => item.Key, item => -item.Value);

        bool expectedRemovalMismatch = active.LocalItems.Any(item =>
          removals.GetValueOrDefault(item.Key) < item.Value);
        bool unrelatedRemoval = removals.Any(item =>
          !active.LocalItems.ContainsKey(item.Key));
        TradeAttributionStatus status =
          expectedRemovalMismatch || unrelatedRemoval
            ? TradeAttributionStatus.InferredAmbiguous
            : TradeAttributionStatus.Inferred;
        Enqueue(new AttributionSignal(active.Token, status, outputs));
      }
      catch (OperationCanceledException)
      {
        // Session shutdown marks pending attribution unavailable.
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Failed to infer product escrow outputs.");
        Enqueue(new AttributionSignal(
          active.Token,
          TradeAttributionStatus.Unavailable,
          new Dictionary<int, int>()));
      }
    }

    private ActiveEscrow GetOrCreate(EscrowObservation observation)
    {
      if (_active.TryGetValue(observation.Token, out ActiveEscrow? active))
        return active;

      active = new ActiveEscrow(observation.Token, observation.ObservedAt);
      _active.Add(observation.Token, active);
      return active;
    }

    private void MergeObservation(
      ActiveEscrow active,
      EscrowObservation observation)
    {
      if (observation.ObservedAt < active.StartedAt)
        active.StartedAt = observation.ObservedAt;
      if (observation.EscrowId > 0)
        active.EscrowId = observation.EscrowId;
      if (observation.Kind.HasValue)
        active.Kind = observation.Kind;
      if (observation.PartnerId > 0)
        active.PartnerId = observation.PartnerId;
      if (!string.IsNullOrWhiteSpace(observation.PartnerName))
        active.PartnerName = observation.PartnerName;
      if (!string.IsNullOrWhiteSpace(observation.ChannelKey))
      {
        RemoveChatChannel(active);
        active.ChannelKey = observation.ChannelKey;
        _chatChannels[observation.ChannelKey] = active.Token;
      }

      active.FinalState = observation.FinalState;
      if (!active.ClosedAt.HasValue || observation.State == TradeState.Closed)
        active.State = observation.State;
      if (observation.State == TradeState.Closed)
      {
        active.ClosedAt ??= observation.ObservedAt;
        active.Result = GetResult(
          observation.FinalState,
          observation.PreviousState);
        active.AttributionStatus = TradeAttributionStatus.NotApplicable;
      }
      else if (!active.ClosedAt.HasValue)
      {
        active.Result = TradeEscrowResult.InProgress;
      }
    }

    private void RemoveChatChannel(ActiveEscrow active)
    {
      if (active.ChannelKey != null &&
          _chatChannels.TryGetValue(active.ChannelKey, out Guid token) &&
          token == active.Token)
      {
        _chatChannels.Remove(active.ChannelKey);
      }
    }

    private static TradeEscrowResult GetResult(
      TradeFinalState finalState,
      TradeState? previousState)
    {
      return finalState switch
      {
        TradeFinalState.TradeComplete or
        TradeFinalState.TradeCompleteWithErrors or
        TradeFinalState.OpenpackComplete => TradeEscrowResult.Completed,

        TradeFinalState.UserCanceledInvitation or
        TradeFinalState.OtherCanceledInvitation or
        TradeFinalState.InvitationExpired or
        TradeFinalState.UserDeclinedInvitation or
        TradeFinalState.OtherDeclinedInvitation or
        TradeFinalState.OtherBusyTrading or
        TradeFinalState.TradeExpired or
        TradeFinalState.UserCanceledTrade or
        TradeFinalState.OtherCanceledTrade => TradeEscrowResult.Cancelled,

        TradeFinalState.TradeNotFinal when previousState == TradeState.CancelRequested =>
          TradeEscrowResult.Cancelled,
        TradeFinalState.TradeNotFinal => TradeEscrowResult.ClosedUnknown,
        _ => TradeEscrowResult.Failed,
      };
    }

    private void InstallHooks()
    {
      if (_hooksInstalled)
        return;

      _hooksInstalled = true;
      try
      {
        TradeManager.TradeStarted += OnTradeStarted;
        TradeManager.TradeStateChanged += OnTradeStateChanged;
        TradeEscrow.SendTradeItemUpdate += OnSendTradeItemUpdate;
        TradeEscrow.ReceiveTradeItemUpdate += OnReceiveTradeItemUpdate;
        TradeManager.TradeError += OnTradeError;
        MTGOSDK.API.Chat.Channel.MessageReceived += OnMessageReceived;
      }
      catch
      {
        RemoveHooks();
        throw;
      }
    }

    private void RemoveHooks()
    {
      if (!_hooksInstalled)
        return;

      _hooksInstalled = false;
      TryRemove(() => TradeManager.TradeStarted -= OnTradeStarted);
      TryRemove(() => TradeManager.TradeStateChanged -= OnTradeStateChanged);
      TryRemove(() => TradeEscrow.SendTradeItemUpdate -= OnSendTradeItemUpdate);
      TryRemove(() => TradeEscrow.ReceiveTradeItemUpdate -= OnReceiveTradeItemUpdate);
      TryRemove(() => TradeManager.TradeError -= OnTradeError);
      TryRemove(() => MTGOSDK.API.Chat.Channel.MessageReceived -= OnMessageReceived);
    }

    private void EnqueueOpenTrades()
    {
      try
      {
        foreach (TradeEscrow escrow in TradeManager.OpenTrades.ToArray())
        {
          EscrowObservation observation = CaptureEscrow(escrow, null, null);
          Enqueue(new EscrowSignal(
            observation,
            CaptureItems(() => escrow.TradedItems.CollectionItems),
            CaptureItems(() => escrow.PartnerTradedItems.CollectionItems)));
        }
      }
      catch (Exception ex)
      {
        Log.Error(ex, "Failed to enumerate open trade escrows.");
      }
    }

    private void OnTradeStarted(TradeEscrow escrow, bool isPlayerTrade) =>
      Enqueue(new EscrowSignal(
        CaptureEscrow(
          escrow,
          isPlayerTrade
            ? TradeEscrowKind.Player
            : TradeEscrowKind.NonPlayer,
          null)));

    private void OnTradeStateChanged(
      TradeEscrow escrow,
      (TradeState OldState, TradeState NewState) change)
    {
      EscrowObservation observation =
        CaptureEscrow(escrow, null, change.OldState);
      IReadOnlyList<ItemValue>? localItems =
        change.NewState == TradeState.PackOpenSetupRequested
          ? CaptureItems(() => escrow.TradedItems.CollectionItems)
          : null;
      Enqueue(new EscrowSignal(observation, localItems));
    }

    private void OnSendTradeItemUpdate(
      TradeEscrow escrow,
      IList<CardQuantityPair> items) =>
      Enqueue(new ItemSignal(
        CaptureEscrow(escrow, null, null),
        TradeEscrowItemRole.LocalOffer,
        CaptureItems(items)));

    private void OnReceiveTradeItemUpdate(
      TradeEscrow escrow,
      IList<CardQuantityPair> items) =>
      Enqueue(new ItemSignal(
        CaptureEscrow(escrow, null, null),
        TradeEscrowItemRole.RemoteOffer,
        CaptureItems(items)));

    private void OnTradeError(TradeEscrow? escrow, TradeError error)
    {
      if (escrow == null)
      {
        Log.Warning("Unassociated MTGO trade error: {Error}", error);
        return;
      }

      DateTime observedAt = DateTime.UtcNow;
      Enqueue(new ErrorSignal(
        CaptureEscrow(escrow, null, null, observedAt),
        observedAt,
        (int)error));
    }

    private void OnMessageReceived(MTGOSDK.API.Chat.Channel channel, Message message)
    {
      try
      {
        string key = channel.LocalFileName;
        DateTime timestamp = EnsureUtc(message.Timestamp);
        int? senderId = null;
        string? senderName = null;
        try
        {
          if (message.User is { } user)
          {
            senderId = user.Id > 0 ? user.Id : null;
            senderName = user.Name;
          }
        }
        catch { }

        Enqueue(new MessageSignal(
          key,
          timestamp,
          senderId,
          senderName,
          ChatTextNormalizer.Normalize(message.Text)));
      }
      catch { }
    }

    private static EscrowObservation CaptureEscrow(
      TradeEscrow escrow,
      TradeEscrowKind? kind,
      TradeState? previousState,
      DateTime? observedAt = null)
    {
      Guid token = Safe(() => escrow.Token, Guid.Empty);
      int id = Safe(() => escrow.Id, 0);
      TradeState state = Safe(() => escrow.State, TradeState.Uninitialized);
      TradeFinalState finalState = Safe(
        () => escrow.FinalState,
        TradeFinalState.TradeNotFinal);
      string? partnerName = Safe<string?>(() => escrow.TradePartnerName, null);
      int? partnerId = Safe<int?>(() => escrow.TradePartnerId, null);
      string? channelKey = Safe<string?>(() =>
        escrow.ChatChannel?.LocalFileName, null);

      kind ??= Safe<TradeEscrowKind?>(() => escrow.IsPlayerTrade
        ? TradeEscrowKind.Player
        : TradeEscrowKind.NonPlayer, null);
      kind ??= InferKind(state, finalState, partnerName);
      return new EscrowObservation(
        token,
        id,
        kind,
        partnerId,
        partnerName,
        channelKey,
        state,
        previousState,
        finalState,
        observedAt ?? DateTime.UtcNow);
    }

    private static TradeEscrowKind? InferKind(
      TradeState state,
      TradeFinalState finalState,
      string? partnerName)
    {
      if (state is TradeState.PackOpenSetupRequested or
          TradeState.PackOpenUpdatelistSent or
          TradeState.PackOpenProcessing ||
          finalState is TradeFinalState.OpenpackComplete or
          TradeFinalState.OpenpackNotPermitted or
          TradeFinalState.OpenpackInsufficientQuantity)
      {
        return TradeEscrowKind.NonPlayer;
      }

      if (!string.IsNullOrWhiteSpace(partnerName) ||
          finalState != TradeFinalState.TradeNotFinal)
      {
        return TradeEscrowKind.Player;
      }

      return null;
    }

    private static IReadOnlyList<ItemValue> CaptureItems(
      Func<IEnumerable<CardQuantityPair>> read)
    {
      try { return CaptureItems(read()); }
      catch { return []; }
    }

    private static IReadOnlyList<ItemValue> CaptureItems(
      IEnumerable<CardQuantityPair> items) => items
        .Select(item => new ItemValue(item.Id, item.Quantity))
        .ToArray();

    private static void ReplaceItems(
      IDictionary<int, int> destination,
      IReadOnlyList<ItemValue> source)
    {
      destination.Clear();
      foreach (ItemValue item in source)
      {
        if (item.CatalogId > 0 && item.Quantity > 0)
          destination[item.CatalogId] = item.Quantity;
      }
    }

    private void Enqueue(TradeSignal signal)
    {
      if (signal is EscrowBoundSignal bound && bound.Observation.Token == Guid.Empty)
        return;
      _signals?.Writer.TryWrite(signal);
    }

    private static T Safe<T>(Func<T> read, T fallback)
    {
      try { return read(); }
      catch { return fallback; }
    }

    private static void TryRemove(Action remove)
    {
      try { remove(); }
      catch { }
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
      DateTimeKind.Utc => value,
      DateTimeKind.Local => value.ToUniversalTime(),
      _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private sealed class ActiveEscrow(Guid token, DateTime startedAt)
    {
      public Guid Token { get; } = token;
      public DateTime StartedAt { get; set; } = startedAt;
      public int? EscrowId { get; set; }
      public TradeEscrowKind? Kind { get; set; }
      public int? PartnerId { get; set; }
      public string? PartnerName { get; set; }
      public string? ChannelKey { get; set; }
      public TradeState State { get; set; }
      public TradeFinalState FinalState { get; set; } = TradeFinalState.TradeNotFinal;
      public TradeEscrowResult Result { get; set; } =
        TradeEscrowResult.InProgress;
      public TradeAttributionStatus AttributionStatus { get; set; } =
        TradeAttributionStatus.NotApplicable;
      public DateTime? ClosedAt { get; set; }
      public CollectionStateSnapshot? Baseline { get; set; }
      public bool AttributionStarted { get; set; }
      public Dictionary<int, int> LocalItems { get; } = [];
      public Dictionary<int, int> RemoteItems { get; } = [];
      public List<TradeMessageWrite> PendingMessages { get; } = [];
      public List<TradeErrorWrite> PendingErrors { get; } = [];
    }

    private sealed record EscrowObservation(
      Guid Token,
      int EscrowId,
      TradeEscrowKind? Kind,
      int? PartnerId,
      string? PartnerName,
      string? ChannelKey,
      TradeState State,
      TradeState? PreviousState,
      TradeFinalState FinalState,
      DateTime ObservedAt);

    private abstract record TradeSignal;
    private abstract record EscrowBoundSignal(EscrowObservation Observation)
      : TradeSignal;
    private sealed record EscrowSignal(
      EscrowObservation Observation,
      IReadOnlyList<ItemValue>? ReplaceLocalItems = null,
      IReadOnlyList<ItemValue>? ReplaceRemoteItems = null)
      : EscrowBoundSignal(Observation);
    private sealed record ItemSignal(
      EscrowObservation Observation,
      TradeEscrowItemRole Role,
      IReadOnlyList<ItemValue> Items)
      : EscrowBoundSignal(Observation);
    private sealed record ErrorSignal(
      EscrowObservation Observation,
      DateTime ObservedAt,
      int ErrorCode)
      : EscrowBoundSignal(Observation);
    private sealed record MessageSignal(
      string ChannelKey,
      DateTime Timestamp,
      int? SenderId,
      string? SenderName,
      string Text) : TradeSignal;
    private sealed record AttributionSignal(
      Guid Token,
      TradeAttributionStatus Status,
      IReadOnlyDictionary<int, int> Outputs) : TradeSignal;
    private readonly record struct ItemValue(int CatalogId, int Quantity);
  }
}
