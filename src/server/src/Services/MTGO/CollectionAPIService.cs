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

using MTGOSDK.API.Collection;
using MTGOSDK.API.Trade;
using MTGOSDK.Core.Logging;
using static MTGOSDK.Core.Reflection.DLRWrapper;

using Tracker.Database;
using Tracker.Database.Models.Collection;
using Tracker.Services.MTGO.Collection;
using static Tracker.Services.DatabaseService;


namespace Tracker.Services.MTGO;

public static class CollectionAPIService
{
  public static IHostApplicationBuilder RegisterCollectionService(
    this IHostApplicationBuilder builder)
  {
    builder.Services.AddSingleton<CollectionHistoryWriter>();
    builder.Services.AddSingleton<CollectionSnapshotReader>();
    builder.Services.AddSingleton<CollectionDeckService>();
    builder.Services.AddSingleton<CollectionStateFeed>();
    builder.Services.AddSingleton<ICollectionStateFeed>(provider =>
      provider.GetRequiredService<CollectionStateFeed>());
    builder.Services.AddHostedService<CollectionService>();
    return builder;
  }

  public sealed class CollectionService(
    IClientAPIProvider clientProvider,
    IServiceScopeFactory scopeFactory,
    DatabaseReadiness<CollectionContext> databaseReadiness,
    CollectionHistoryWriter historyWriter,
    CollectionSnapshotReader snapshotReader,
    CollectionStateFeed stateFeed)
      : BackgroundService
  {
    private static readonly TimeSpan s_quietPeriod =
      TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan s_maximumDelay = TimeSpan.FromSeconds(5);

    private Channel<DirtySignal>? _signals;
    private bool _hooksInstalled;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      await databaseReadiness.WaitAsync(stoppingToken);
      Log.Information("Collection API background service started.");

      while (!stoppingToken.IsCancellationRequested)
      {
        try
        {
          await clientProvider.WaitForClientReadyAsync(stoppingToken);
          UserIdentity identity = clientProvider.CurrentUser
            ?? throw new InvalidOperationException(
              "Client is ready without an authoritative user identity.");

          using var sessionCts =
            CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
          _signals = Channel.CreateUnbounded<DirtySignal>(new()
          {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
          });

          InstallHooks();
          await UpsertAccountAsync(identity, sessionCts.Token);
          await PurgeRedundantRevisionsAsync(sessionCts.Token);

          Task processor = ProcessSignalsAsync(identity.Id, sessionCts.Token);
          Task disconnect =
            clientProvider.WaitForClientDisconnectAsync(stoppingToken);
          var initialScan = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
          Enqueue(DirtySignal.StartupScan(initialScan));

          Task completed = await Task.WhenAny(
            initialScan.Task,
            disconnect,
            processor);
          if (completed == processor)
          {
            await processor;
            throw new InvalidOperationException(
              "Collection signal processor stopped during startup.");
          }
          if (completed == initialScan.Task)
          {
            await initialScan.Task;
            Log.Information(
              "Completed initial collection reconciliation for account {AccountId}.",
              identity.Id);
            completed = await Task.WhenAny(disconnect, processor);
            if (completed == processor)
              await processor;
          }

          sessionCts.Cancel();
          try { await processor; }
          catch (OperationCanceledException) { }
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
          Log.Error(ex, "Collection service session failed: {Message}", ex.Message);
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
          RemoveHooks();
          _signals?.Writer.TryComplete();
          _signals = null;
          stateFeed.Reset();
        }
      }
    }

    private async Task UpsertAccountAsync(
      UserIdentity identity,
      CancellationToken cancellationToken)
    {
      await using var scope = scopeFactory.CreateAsyncScope();
      var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
      await historyWriter.UpsertAccountAsync(context, identity, cancellationToken);
    }

    private async Task PurgeRedundantRevisionsAsync(CancellationToken cancellationToken)
    {
      try
      {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
        int purged = await historyWriter.PurgeRedundantRevisionsAsync(context, cancellationToken);
        if (purged > 0)
        {
          Log.Information("Purged {PurgedCount} redundant deck/collection revisions from database.", purged);
        }
      }
      catch (Exception ex)
      {
        Log.Warning(ex, "Failed to purge redundant collection revisions on startup.");
      }
    }

