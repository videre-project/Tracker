/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MTGOSDK.Core.Logging;

using Tracker.Database;
using Tracker.Database.Models.Collection;
using static Tracker.Services.DatabaseService;


namespace Tracker.Services.MTGO.Collection;

/// <summary>
/// Reconciles controller-authored deck state in SQLite to MTGO.
/// </summary>
public sealed class DeckSyncService(
  IServiceScopeFactory scopeFactory,
  DatabaseReadiness<CollectionContext> databaseReadiness,
  IClientAPIProvider clientProvider,
  IClientCommandGateway commandGateway) : BackgroundService
{
  private static readonly TimeSpan s_idleDelay = TimeSpan.FromMilliseconds(500);
  private static readonly TimeSpan s_retryDelay = TimeSpan.FromSeconds(2);
  private readonly Dictionary<long, DateTime> _retryAfter = new();

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    await databaseReadiness.WaitAsync(stoppingToken);

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await clientProvider.WaitForClientReadyAsync(stoppingToken);
        UserIdentity identity = clientProvider.CurrentUser
          ?? throw new InvalidOperationException(
            "Client is ready without an authoritative user identity.");
        PendingDeck? pending = await LoadPendingDeckAsync(
          identity.Id,
          stoppingToken);
        if (pending == null)
        {
          await Task.Delay(s_idleDelay, stoppingToken);
          continue;
        }

        try
        {
          await ReconcileAsync(pending, stoppingToken);
          _retryAfter.Remove(pending.GroupingId);
        }
        catch (Exception exception) when (
          exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
          // Defer this deck while allowing other queued decks to proceed.
          _retryAfter[pending.GroupingId] = DateTime.UtcNow + s_retryDelay;
          Log.Error(exception, "Deck {GroupingId} synchronization failed: {Message}",
            pending.GroupingId, exception.Message);
        }
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;
      }
      catch (Exception exception)
      {
        Log.Error(
          exception,
          "Outbound deck reconciliation failed: {Message}",
          exception.Message);
        try
        {
          await Task.Delay(s_retryDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
          break;
        }
      }
    }
  }

  private async Task<PendingDeck?> LoadPendingDeckAsync(
    int accountId,
    CancellationToken cancellationToken)
  {
    await using var scope = scopeFactory.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
    DateTime now = DateTime.UtcNow;
    long[] expiredIds = _retryAfter
      .Where(entry => entry.Value <= now)
      .Select(entry => entry.Key)
      .ToArray();
    foreach (long id in expiredIds)
      _retryAfter.Remove(id);
    long[] deferredIds = _retryAfter.Keys.ToArray();
    CardGroupingModel? grouping = await context.CardGroupings
      .AsNoTracking()
      .Where(candidate =>
        candidate.AccountId == accountId &&
        candidate.Kind == CardGroupingKind.Deck &&
        candidate.PendingOperation != DeckSyncOperation.None &&
        !deferredIds.Contains(candidate.Id))
      .OrderBy(candidate => candidate.Id)
      .FirstOrDefaultAsync(cancellationToken);
    if (grouping == null)
      return null;

    List<CardGroupingRevisionModel> revisions = await context.CardGroupingRevisions
      .AsNoTracking()
      .Where(revision => revision.CardGroupingId == grouping.Id)
      .OrderBy(revision => revision.Id)
      .ToListAsync(cancellationToken);
    long revisionId = revisions.LastOrDefault()?.Id ?? 0;
    CardGroupingState? state = grouping.PendingOperation == DeckSyncOperation.Upsert
      ? CollectionHistoryWriter.Replay(grouping, revisions)
      : null;
    if (grouping.PendingOperation == DeckSyncOperation.Upsert && state == null)
      throw new InvalidOperationException(
        $"Pending deck grouping {grouping.Id} has no replayable state.");

    return new PendingDeck(
      grouping.Id,
      grouping.AccountId,
      grouping.NetDeckId,
      grouping.IsLocal,
      grouping.PendingOperation,
      revisionId,
      state);
  }

  private async Task ReconcileAsync(
    PendingDeck pending,
    CancellationToken cancellationToken)
  {
    try
    {
      if (pending.Operation == DeckSyncOperation.Delete)
      {
        if (!pending.IsLocal)
          await commandGateway.DeleteDeckAsync(pending.NetDeckId, cancellationToken);
        await CompleteDeleteAsync(pending, cancellationToken);
        return;
      }

      DeckSyncState desired = ToSyncState(pending.State!);
      if (pending.IsLocal)
      {
        DeckSyncResult imported = await commandGateway.ImportDeckAsync(
          desired,
          cancellationToken);
        await CompleteImportAsync(pending, imported, cancellationToken);
      }
      else
      {
        await commandGateway.UpdateDeckAsync(desired, cancellationToken);
        await CompleteUpdateAsync(pending, cancellationToken);
      }
    }
    catch (Exception exception) when (
      exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
    {
      await RecordErrorAsync(pending.GroupingId, exception.Message, cancellationToken);
      throw;
    }
  }

  private async Task CompleteImportAsync(
    PendingDeck pending,
    DeckSyncResult imported,
    CancellationToken cancellationToken)
  {
    if (imported.NetDeckId <= 0)
      throw new InvalidOperationException("MTGO returned an invalid imported deck ID.");

    await using var scope = scopeFactory.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
    await using var transaction = await context.Database.BeginTransactionAsync(
      cancellationToken);
    CardGroupingModel? grouping = await context.CardGroupings
      .SingleOrDefaultAsync(candidate => candidate.Id == pending.GroupingId, cancellationToken);
    if (grouping == null || !grouping.IsLocal)
      return;

    CardGroupingModel? duplicate = await context.CardGroupings.SingleOrDefaultAsync(
      candidate =>
        candidate.AccountId == pending.AccountId &&
        candidate.Kind == CardGroupingKind.Deck &&
        candidate.NetDeckId == imported.NetDeckId &&
        candidate.Id != grouping.Id,
      cancellationToken);
    if (duplicate != null)
    {
      context.CardGroupings.Remove(duplicate);
      await context.SaveChangesAsync(cancellationToken);
    }

    grouping.NetDeckId = imported.NetDeckId;
    grouping.Name = imported.Name;
    grouping.IsLocal = false;
    grouping.SyncError = null;
    if (grouping.PendingOperation == DeckSyncOperation.Upsert)
    {
      grouping.PendingOperation = await LatestRevisionIdAsync(
        context,
        grouping.Id,
        cancellationToken) == pending.RevisionId
          ? DeckSyncOperation.None
          : DeckSyncOperation.Upsert;
    }
    await context.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
  }

  private async Task CompleteUpdateAsync(
    PendingDeck pending,
    CancellationToken cancellationToken)
  {
    await using var scope = scopeFactory.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
    // Keep the revision check and queue acknowledgement under one write
    // transaction so an edit arriving during sync cannot lose its marker.
    await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    CardGroupingModel? grouping = await context.CardGroupings
      .SingleOrDefaultAsync(candidate => candidate.Id == pending.GroupingId, cancellationToken);
    if (grouping == null || grouping.PendingOperation != DeckSyncOperation.Upsert)
      return;

    grouping.SyncError = null;
    if (await LatestRevisionIdAsync(context, grouping.Id, cancellationToken) ==
        pending.RevisionId)
    {
      grouping.PendingOperation = DeckSyncOperation.None;
    }
    await context.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
  }

  private async Task CompleteDeleteAsync(
    PendingDeck pending,
    CancellationToken cancellationToken)
  {
    await using var scope = scopeFactory.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
    CardGroupingModel? grouping = await context.CardGroupings
      .SingleOrDefaultAsync(candidate => candidate.Id == pending.GroupingId, cancellationToken);
    if (grouping == null || grouping.PendingOperation != DeckSyncOperation.Delete)
      return;

    grouping.PendingOperation = DeckSyncOperation.None;
    grouping.SyncError = null;
    await context.SaveChangesAsync(cancellationToken);
  }

  private async Task RecordErrorAsync(
    long groupingId,
    string error,
    CancellationToken cancellationToken)
  {
    await using var scope = scopeFactory.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
    CardGroupingModel? grouping = await context.CardGroupings.FindAsync(
      [groupingId],
      cancellationToken);
    if (grouping == null)
      return;

    grouping.SyncError = error;
    await context.SaveChangesAsync(cancellationToken);
  }

  private static Task<long> LatestRevisionIdAsync(
    CollectionContext context,
    long groupingId,
    CancellationToken cancellationToken) =>
    context.CardGroupingRevisions
      .Where(revision => revision.CardGroupingId == groupingId)
      .MaxAsync(revision => revision.Id, cancellationToken);

  private static DeckSyncState ToSyncState(CardGroupingState state) =>
    new(
      state.NetDeckId,
      state.Name ?? throw new InvalidOperationException("A deck name is required."),
      state.FormatCode,
      state.Items.Select(item => new DeckSyncCard(
        item.CatalogId,
        item.Region,
        item.Annotation,
        item.Quantity)).ToArray());

  private sealed record PendingDeck(
    long GroupingId,
    int AccountId,
    int NetDeckId,
    bool IsLocal,
    DeckSyncOperation Operation,
    long RevisionId,
    CardGroupingState? State);
}
