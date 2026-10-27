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

using MTGOTracker.Database;
using MTGOTracker.Database.Models.Collection;
using static MTGOTracker.Services.DatabaseService;


namespace MTGOTracker.Services.MTGO.Collection;

/// <summary>
/// Reconciles controller-authored deck state in SQLite to MTGO.
/// </summary>
public sealed class DeckSyncService(
  IServiceScopeFactory scopeFactory,
  DatabaseReadiness<CollectionContext> databaseReadiness,
  IClientAPIProvider clientProvider,
  IClientCommandGateway commandGateway,
  SettingsService settings,
  CollectionHistoryWriter historyWriter) : BackgroundService
{
  private static readonly TimeSpan s_idleDelay = TimeSpan.FromMilliseconds(500);
  private static readonly TimeSpan s_retryDelay = TimeSpan.FromSeconds(2);
  private readonly Dictionary<long, DateTime> _retryAfter = new();

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (!settings.Startup.SynchronizeDecks) return;
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
          _retryAfter.Remove(pending.Grouping.Id);
        }
        catch (Exception exception) when (
          exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
          // Defer this deck while allowing other queued decks to proceed.
          _retryAfter[pending.Grouping.Id] = DateTime.UtcNow + s_retryDelay;
          Log.Error(exception, "Deck {GroupingId} synchronization failed: {Message}",
            pending.Grouping.Id, exception.Message);
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

    if (!grouping.IsLocal && grouping.PendingOperation == DeckSyncOperation.Delete)
      return new PendingDeck(grouping, 0, []);

    List<CardGroupingRevisionModel> revisions = await context.CardGroupingRevisions
      .AsNoTracking()
      .Where(revision => revision.CardGroupingId == grouping.Id)
      .OrderBy(revision => revision.Id)
      .ToListAsync(cancellationToken);
    long revisionId = revisions.LastOrDefault()?.Id ?? 0;
    return new PendingDeck(grouping, revisionId, revisions);
  }

  private async Task ReconcileAsync(
    PendingDeck pending,
    CancellationToken cancellationToken)
  {
    await using var scope = scopeFactory.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
    try
    {
      // Register the original list first. Later edits use its recovered MTGO ID,
      // so an edit made before linking cannot turn a retry into a second import.
      var registrationRevision = pending.Grouping.IsLocal ? pending.Revisions.FirstOrDefault(revision =>
        revision.RevisionType == CardGroupingRevisionType.Snapshot) : null;
      var registration = registrationRevision == null ? null : CardGroupingRevisionCodec.DeserializeSnapshot(
        pending.Grouping.Kind, pending.Grouping.NetDeckId, registrationRevision.Payload!);

      if (pending.Grouping.PendingOperation == DeckSyncOperation.Delete)
      {
        var imported = registration == null ? null : commandGateway.FindDeck(registration);
        if (imported != null)
          await historyWriter.LinkImportedDeckAsync(context, pending.Grouping.Id,
            registrationRevision!.Id, imported, cancellationToken);
        int? netDeckId = pending.Grouping.IsLocal ? imported?.NetDeckId : pending.Grouping.NetDeckId;
        if (netDeckId.HasValue)
          await commandGateway.DeleteDeckAsync(netDeckId.Value, cancellationToken);
        await historyWriter.CompleteDeckSyncAsync(context, pending.Grouping.Id,
          pending.Grouping.PendingOperation, pending.RevisionId, cancellationToken);
        return;
      }

      if (pending.Grouping.IsLocal)
      {
        if (registration == null)
          throw new InvalidOperationException($"Pending deck grouping {pending.Grouping.Id} has no replayable state.");
        var imported = await commandGateway.ImportDeckAsync(registration, cancellationToken);
        await historyWriter.LinkImportedDeckAsync(context, pending.Grouping.Id,
          registrationRevision!.Id, imported, cancellationToken);
      }
      else
      {
        var state = CollectionHistoryWriter.Replay(pending.Grouping, pending.Revisions)
          ?? throw new InvalidOperationException($"Pending deck grouping {pending.Grouping.Id} has no replayable state.");
        await commandGateway.UpdateDeckAsync(state, cancellationToken);
        await historyWriter.CompleteDeckSyncAsync(context, pending.Grouping.Id,
          pending.Grouping.PendingOperation, pending.RevisionId, cancellationToken);
      }
    }
    catch (Exception exception) when (
      exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
    {
      await RecordErrorAsync(pending.Grouping.Id, exception.Message, cancellationToken);
      throw;
    }
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

  private sealed record PendingDeck(
    CardGroupingModel Grouping,
    long RevisionId,
    IReadOnlyList<CardGroupingRevisionModel> Revisions);
}
