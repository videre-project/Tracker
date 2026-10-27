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

using MTGOTracker.Database;
using MTGOTracker.Database.Models.Collection;


namespace MTGOTracker.Services.MTGO.Collection;

public sealed class CollectionHistoryWriter
{
  public const int MaximumDeltasBeforeSnapshot = 4096;

  public async Task UpsertAccountAsync(
    CollectionContext context,
    UserIdentity identity,
    CancellationToken cancellationToken)
  {
    if (!identity.IsValid)
      throw new InvalidOperationException(
        "A valid current user ID and username are required.");

    var account = await context.Accounts.FindAsync(
      [identity.Id],
      cancellationToken);
    if (account == null)
    {
      context.Accounts.Add(new AccountModel
      {
        Id = identity.Id,
        Username = identity.Username,
      });
    }
    else if (!string.Equals(
      account.Username,
      identity.Username,
      StringComparison.Ordinal))
    {
      account.Username = identity.Username;
    }

    await context.SaveChangesAsync(cancellationToken);
  }

  public async Task<bool> ReconcileAsync(
    CollectionContext context,
    int accountId,
    CardGroupingState state,
    DateTime observedAt,
    CancellationToken cancellationToken,
    bool isLocal = false,
    bool allowPendingWrite = false,
    long? expectedRevisionId = null)
  {
    state = state.Normalize();
    ValidateState(state);

    // An import may include the revision, sync marker, and enrichment in one transaction.
    await using var transaction = context.Database.CurrentTransaction == null
      ? await context.Database.BeginTransactionAsync(cancellationToken)
      : null;

    var grouping = await context.CardGroupings
      .SingleOrDefaultAsync(candidate =>
        candidate.AccountId == accountId &&
        candidate.Kind == state.Kind &&
        candidate.NetDeckId == state.NetDeckId &&
        candidate.IsLocal == isLocal,
        cancellationToken);

    if (expectedRevisionId.HasValue)
    {
      var intervening = grouping == null ? [] : await context.CardGroupingRevisions
        .Where(revision => revision.CardGroupingId == grouping.Id && revision.Id > expectedRevisionId.Value)
        .OrderBy(revision => revision.Id)
        .ToListAsync(cancellationToken);
      if (grouping == null || intervening.Any(revision => !CardGroupingRevisionCodec.IsMetadataOnly(revision)))
        throw new InvalidOperationException(
          "The deck changed after this revision was loaded. Reload it and try again.");
      // Preserve native metadata such as MTGO's canonical format code while
      // applying the card edit against its original content revision.
      DateTime? editedAt = state.Timestamp;
      foreach (var revision in intervening)
        state = CardGroupingRevisionCodec.ApplyDelta(state, revision.Payload!);
      state = state with { Timestamp = editedAt };
    }

    if (grouping == null)
    {
      grouping = new CardGroupingModel
      {
        AccountId = accountId,
        Kind = state.Kind,
        NetDeckId = state.NetDeckId,
        IsLocal = isLocal,
      };
      ApplyMetadata(grouping, state);
      context.CardGroupings.Add(grouping);
      await context.SaveChangesAsync(cancellationToken);

      context.CardGroupingRevisions.Add(new CardGroupingRevisionModel
      {
        CardGroupingId = grouping.Id,
        ObservedAt = observedAt,
        RevisionType = CardGroupingRevisionType.Snapshot,
        Payload = CardGroupingRevisionCodec.SerializeSnapshot(state),
      });
      await context.SaveChangesAsync(cancellationToken);
      if (transaction != null) await transaction.CommitAsync(cancellationToken);
      return true;
    }

    // A controller-authored revision is the desired state until the outbound
    // deck reconciler applies it. Do not let an older MTGO snapshot overwrite
    // that pending edit while the operation is queued.
    if (!isLocal && !allowPendingWrite &&
        grouping.PendingOperation != DeckSyncOperation.None)
    {
      if (transaction != null) await transaction.RollbackAsync(cancellationToken);
      return false;
    }

    var revisions = new List<CardGroupingRevisionModel>();
    CardGroupingState? previous = null;
    if (!grouping.IsDeleted)
    {
      long? latestSnapshotId = await context.CardGroupingRevisions
        .Where(revision =>
          revision.CardGroupingId == grouping.Id &&
          revision.RevisionType == CardGroupingRevisionType.Snapshot)
        .Select(revision => (long?)revision.Id)
        .MaxAsync(cancellationToken);
      if (latestSnapshotId.HasValue)
      {
        revisions = await context.CardGroupingRevisions
          .AsNoTracking()
          .Where(revision =>
            revision.CardGroupingId == grouping.Id &&
            revision.Id >= latestSnapshotId.Value)
          .OrderBy(revision => revision.Id)
          .ToListAsync(cancellationToken);
        previous = Replay(grouping, revisions);
      }
    }
    if (!grouping.IsDeleted && previous != null &&
        CardGroupingRevisionCodec.StateEquals(previous, state))
    {
      if (transaction != null) await transaction.RollbackAsync(cancellationToken);
      return false;
    }

    CardGroupingRevisionType revisionType;
    byte[] payload;
    if (grouping.IsDeleted || previous == null)
    {
      revisionType = CardGroupingRevisionType.Snapshot;
      payload = CardGroupingRevisionCodec.SerializeSnapshot(state);
    }
    else
    {
      byte[] delta = CardGroupingRevisionCodec.SerializeDelta(previous, state);
      CardGroupingDeltaPayload deltaState =
        CardGroupingRevisionCodec.DeserializeDelta(delta);
      // Metadata-only revisions must remain deltas. Promoting one to a
      // snapshot would make it look like a content change to consumers that
      // derive the last-content-modified timestamp from revision history.
      bool checkpoint = deltaState.ItemChanges.Length > 0 &&
        ShouldCheckpoint(revisions, delta.Length);
      revisionType = checkpoint
        ? CardGroupingRevisionType.Snapshot
        : CardGroupingRevisionType.Delta;
      payload = checkpoint
        ? CardGroupingRevisionCodec.SerializeSnapshot(state)
        : delta;
    }

    ApplyMetadata(grouping, state);
    grouping.IsDeleted = false;
    context.CardGroupingRevisions.Add(new CardGroupingRevisionModel
    {
      CardGroupingId = grouping.Id,
      ObservedAt = observedAt,
      RevisionType = revisionType,
      Payload = payload,
    });
    await context.SaveChangesAsync(cancellationToken);
    if (transaction != null) await transaction.CommitAsync(cancellationToken);
    return true;
  }