    private async Task ProcessSignalsAsync(
      int accountId,
      CancellationToken cancellationToken)
    {
      ChannelReader<DirtySignal> reader = _signals!.Reader;
      var pending = new Dictionary<DirtySignal, PendingSignal>();

      while (!cancellationToken.IsCancellationRequested)
      {
        if (pending.Count == 0)
        {
          if (!await reader.WaitToReadAsync(cancellationToken))
            break;
        }
        else
        {
          DateTime nextDue = pending.Values.Min(GetSignalDueAt);
          TimeSpan delay = nextDue - DateTime.UtcNow;
          if (delay > TimeSpan.Zero)
          {
            using var deadlineCts =
              CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadlineCts.CancelAfter(delay);
            try
            {
              if (!await reader.WaitToReadAsync(deadlineCts.Token))
                break;
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested)
            {
              // The next grouping reached its exact debounce deadline.
            }
          }
        }

        DateTime now = DateTime.UtcNow;
        var immediate = new List<DirtySignal>();
        while (reader.TryRead(out DirtySignal signal))
        {
          if (signal.IsImmediate)
          {
            immediate.Add(signal);
          }
          else if (pending.TryGetValue(signal, out PendingSignal current))
          {
            pending[signal] = current with { LastSeen = now };
          }
          else
          {
            pending[signal] = new(now, now);
          }
        }

        foreach (DirtySignal signal in immediate)
        {
          if (!await TryReconcileSignalAsync(signal))
          {
            pending[signal with { IsImmediate = false }] =
              new(DateTime.UtcNow, DateTime.UtcNow);
          }
        }

        now = DateTime.UtcNow;
        DirtySignal[] due = pending
          .Where(entry => GetSignalDueAt(entry.Value) <= now)
          .Select(entry => entry.Key)
          .ToArray();

        foreach (DirtySignal signal in due)
        {
          pending.Remove(signal);
          if (!await TryReconcileSignalAsync(signal))
            pending[signal] = new(now, now);
        }
      }

      async Task<bool> TryReconcileSignalAsync(DirtySignal signal)
      {
        try
        {
          if (signal.IsFullScan)
            await ReconcileAllAsync(accountId, cancellationToken, signal);
          else
            await ReconcileOneAsync(accountId, signal, cancellationToken);
          signal.Completion?.TrySetResult();
          return true;
        }
        catch (OperationCanceledException)
        {
          signal.Completion?.TrySetCanceled(cancellationToken);
          throw;
        }
        catch (Exception ex)
        {
          Log.Error(
            ex,
            "Failed to reconcile collection state; retaining dirty work: {Message}",
            ex.Message);
          return false;
        }
      }
    }
    private async Task ReconcileAllAsync(
      int accountId,
      CancellationToken cancellationToken,
      DirtySignal? source = null)
    {
      IReadOnlyList<CardGroupingState> states = snapshotReader.ReadAll();
      DateTime observedAt = source?.Correlation?.Timestamp ?? DateTime.UtcNow;
      CardGroupingState collection = states.Single(state =>
        state.Kind == CardGroupingKind.Collection);
      var seen = states
        .Select(state => (state.Kind, state.NetDeckId))
        .ToHashSet();

      await using var scope = scopeFactory.CreateAsyncScope();
      var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
      foreach (CardGroupingState state in states)
      {
        await historyWriter.ReconcileAsync(
          context,
          accountId,
          state,
          observedAt,
          cancellationToken);
      }

      int deleted = await historyWriter.MarkMissingDeletedAsync(
        context,
        accountId,
        seen,
        observedAt,
        cancellationToken);
      Log.Trace(
        "Reconciled {GroupingCount} card groupings; marked {DeletedCount} deleted.",
        states.Count,
        deleted);
      PublishCollectionState(accountId, collection, observedAt, source);
    }

    private async Task ReconcileOneAsync(
      int accountId,
      DirtySignal signal,
      CancellationToken cancellationToken)
    {
      CardGroupingState? state = snapshotReader.TryRead(
        signal.Kind,
        signal.NetDeckId);
      if (state == null)
      {
        await ReconcileAllAsync(accountId, cancellationToken, signal);
        return;
      }

      DateTime observedAt = signal.Correlation?.Timestamp ?? DateTime.UtcNow;
      await using var scope = scopeFactory.CreateAsyncScope();
      var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
      bool changed = await historyWriter.ReconcileAsync(
        context,
        accountId,
        state,
        observedAt,
        cancellationToken);
      if (changed && state.Kind == CardGroupingKind.Collection)
        PublishCollectionState(accountId, state, observedAt, signal);
    }