  public async Task<long> ReconcileAndGetRevisionAsync(
    CollectionContext context,
    int accountId,
    CardGroupingState state,
    DateTime observedAt,
    CancellationToken cancellationToken,
    bool isLocal = false,
    bool allowPendingWrite = false,
    long? expectedRevisionId = null)
  {
    await ReconcileAsync(
      context,
      accountId,
      state,
      observedAt,
      cancellationToken,
      isLocal,
      allowPendingWrite,
      expectedRevisionId);

    long groupingId = await context.CardGroupings
      .Where(grouping =>
        grouping.AccountId == accountId &&
        grouping.Kind == state.Kind &&
        grouping.NetDeckId == state.NetDeckId &&
        grouping.IsLocal == isLocal)
      .Select(grouping => grouping.Id)
      .SingleAsync(cancellationToken);

    return await context.CardGroupingRevisions
      .Where(revision =>
        revision.CardGroupingId == groupingId &&
        revision.RevisionType != CardGroupingRevisionType.Deleted)
      .MaxAsync(revision => revision.Id, cancellationToken);
  }

  /// <summary>
  /// Links a local deck to its imported MTGO deck, merging any observed history
  /// without changing existing revision identities or losing newer local edits.
  /// </summary>
  public async Task LinkImportedDeckAsync(
    CollectionContext context,
    long groupingId,
    long revisionId,
    CardGroupingState imported,
    CancellationToken cancellationToken)
  {
    await using var transaction = await context.Database.BeginTransactionAsync(
      cancellationToken);
    CardGroupingModel? grouping = await context.CardGroupings
      .SingleOrDefaultAsync(candidate => candidate.Id == groupingId, cancellationToken);
    if (grouping == null || !grouping.IsLocal)
      return;
    if (imported.NetDeckId <= 0)
      throw new InvalidOperationException("MTGO returned an invalid imported deck ID.");

    long latestLocalRevision = await context.CardGroupingRevisions
      .Where(revision => revision.CardGroupingId == grouping.Id)
      .MaxAsync(revision => revision.Id, cancellationToken);
    bool hasNewerEdit = latestLocalRevision != revisionId;
    CardGroupingModel? duplicate = await context.CardGroupings.SingleOrDefaultAsync(
      candidate =>
        candidate.AccountId == grouping.AccountId &&
        candidate.Kind == CardGroupingKind.Deck &&
        candidate.NetDeckId == imported.NetDeckId &&
        candidate.Id != grouping.Id,
      cancellationToken);
    if (duplicate != null)
    {
      var revisions = await context.CardGroupingRevisions
        .Where(revision => revision.CardGroupingId == grouping.Id || revision.CardGroupingId == duplicate.Id)
        .OrderBy(revision => revision.Id)
        .ToListAsync(cancellationToken);
      var localRevisions = revisions.Where(revision => revision.CardGroupingId == grouping.Id).ToList();
      var observedRevisions = revisions.Where(revision => revision.CardGroupingId == duplicate.Id).ToList();
      CardGroupingState? desired = hasNewerEdit && grouping.PendingOperation == DeckSyncOperation.Upsert
        ? Replay(grouping, localRevisions)
        : null;

      // Each history has its own delta chain. Checkpoint at each switch between
      // them so existing revision IDs still resolve to the same deck contents.
      var checkpoints = new HashSet<long>();
      for (int i = 1; i < revisions.Count; i++)
        if (revisions[i - 1].CardGroupingId != revisions[i].CardGroupingId)
          checkpoints.Add(revisions[i].Id);
      CheckpointRevisions(grouping, localRevisions, checkpoints);
      CheckpointRevisions(duplicate, observedRevisions, checkpoints);
      foreach (var revision in observedRevisions) revision.CardGrouping = grouping;
      await context.SaveChangesAsync(cancellationToken);

      if (desired != null && observedRevisions.Any(revision => revision.Id > latestLocalRevision))
      {
        var enrichment = await context.DeckRevisionEnrichments.AsNoTracking()
          .SingleOrDefaultAsync(item => item.CardGroupingRevisionId == latestLocalRevision, cancellationToken);
        context.CardGroupingRevisions.Add(new CardGroupingRevisionModel
        {
          CardGroupingId = grouping.Id,
          ObservedAt = DateTime.UtcNow,
          RevisionType = CardGroupingRevisionType.Snapshot,
          Payload = CardGroupingRevisionCodec.SerializeSnapshot(desired),
          DeckEnrichment = enrichment == null ? null : new DeckRevisionEnrichmentModel
          {
            Archetype = enrichment.Archetype,
            FeaturedCard = enrichment.FeaturedCard,
          },
        });
      }
      context.CardGroupings.Remove(duplicate);
      await context.SaveChangesAsync(cancellationToken);
    }

    grouping.NetDeckId = imported.NetDeckId;
    if (!hasNewerEdit) ApplyMetadata(grouping, imported);
    grouping.IsLocal = false;
    grouping.SyncError = null;
    if (grouping.PendingOperation == DeckSyncOperation.Upsert)
    {
      grouping.PendingOperation = hasNewerEdit ? DeckSyncOperation.Upsert : DeckSyncOperation.None;
    }
    await context.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
  }