    private void PublishCollectionState(
      int accountId,
      CardGroupingState state,
      DateTime observedAt,
      DirtySignal? source = null)
    {
      var quantities = state.Items
        .GroupBy(item => item.CatalogId)
        .ToDictionary(
          group => group.Key,
          group => group.Sum(item => item.Quantity));
      var changes = source?.Changes?
        .Where(item => item.Id > 0 && item.Quantity != 0)
        .GroupBy(item => item.Id)
        .ToDictionary(
          group => group.Key,
          group => group.Sum(item => item.Quantity))
        ?? new Dictionary<int, int>();

      stateFeed.Publish(
        accountId,
        observedAt,
        source?.Correlation,
        changes,
        quantities);
    }
    private void InstallHooks()
    {
      if (_hooksInstalled)
        return;

      CollectionManager.CollectionItemsChanged += OnCollectionItemsChanged;
      CollectionManager.CardGroupingItemsChanged += OnGroupingItemsChanged;
      CollectionManager.CardGroupingPropertyChanged += OnGroupingPropertyChanged;
      CollectionManager.DeckCreatedOrImported += OnGroupingCreatedOrImported;
      CollectionManager.DeleteGrouping += OnGroupingDeleted;
      _hooksInstalled = true;
    }

    private void RemoveHooks()
    {
      if (!_hooksInstalled)
        return;

      Try(() => CollectionManager.CollectionItemsChanged -= OnCollectionItemsChanged);
      Try(() => CollectionManager.CardGroupingItemsChanged -= OnGroupingItemsChanged);
      Try(() => CollectionManager.CardGroupingPropertyChanged -= OnGroupingPropertyChanged);
      Try(() => CollectionManager.DeckCreatedOrImported -= OnGroupingCreatedOrImported);
      Try(() => CollectionManager.DeleteGrouping -= OnGroupingDeleted);
      _hooksInstalled = false;
    }

    private void OnCollectionItemsChanged(
      MTGOSDK.API.Collection.Collection _,
      (
        IList<CardQuantityPair> Changes,
        TransactionCorrelation Correlation) change) =>
      Enqueue(new(
        CardGroupingKind.Collection,
        0,
        Correlation: NormalizeCorrelation(change.Correlation),
        Changes: change.Changes.ToArray(),
        IsImmediate: true));

    private void OnGroupingItemsChanged(
      CardGrouping grouping,
      IList<CardQuantityPair> _) =>
      EnqueueGrouping(grouping);

    private void OnGroupingPropertyChanged(
      CardGrouping grouping,
      string _) =>
      EnqueueGrouping(grouping);

    private void OnGroupingCreatedOrImported(
      CardGrouping grouping,
      DateTime _) =>
      EnqueueGrouping(grouping, fullScanWhenUnassigned: true);

    private void OnGroupingDeleted(CardGrouping _, DateTime __) =>
      Enqueue(DirtySignal.FullScan);

    private void EnqueueGrouping(
      CardGrouping grouping,
      bool fullScanWhenUnassigned = false)
    {
      try
      {
        if (grouping.NetDeckId <= 0)
        {
          if (fullScanWhenUnassigned)
            Enqueue(DirtySignal.FullScan);
          return;
        }

        CardGroupingKind? kind = grouping.GroupingType switch
        {
          CardGroupingType.Deck => CardGroupingKind.Deck,
          CardGroupingType.Binder => CardGroupingKind.Binder,
          CardGroupingType.Wishlist => CardGroupingKind.Wishlist,
          _ => null,
        };
        if (kind.HasValue)
          Enqueue(new(kind.Value, grouping.NetDeckId));
      }
      catch
      {
        Enqueue(DirtySignal.FullScan);
      }
    }

    private void Enqueue(DirtySignal signal) =>
      _signals?.Writer.TryWrite(signal);

    private static TransactionCorrelation NormalizeCorrelation(
      TransactionCorrelation correlation) => correlation with
    {
      Timestamp = correlation.Timestamp == default
        ? DateTime.UtcNow
        : EnsureUtc(correlation.Timestamp),
    };

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
      DateTimeKind.Utc => value,
      DateTimeKind.Local => value.ToUniversalTime(),
      _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private static DateTime GetSignalDueAt(PendingSignal signal)
    {
      DateTime quietDeadline = signal.LastSeen + s_quietPeriod;
      DateTime maximumDeadline = signal.FirstSeen + s_maximumDelay;
      return quietDeadline <= maximumDeadline
        ? quietDeadline
        : maximumDeadline;
    }

    private readonly record struct PendingSignal(
      DateTime FirstSeen,
      DateTime LastSeen);

    private readonly record struct DirtySignal(
      CardGroupingKind Kind,
      int NetDeckId,
      bool IsFullScan = false,
      TransactionCorrelation? Correlation = null,
      IReadOnlyList<CardQuantityPair>? Changes = null,
      bool IsImmediate = false,
      TaskCompletionSource? Completion = null)
    {
      public static DirtySignal FullScan { get; } =
        new(default, default, true);

      public static DirtySignal StartupScan(TaskCompletionSource completion) =>
        new(
          default,
          default,
          IsFullScan: true,
          IsImmediate: true,
          Completion: completion);
    }
  }
}