  private static void CheckpointRevisions(
    CardGroupingModel grouping,
    IReadOnlyList<CardGroupingRevisionModel> revisions,
    IReadOnlySet<long> checkpoints)
  {
    var segment = new List<CardGroupingRevisionModel>();
    foreach (var revision in revisions)
    {
      if (revision.RevisionType is CardGroupingRevisionType.Snapshot or CardGroupingRevisionType.Deleted)
        segment.Clear();
      segment.Add(revision);
      if (revision.RevisionType != CardGroupingRevisionType.Delta || !checkpoints.Contains(revision.Id))
        continue;

      var state = Replay(grouping, segment)
        ?? throw new InvalidOperationException($"Revision {revision.Id} has no replayable snapshot.");
      revision.RevisionType = CardGroupingRevisionType.Snapshot;
      revision.Payload = CardGroupingRevisionCodec.SerializeSnapshot(state);
      segment.Clear();
      segment.Add(revision);
    }
    if (segment.Count > 0 && segment[^1].RevisionType != CardGroupingRevisionType.Deleted)
      _ = Replay(grouping, segment)
        ?? throw new InvalidOperationException($"Revision {segment[^1].Id} has no replayable snapshot.");
  }

  /// <summary>
  /// Clears an applied deletion or a card edit whose source revision is still current.
  /// </summary>
  public async Task CompleteDeckSyncAsync(
    CollectionContext context,
    long groupingId,
    DeckSyncOperation operation,
    long revisionId,
    CancellationToken cancellationToken)
  {
    await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    var grouping = await context.CardGroupings.SingleOrDefaultAsync(
      candidate => candidate.Id == groupingId, cancellationToken);
    if (grouping == null || grouping.PendingOperation != operation) return;

    grouping.SyncError = null;
    if (operation == DeckSyncOperation.Delete ||
        await context.CardGroupingRevisions.Where(revision => revision.CardGroupingId == groupingId)
          .MaxAsync(revision => revision.Id, cancellationToken) == revisionId)
      grouping.PendingOperation = DeckSyncOperation.None;
    await context.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
  }

  public async Task<int> MarkMissingDeletedAsync(
    CollectionContext context,
    int accountId,
    IReadOnlySet<(CardGroupingKind Kind, int NetDeckId)> seen,
    DateTime observedAt,
    CancellationToken cancellationToken)
  {
    var missing = await context.CardGroupings
      .Where(grouping =>
        grouping.AccountId == accountId &&
        !grouping.IsDeleted &&
        !grouping.IsLocal)
      .ToListAsync(cancellationToken);
    missing.RemoveAll(grouping =>
      seen.Contains((grouping.Kind, grouping.NetDeckId)));
    if (missing.Count == 0)
      return 0;

    await using var transaction =
      await context.Database.BeginTransactionAsync(cancellationToken);
    foreach (var grouping in missing)
    {
      grouping.IsDeleted = true;
      context.CardGroupingRevisions.Add(new CardGroupingRevisionModel
      {
        CardGroupingId = grouping.Id,
        ObservedAt = observedAt,
        RevisionType = CardGroupingRevisionType.Deleted,
        Payload = null,
      });
    }

    await context.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);
    return missing.Count;
  }

  public async Task<int> PurgeRedundantRevisionsAsync(
    CollectionContext context,
    CancellationToken cancellationToken = default)
  {
    var allGroupings = await context.CardGroupings
      .Select(g => g.Id)
      .ToListAsync(cancellationToken);

    int totalPurged = 0;

    foreach (long groupingId in allGroupings)
    {
      var revisions = await context.CardGroupingRevisions
        .Where(r => r.CardGroupingId == groupingId && r.RevisionType != CardGroupingRevisionType.Deleted)
        .OrderBy(r => r.Id)
        .ToListAsync(cancellationToken);

      if (revisions.Count <= 1) continue;

      var grouping = await context.CardGroupings
        .SingleOrDefaultAsync(g => g.Id == groupingId, cancellationToken);
      if (grouping == null) continue;

      var toDelete = new List<CardGroupingRevisionModel>();
      CardGroupingState? previousState = null;
      var accumulatedModels = new List<CardGroupingRevisionModel>();

      for (int i = 0; i < revisions.Count; i++)
      {
        var rev = revisions[i];
        if (rev.RevisionType == CardGroupingRevisionType.Snapshot)
        {
          accumulatedModels.Clear();
        }
        accumulatedModels.Add(rev);

        var currentState = Replay(grouping, accumulatedModels.ToList());
        if (currentState == null) continue;

        bool isLatest = i == revisions.Count - 1;

        if (previousState != null && CardGroupingRevisionCodec.StateEquals(previousState, currentState))
        {
          if (!isLatest)
          {
            toDelete.Add(rev);
          }
        }
        else
        {
          previousState = currentState;
        }
      }

      if (toDelete.Count > 0)
      {
        var toDeleteIds = toDelete.Select(d => d.Id).ToList();

        var referencedIds = await context.DeckRevisionEnrichments
          .Where(e => toDeleteIds.Contains(e.CardGroupingRevisionId))
          .Select(e => e.CardGroupingRevisionId)
          .ToListAsync(cancellationToken);

        toDelete.RemoveAll(d => referencedIds.Contains(d.Id));

        if (toDelete.Count > 0)
        {
          context.CardGroupingRevisions.RemoveRange(toDelete);
          totalPurged += toDelete.Count;
        }
      }
    }

    if (totalPurged > 0)
    {
      await context.SaveChangesAsync(cancellationToken);
    }

    return totalPurged;
  }

  public static CardGroupingState? Replay(
    CardGroupingModel grouping,
    IReadOnlyList<CardGroupingRevisionModel> revisions)
  {
    int snapshotIndex = -1;
    for (int i = revisions.Count - 1; i >= 0; i--)
    {
      if (revisions[i].RevisionType == CardGroupingRevisionType.Deleted)
        return null;
      if (revisions[i].RevisionType == CardGroupingRevisionType.Snapshot)
      {
        snapshotIndex = i;
        break;
      }
    }

    if (snapshotIndex < 0 || revisions[snapshotIndex].Payload == null)
      return null;

    CardGroupingState state = CardGroupingRevisionCodec.DeserializeSnapshot(
      grouping.Kind,
      grouping.NetDeckId,
      revisions[snapshotIndex].Payload!);
    for (int i = snapshotIndex + 1; i < revisions.Count; i++)
    {
      var revision = revisions[i];
      if (revision.RevisionType == CardGroupingRevisionType.Deleted)
        return null;
      if (revision.Payload == null)
        throw new InvalidOperationException(
          $"Revision {revision.Id} has no replay payload.");

      state = revision.RevisionType switch
      {
        CardGroupingRevisionType.Snapshot =>
          CardGroupingRevisionCodec.DeserializeSnapshot(
            grouping.Kind,
            grouping.NetDeckId,
            revision.Payload),
        CardGroupingRevisionType.Delta =>
          CardGroupingRevisionCodec.ApplyDelta(state, revision.Payload),
        _ => throw new InvalidOperationException(
          $"Unknown revision type {revision.RevisionType}."),
      };
    }

    return state;
  }

  public static bool ShouldCheckpoint(
    IReadOnlyList<CardGroupingRevisionModel> revisions,
    int pendingDeltaLength)
  {
    int snapshotIndex = -1;
    for (int i = revisions.Count - 1; i >= 0; i--)
    {
      if (revisions[i].RevisionType == CardGroupingRevisionType.Snapshot)
      {
        snapshotIndex = i;
        break;
      }
      if (revisions[i].RevisionType == CardGroupingRevisionType.Deleted)
        return true;
    }

    if (snapshotIndex < 0 || revisions[snapshotIndex].Payload == null)
      return true;

    var deltas = revisions
      .Skip(snapshotIndex + 1)
      .Where(revision => revision.RevisionType == CardGroupingRevisionType.Delta)
      .ToArray();
    if (deltas.Length + 1 >= MaximumDeltasBeforeSnapshot)
      return true;

    long deltaBytes = pendingDeltaLength;
    foreach (var delta in deltas)
      deltaBytes += delta.Payload?.Length ?? 0;

    return deltaBytes >= revisions[snapshotIndex].Payload!.Length;
  }

  private static void ApplyMetadata(
    CardGroupingModel grouping,
    CardGroupingState state)
  {
    grouping.Timestamp = state.Kind == CardGroupingKind.Collection
      ? null
      : state.Timestamp;
    grouping.Name = state.Kind == CardGroupingKind.Collection ? null : state.Name;
    grouping.FormatCode = state.Kind == CardGroupingKind.Collection
      ? null
      : state.FormatCode;
  }

  private static void ValidateState(CardGroupingState state)
  {
    if (state.Kind == CardGroupingKind.Collection)
    {
      if (state.NetDeckId != 0)
        throw new InvalidOperationException("Collection must use NetDeckId zero.");
      return;
    }

    if (state.NetDeckId <= 0)
      throw new InvalidOperationException(
        "Decks, binders, and wishlist require a positive NetDeckId.");
  }
}
