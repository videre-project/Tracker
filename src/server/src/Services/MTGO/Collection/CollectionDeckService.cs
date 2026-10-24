/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using MTGOSDK.API.Collection;
using MTGOSDK.Core.Logging;

using MTGOTracker.Controllers.Models.Decks;
using MTGOTracker.Database;
using MTGOTracker.Database.Models;
using MTGOTracker.Database.Models.Collection;
using MTGOTracker.Services.Videre;
using static MTGOTracker.Services.DatabaseService;


namespace MTGOTracker.Services.MTGO.Collection;

public sealed record DeckRevisionView(
  long RevisionId,
  long CardGroupingId,
  int AccountId,
  int NetDeckId,
  DateTime ObservedAt,
  DateTime Timestamp,
  string Name,
  string Format,
  List<CardEntry> Mainboard,
  List<CardEntry> Sideboard,
  List<string> Colors,
  string? Archetype,
  string? FeaturedCard);

public sealed class CollectionDeckService(
  IServiceScopeFactory scopeFactory,
  DatabaseReadiness<CollectionContext> databaseReadiness,
  IClientAPIProvider clientProvider,
  CollectionHistoryWriter historyWriter,
  CollectionSnapshotReader snapshotReader,
  ICollectionStateFeed stateFeed)
{
  private sealed record CatalogData(
    string Name,
    int Cmc,
    string? Colors,
    List<string> Types,
    string Rarity);

  private static readonly TimeSpan s_initialReconciliationTimeout =
    TimeSpan.FromSeconds(30);
  private static readonly ConcurrentDictionary<int, CatalogData> s_catalog = new();
  private static readonly ConcurrentDictionary<int, List<string>> s_cardTypes = new();

  public long ResolveRevision(Deck deck) =>
    ResolveRevisionAsync(deck).GetAwaiter().GetResult();

  public async Task<long> ResolveRevisionAsync(
    Deck deck,
    CancellationToken cancellationToken = default)
  {
    await databaseReadiness.WaitAsync(cancellationToken);
    UserIdentity identity = clientProvider.CurrentUser
      ?? throw new InvalidOperationException(
        "Cannot resolve an event deck without an authoritative user identity.");

    int netDeckId = deck.NetDeckId;
    if (netDeckId <= 0)
      throw new InvalidOperationException(
        "The registered deck does not have a positive NetDeckId.");

    CardGroupingState state = snapshotReader.ReadDeck(deck);
    if (state.NetDeckId != netDeckId)
      throw new InvalidOperationException(
        "The registered deck snapshot does not match its NetDeckId.");

    await using var scope = scopeFactory.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
    await historyWriter.UpsertAccountAsync(context, identity, cancellationToken);
    return await historyWriter.ReconcileAndGetRevisionAsync(
      context,
      identity.Id,
      state,
      DateTime.UtcNow,
      cancellationToken);
  }

  /// <summary>
  /// Creates or updates the desired Tracker-local deck state in Collection.db.
  /// The outbound reconciliation service owns the MTGO boundary.
  /// </summary>
  public async Task<ImportDeckResponse> ImportLocalDeckAsync(
    ImportDeckRequest request,
    CancellationToken cancellationToken = default)
  {
    string name = request.Name.Trim();
    string format = request.Format.Trim();
    if (name.Length == 0)
      throw new ArgumentException("A deck name is required.", nameof(request));
    if (format.Length == 0)
      throw new ArgumentException("A deck format is required.", nameof(request));

    ImportDeckCardDTO[] cards = request.Mainboard
      .Concat(request.Sideboard)
      .Where(card => card.CatalogId > 0 && card.Quantity > 0)
      .ToArray();
    if (cards.Length == 0)
      throw new ArgumentException("At least one deck card is required.", nameof(request));

    await databaseReadiness.WaitAsync(cancellationToken);
    await clientProvider.WaitForClientReadyAsync(cancellationToken);
    UserIdentity identity = clientProvider.CurrentUser
      ?? throw new InvalidOperationException(
        "Cannot import a local deck without an authoritative user identity.");

    format = DeckFormatResolver.Resolve(format).Code;

    await using var scope = scopeFactory.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
    await historyWriter.UpsertAccountAsync(context, identity, cancellationToken);

    CardGroupingModel? existing = await context.CardGroupings
      .Where(grouping =>
        grouping.AccountId == identity.Id &&
        grouping.Kind == CardGroupingKind.Deck &&
        grouping.IsLocal &&
        !grouping.IsDeleted &&
        grouping.Name == name &&
        grouping.FormatCode == format)
      .OrderByDescending(grouping => grouping.Timestamp)
      .FirstOrDefaultAsync(cancellationToken);

    bool created = existing == null;
    int netDeckId = existing?.NetDeckId
      ?? await AllocateLocalNetDeckIdAsync(context, identity.Id, cancellationToken);
    DateTime observedAt = DateTime.UtcNow;
    var items = request.Mainboard
      .Where(card => card.CatalogId > 0 && card.Quantity > 0)
      .Select(card => new CardGroupingItemState(
        card.CatalogId,
        (int)DeckRegion.MainDeck,
        0,
        card.Quantity))
      .Concat(request.Sideboard
        .Where(card => card.CatalogId > 0 && card.Quantity > 0)
        .Select(card => new CardGroupingItemState(
          card.CatalogId,
          (int)DeckRegion.Sideboard,
          0,
          card.Quantity)))
      .ToArray();
    var state = new CardGroupingState(
      CardGroupingKind.Deck,
      netDeckId,
      null,
      name,
      format,
      items);

    long revisionId = await historyWriter.ReconcileAndGetRevisionAsync(
      context,
      identity.Id,
      state,
      observedAt,
      cancellationToken,
      isLocal: true);

    CardGroupingModel localGrouping = await context.CardGroupings.SingleAsync(
      grouping =>
        grouping.AccountId == identity.Id &&
        grouping.Kind == CardGroupingKind.Deck &&
        grouping.NetDeckId == netDeckId &&
        grouping.IsLocal,
      cancellationToken);
    localGrouping.PendingOperation = DeckSyncOperation.Upsert;
    localGrouping.SyncError = null;

    string? archetype = string.IsNullOrWhiteSpace(request.Archetype)
      ? null
      : request.Archetype.Trim();
    var enrichment = await context.DeckRevisionEnrichments.FindAsync(
      [revisionId],
      cancellationToken);
    if (enrichment == null)
    {
      context.DeckRevisionEnrichments.Add(new DeckRevisionEnrichmentModel
      {
        CardGroupingRevisionId = revisionId,
        Archetype = archetype,
        FeaturedCard = request.Mainboard.FirstOrDefault()?.Name,
      });
    }
    else
    {
      enrichment.Archetype = archetype;
      enrichment.FeaturedCard = request.Mainboard.FirstOrDefault()?.Name;
    }

    foreach (ImportDeckCardDTO card in cards)
    {
      s_catalog[card.CatalogId] = new CatalogData(
        string.IsNullOrWhiteSpace(card.Name)
          ? card.CatalogId.ToString(CultureInfo.InvariantCulture)
          : card.Name.Trim(),
        Math.Max(0, card.Cmc),
        string.Join(',', card.Colors),
        ResolveCardTypes(card.CatalogId, card.Types.SelectMany(ParseTypeLine)),
        string.IsNullOrWhiteSpace(card.Rarity) ? "common" : card.Rarity);
    }
    await context.SaveChangesAsync(cancellationToken);

    return new ImportDeckResponse
    {
      RevisionId = revisionId,
      NetDeckId = netDeckId,
      Created = created,
    };
  }

  public async Task<DeckMutationResponse> AddCardAsync(
    long revisionId,
    DeckCardMutationRequest request,
    CancellationToken cancellationToken = default) =>
    await MutateCardAsync(
      revisionId,
      request,
      remove: false,
      cancellationToken);

  public async Task<DeckMutationResponse> RemoveCardAsync(
    long revisionId,
    DeckCardMutationRequest request,
    CancellationToken cancellationToken = default) =>
    await MutateCardAsync(
      revisionId,
      request,
      remove: true,
      cancellationToken);

  public async Task<DeckMutationResponse> DeleteLocalDeckAsync(
    long revisionId,
    CancellationToken cancellationToken = default)
  {
    await databaseReadiness.WaitAsync(cancellationToken);
    await clientProvider.WaitForClientReadyAsync(cancellationToken);
    UserIdentity identity = clientProvider.CurrentUser
      ?? throw new InvalidOperationException(
        "Cannot delete a deck without an authoritative user identity.");

    await using var scope = scopeFactory.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
    await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    MutableDeck mutable = await LoadMutableDeckAsync(
      context,
      identity.Id,
      revisionId,
      cancellationToken);

    mutable.Grouping.IsDeleted = true;
    mutable.Grouping.PendingOperation = DeckSyncOperation.Delete;
    mutable.Grouping.SyncError = null;
    var deleted = new CardGroupingRevisionModel
    {
      CardGroupingId = mutable.Grouping.Id,
      ObservedAt = DateTime.UtcNow,
      RevisionType = CardGroupingRevisionType.Deleted,
    };
    context.CardGroupingRevisions.Add(deleted);
    await context.SaveChangesAsync(cancellationToken);
    await transaction.CommitAsync(cancellationToken);

    return new DeckMutationResponse
    {
      RevisionId = deleted.Id,
      NetDeckId = mutable.Grouping.NetDeckId,
      Deleted = true,
    };
  }

  public async Task<DeckMutationResponse> MoveCardsAsync(
    long revisionId,
    DeckCardMoveRequest request,
    CancellationToken cancellationToken = default)
  {
    int fromRegion = ToDeckRegion(request.FromZone);
    int toRegion = ToDeckRegion(request.ToZone);
    if (fromRegion == toRegion)
      throw new ArgumentException("Source and destination zones must differ.", nameof(request));
    if (request.Cards is not { Count: > 0 } ||
        request.Cards.Any(card => card is null || card.CatalogId <= 0 || card.Quantity <= 0))
      throw new ArgumentException("Positive catalog IDs and quantities are required.", nameof(request));

    await databaseReadiness.WaitAsync(cancellationToken);
    await clientProvider.WaitForClientReadyAsync(cancellationToken);
    UserIdentity identity = clientProvider.CurrentUser
      ?? throw new InvalidOperationException("Cannot modify a deck without an authoritative user identity.");

    await using var scope = scopeFactory.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
    MutableDeck mutable = await LoadMutableDeckAsync(context, identity.Id, revisionId, cancellationToken);
    CardGroupingState current = mutable.State.Normalize();
    var items = current.Items.ToDictionary(item => (item.CatalogId, item.Region, item.Annotation));

    foreach (var group in request.Cards.GroupBy(card => card.CatalogId))
    {
      int remaining = group.Sum(card => card.Quantity);
      var source = items.Values
        .Where(item => item.CatalogId == group.Key && item.Region == fromRegion)
        .OrderBy(item => item.Annotation)
        .ToArray();
      if (source.Sum(item => item.Quantity) < remaining)
        throw new InvalidOperationException("The source zone does not contain enough copies to move.");

      foreach (var item in source)
      {
        int quantity = Math.Min(item.Quantity, remaining);
        var sourceKey = (item.CatalogId, fromRegion, item.Annotation);
        if (quantity == item.Quantity) items.Remove(sourceKey);
        else items[sourceKey] = item with { Quantity = item.Quantity - quantity };
        var targetKey = (item.CatalogId, toRegion, item.Annotation);
        int targetQuantity = items.GetValueOrDefault(targetKey)?.Quantity ?? 0;
        items[targetKey] = item with { Region = toRegion, Quantity = checked(targetQuantity + quantity) };
        remaining -= quantity;
        if (remaining == 0) break;
      }
    }

    return await SaveMutationAsync(context, mutable, current with
    {
      Timestamp = DateTime.UtcNow,
      Items = items.Values.ToArray(),
    }, identity.Id, cancellationToken);
  }

  private static int ToDeckRegion(DeckCardZone zone) => zone switch
  {
    DeckCardZone.Mainboard => (int)DeckRegion.MainDeck,
    DeckCardZone.Sideboard => (int)DeckRegion.Sideboard,
    _ => throw new ArgumentOutOfRangeException(nameof(zone), zone,
      "Only mainboard and sideboard mutations are supported."),
  };

  private async Task<DeckMutationResponse> MutateCardAsync(
    long revisionId,
    DeckCardMutationRequest request,
    bool remove,
    CancellationToken cancellationToken)
  {
    if (request.CatalogId <= 0)
      throw new ArgumentOutOfRangeException(
        nameof(request),
        "A positive MTGO catalog ID is required.");
    if (request.Quantity <= 0)
      throw new ArgumentOutOfRangeException(
        nameof(request),
        "The card quantity must be positive.");

    await databaseReadiness.WaitAsync(cancellationToken);
    await clientProvider.WaitForClientReadyAsync(cancellationToken);
    UserIdentity identity = clientProvider.CurrentUser
      ?? throw new InvalidOperationException(
        "Cannot modify a deck without an authoritative user identity.");

    await using var scope = scopeFactory.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
    MutableDeck mutable = await LoadMutableDeckAsync(
      context,
      identity.Id,
      revisionId,
      cancellationToken);

    int region = ToDeckRegion(request.Zone);
    CardGroupingState current = mutable.State.Normalize();
    var items = current.Items.ToDictionary(item =>
      (item.CatalogId, item.Region, item.Annotation));
    var key = (request.CatalogId, region, request.Annotation);
    if (remove)
    {
      var matchingKeys = items.Keys
        .Where(candidate =>
          candidate.CatalogId == request.CatalogId &&
          candidate.Region == region &&
          (request.Annotation == 0 || candidate.Annotation == request.Annotation))
        .OrderBy(candidate => candidate.Annotation == request.Annotation ? 0 : 1)
        .ThenBy(candidate => candidate.Annotation)
        .ToArray();
      int available = matchingKeys.Sum(candidate => items[candidate].Quantity);
      if (available < request.Quantity)
      {
        throw new InvalidOperationException(
          $"The deck only contains {available} matching copies to remove.");
      }

      int remaining = request.Quantity;
      foreach (var matchingKey in matchingKeys)
      {
        CardGroupingItemState item = items[matchingKey];
        int removed = Math.Min(item.Quantity, remaining);
        int nextQuantity = item.Quantity - removed;
        if (nextQuantity == 0)
          items.Remove(matchingKey);
        else
          items[matchingKey] = item with { Quantity = nextQuantity };
        remaining -= removed;
        if (remaining == 0)
          break;
      }
    }
    else
    {
      int currentQuantity = items.GetValueOrDefault(key)?.Quantity ?? 0;
      items[key] = new CardGroupingItemState(
        request.CatalogId,
        region,
        request.Annotation,
        checked(currentQuantity + request.Quantity));
    }

    var desired = current with
    {
      Timestamp = DateTime.UtcNow,
      Items = items.Values.ToArray(),
    };
    CacheCatalogData(request);
    return await SaveMutationAsync(context, mutable, desired, identity.Id, cancellationToken);
  }

  private async Task<DeckMutationResponse> SaveMutationAsync(
    CollectionContext context,
    MutableDeck mutable,
    CardGroupingState desired,
    int accountId,
    CancellationToken cancellationToken)
  {
    // Save the outbound marker in the same transaction as the revision so an
    // incoming MTGO observation cannot replace an edit before it is queued.
    mutable.Grouping.PendingOperation = DeckSyncOperation.Upsert;
    mutable.Grouping.SyncError = null;
    long nextRevisionId = await historyWriter.ReconcileAndGetRevisionAsync(
      context,
      accountId,
      desired,
      DateTime.UtcNow,
      cancellationToken,
      isLocal: mutable.Grouping.IsLocal,
      allowPendingWrite: true,
      expectedRevisionId: mutable.Revision.Id);

    CopyEnrichment(context, mutable.Revision, nextRevisionId);
    await context.SaveChangesAsync(cancellationToken);

    return new DeckMutationResponse
    {
      RevisionId = nextRevisionId,
      NetDeckId = mutable.Grouping.NetDeckId,
      Deleted = false,
    };
  }

  private static async Task<MutableDeck> LoadMutableDeckAsync(
    CollectionContext context,
    int accountId,
    long revisionId,
    CancellationToken cancellationToken)
  {
    CardGroupingRevisionModel? target = await context.CardGroupingRevisions
      .Include(revision => revision.CardGrouping)
      .Include(revision => revision.DeckEnrichment)
      .SingleOrDefaultAsync(
        revision =>
          revision.Id == revisionId &&
          revision.CardGrouping.AccountId == accountId &&
          revision.CardGrouping.Kind == CardGroupingKind.Deck,
        cancellationToken);
    if (target == null ||
        target.RevisionType == CardGroupingRevisionType.Deleted ||
        target.CardGrouping.IsDeleted)
    {
      throw new KeyNotFoundException($"Deck revision {revisionId} was not found.");
    }
    if (target.CardGrouping.PendingOperation == DeckSyncOperation.Delete)
      throw new InvalidOperationException("The deck is pending deletion.");

    var intervening = await context.CardGroupingRevisions
      .Where(revision => revision.CardGroupingId == target.CardGroupingId && revision.Id > revisionId)
      .ToListAsync(cancellationToken);
    if (intervening.Any(revision => !CardGroupingRevisionCodec.IsMetadataOnly(revision)))
    {
      throw new InvalidOperationException(
        "The deck changed after this revision was loaded. Reload it and try again.");
    }

    long? snapshotId = await context.CardGroupingRevisions
      .Where(revision =>
        revision.CardGroupingId == target.CardGroupingId &&
        revision.Id <= revisionId &&
        revision.RevisionType == CardGroupingRevisionType.Snapshot)
      .Select(revision => (long?)revision.Id)
      .MaxAsync(cancellationToken);
    if (!snapshotId.HasValue)
      throw new InvalidOperationException("The deck has no replayable snapshot.");

    List<CardGroupingRevisionModel> revisions = await context.CardGroupingRevisions
      .AsNoTracking()
      .Where(revision =>
        revision.CardGroupingId == target.CardGroupingId &&
        revision.Id >= snapshotId.Value &&
        revision.Id <= revisionId)
      .OrderBy(revision => revision.Id)
      .ToListAsync(cancellationToken);
    CardGroupingState state = CollectionHistoryWriter.Replay(
      target.CardGrouping,
      revisions)
      ?? throw new InvalidOperationException("The deck revision could not be replayed.");
    return new MutableDeck(target.CardGrouping, target, state);
  }

  private static void CopyEnrichment(
    CollectionContext context,
    CardGroupingRevisionModel source,
    long destinationRevisionId)
  {
    if (source.DeckEnrichment == null || source.Id == destinationRevisionId)
      return;

    context.DeckRevisionEnrichments.Add(new DeckRevisionEnrichmentModel
    {
      CardGroupingRevisionId = destinationRevisionId,
      Archetype = source.DeckEnrichment.Archetype,
      FeaturedCard = source.DeckEnrichment.FeaturedCard,
    });
  }

  private void CacheCatalogData(DeckCardMutationRequest card)
  {
    var incoming = new CatalogData(
      string.IsNullOrWhiteSpace(card.Name)
        ? card.CatalogId.ToString(CultureInfo.InvariantCulture)
        : card.Name.Trim(),
      Math.Max(0, card.Cmc),
      string.Join(',', card.Colors),
      ResolveCardTypes(card.CatalogId, card.Types.SelectMany(ParseTypeLine)),
      string.IsNullOrWhiteSpace(card.Rarity) ? "common" : card.Rarity);
    s_catalog.AddOrUpdate(
      card.CatalogId,
      incoming,
      (_, existing) => MergeCatalogData(existing, incoming));
  }

  public void EnrichSearchCardTypes(IEnumerable<CardSearchResultDTO> cards)
  {
    foreach (CardSearchResultDTO card in cards)
      card.Types = ResolveCardTypes(card.MtgoId, ParseTypeLine(card.Type));
  }

  private List<string> ResolveCardTypes(int catalogId, IEnumerable<string> fallback)
  {
    if (s_cardTypes.TryGetValue(catalogId, out var cached))
      return cached;
    if (clientProvider.IsReady)
    {
      try
      {
        // MTGO's type flags include supertypes such as Basic and Snow, which
        // the external search catalog may omit from its display type line.
        return s_cardTypes.GetOrAdd(catalogId, id => CollectionManager.GetCard(id).Types.ToList());
      }
      catch (KeyNotFoundException)
      {
        // The search catalog can contain printings absent from this client.
      }
    }
    return fallback.Distinct().ToList();
  }

  private static CatalogData MergeCatalogData(
    CatalogData existing,
    CatalogData incoming)
  {
    return new CatalogData(
      IsPlaceholderName(incoming.Name)
        ? existing.Name
        : incoming.Name,
      incoming.Cmc > 0 ? incoming.Cmc : existing.Cmc,
      string.IsNullOrWhiteSpace(incoming.Colors) ? existing.Colors : incoming.Colors,
      incoming.Types.Count > 0 ? incoming.Types : existing.Types,
      string.Equals(incoming.Rarity, "common", StringComparison.OrdinalIgnoreCase) &&
      !string.Equals(existing.Rarity, "common", StringComparison.OrdinalIgnoreCase)
        ? existing.Rarity
        : incoming.Rarity);
  }

  private static bool IsPlaceholderName(string name) =>
    string.IsNullOrWhiteSpace(name) ||
    int.TryParse(name, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

  private sealed record MutableDeck(
    CardGroupingModel Grouping,
    CardGroupingRevisionModel Revision,
    CardGroupingState State);

  private static async Task<int> AllocateLocalNetDeckIdAsync(
    CollectionContext context,
    int accountId,
    CancellationToken cancellationToken)
  {
    int candidate = await context.CardGroupings
      .Where(grouping => grouping.AccountId == accountId && grouping.IsLocal)
      .Select(grouping => (int?)grouping.NetDeckId)
      .MinAsync(cancellationToken) is int minimum
        ? minimum - 1
        : int.MaxValue;

    while (candidate > 0 && await context.CardGroupings.AnyAsync(
      grouping =>
        grouping.AccountId == accountId &&
        grouping.Kind == CardGroupingKind.Deck &&
        grouping.NetDeckId == candidate,
      cancellationToken))
    {
      candidate--;
    }

    return candidate > 0
      ? candidate
      : throw new InvalidOperationException("No local deck identifiers are available.");
  }

  public async Task<IReadOnlyList<DeckRevisionView>> GetCurrentDecksAsync(
    CancellationToken cancellationToken = default)
  {
    await databaseReadiness.WaitAsync(cancellationToken);
    await clientProvider.WaitForClientReadyAsync(cancellationToken);
    UserIdentity identity = clientProvider.CurrentUser
      ?? throw new InvalidOperationException(
        "Cannot list decks without an authoritative user identity.");

    CollectionStateSnapshot? collectionState = stateFeed.Current;
    if (collectionState?.AccountId != identity.Id)
    {
      collectionState = await stateFeed.WaitForCurrentStateAsync(
        identity.Id,
        s_initialReconciliationTimeout,
        cancellationToken);
    }
    cancellationToken.ThrowIfCancellationRequested();
    if (collectionState == null)
    {
      throw new TimeoutException(
        "The initial MTGO collection reconciliation did not complete.");
    }

    await using var scope = scopeFactory.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
    var groupings = await context.CardGroupings
      .AsNoTracking()
      .Where(grouping =>
        grouping.AccountId == identity.Id &&
        grouping.Kind == CardGroupingKind.Deck &&
        !grouping.IsDeleted)
      .ToListAsync(cancellationToken);
    if (groupings.Count == 0)
      return [];

    long[] groupingIds = groupings.Select(grouping => grouping.Id).ToArray();
    var latestSnapshots = context.CardGroupingRevisions
      .AsNoTracking()
      .Where(revision =>
        groupingIds.Contains(revision.CardGroupingId) &&
        revision.RevisionType == CardGroupingRevisionType.Snapshot)
      .GroupBy(revision => revision.CardGroupingId)
      .Select(revisions => new
      {
        CardGroupingId = revisions.Key,
        SnapshotId = revisions.Max(revision => revision.Id),
      });

    var replayRevisions = await context.CardGroupingRevisions
      .AsNoTracking()
      .Join(
        latestSnapshots,
        revision => revision.CardGroupingId,
        snapshot => snapshot.CardGroupingId,
        (revision, snapshot) => new { Revision = revision, snapshot.SnapshotId })
      .Where(row => row.Revision.Id >= row.SnapshotId)
      .Select(row => row.Revision)
      .OrderBy(revision => revision.CardGroupingId)
      .ThenBy(revision => revision.Id)
      .ToListAsync(cancellationToken);

    var revisionsByGrouping = replayRevisions
      .GroupBy(revision => revision.CardGroupingId)
      .ToDictionary(group => group.Key, group => group.ToList());
    long[] latestRevisionIds = revisionsByGrouping.Values
      .Where(revisions => revisions.Count > 0)
      .Select(revisions => revisions[^1].Id)
      .ToArray();
    var enrichments = await context.DeckRevisionEnrichments
      .AsNoTracking()
      .Where(enrichment =>
        latestRevisionIds.Contains(enrichment.CardGroupingRevisionId))
      .ToDictionaryAsync(
        enrichment => enrichment.CardGroupingRevisionId,
        cancellationToken);

    await WarmCurrentCatalogAsync(
      groupings,
      revisionsByGrouping,
      cancellationToken);

    var decks = new List<DeckRevisionView>(groupings.Count);
    foreach (CardGroupingModel grouping in groupings)
    {
      if (!revisionsByGrouping.TryGetValue(grouping.Id, out var revisions))
        continue;

      DeckRevisionEnrichmentModel? enrichment = null;
      if (revisions.Count > 0)
        enrichments.TryGetValue(revisions[^1].Id, out enrichment);

      DeckRevisionView? deck = MaterializeRevision(
        grouping,
        revisions,
        enrichment);
      if (deck != null)
        decks.Add(deck);
    }
    return decks;
  }

  public async Task<DeckRevisionView?> GetRevisionAsync(
    long revisionId,
    CancellationToken cancellationToken = default)
  {
    await databaseReadiness.WaitAsync(cancellationToken);
    await using var scope = scopeFactory.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
    return await LoadRevisionAsync(context, revisionId, cancellationToken);
  }

  public Task EnsureCatalogAsync(
    IEnumerable<int> catalogIds,
    CancellationToken cancellationToken = default) =>
    HydrateCatalogFromVidereAsync(catalogIds, cancellationToken);

  public CardEntry GetCatalogCard(int catalogId, int quantity = 0)
  {
    CatalogData data = ResolveCatalogData(catalogId);
    return new CardEntry(
      catalogId,
      data.Name,
      quantity,
      data.Cmc,
      SplitStoredColors(data.Colors),
      data.Types,
      data.Rarity);
  }

  public async Task<DeckHistoryView?> GetDeckHistoryAsync(
    long revisionId,
    CancellationToken cancellationToken = default)
  {
    await databaseReadiness.WaitAsync(cancellationToken);
    await using var scope = scopeFactory.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();

    var targetRevision = await context.CardGroupingRevisions
      .AsNoTracking()
      .Where(r => r.Id == revisionId && r.CardGrouping.Kind == CardGroupingKind.Deck)
      .Select(r => new { r.CardGroupingId, r.Id })
      .FirstOrDefaultAsync(cancellationToken);
    if (targetRevision == null) return null;

    long groupingId = targetRevision.CardGroupingId;

    var grouping = await context.CardGroupings
      .AsNoTracking()
      .SingleOrDefaultAsync(g => g.Id == groupingId, cancellationToken);
    if (grouping == null) return null;

    var allRevisionModels = await context.CardGroupingRevisions
      .AsNoTracking()
      .Where(r => r.CardGroupingId == groupingId && r.RevisionType != CardGroupingRevisionType.Deleted)
      .OrderBy(r => r.Id)
      .ToListAsync(cancellationToken);

    if (allRevisionModels.Count == 0) return null;

    await HydrateCatalogFromVidereAsync(
      CollectCatalogIds(grouping, allRevisionModels),
      cancellationToken);

    var allRevisionIds = allRevisionModels.Select(r => r.Id).ToList();
    var enrichments = await context.DeckRevisionEnrichments
      .AsNoTracking()
      .Where(e => allRevisionIds.Contains(e.CardGroupingRevisionId))
      .ToDictionaryAsync(e => e.CardGroupingRevisionId, cancellationToken);

    var materializedRevisions = new List<DeckRevisionView>();
    var accumulatedModels = new List<CardGroupingRevisionModel>();

    for (int i = 0; i < allRevisionModels.Count; i++)
    {
      var revModel = allRevisionModels[i];
      if (revModel.RevisionType == CardGroupingRevisionType.Snapshot)
      {
        accumulatedModels.Clear();
      }
      accumulatedModels.Add(revModel);

      enrichments.TryGetValue(revModel.Id, out var enrichment);
      var deckView = MaterializeRevision(
        grouping,
        accumulatedModels.ToList(),
        enrichment);

      if (deckView != null)
      {
        materializedRevisions.Add(deckView);
      }
    }

    if (materializedRevisions.Count == 0) return null;

    var latestRevision = materializedRevisions[^1];

    // Filter materialized revisions to only keep revisions where card composition actually changed
    var filteredRevisions = new List<DeckRevisionView>();
    DeckRevisionView? previousKept = null;

    for (int i = 0; i < materializedRevisions.Count; i++)
    {
      var cur = materializedRevisions[i];
      bool isLatest = i == materializedRevisions.Count - 1;

      if (previousKept == null)
      {
        filteredRevisions.Add(cur);
        previousKept = cur;
      }
      else
      {
        var cardChanges = ComputeDeltas(cur, previousKept);
        if (cardChanges.Count > 0)
        {
          filteredRevisions.Add(cur);
          previousKept = cur;
        }
        else if (isLatest)
        {
          // Replace previous kept entry with latest so we always reference the latest revision ID & timestamp
          filteredRevisions[^1] = cur;
        }
      }
    }

    var historyRevisions = new List<DeckHistoryRevisionView>();
    for (int i = 0; i < filteredRevisions.Count; i++)
    {
      var cur = filteredRevisions[i];
      var prev = i > 0 ? filteredRevisions[i - 1] : null;

      var changesFromPrev = prev != null ? ComputeDeltas(cur, prev) : new List<DeckHistoryChangeView>();
      var changesFromLatest = cur.RevisionId != latestRevision.RevisionId
        ? ComputeDeltas(cur, latestRevision)
        : new List<DeckHistoryChangeView>();

      historyRevisions.Add(new DeckHistoryRevisionView(
        cur.RevisionId,
        cur.CardGroupingId,
        cur.ObservedAt,
        cur.Timestamp,
        cur.Name,
        cur.Format,
        cur.Mainboard.Sum(c => c.quantity),
        cur.Sideboard.Sum(c => c.quantity),
        cur.Colors,
        cur.Archetype,
        cur.FeaturedCard,
        cur.Mainboard,
        cur.Sideboard,
        changesFromPrev,
        changesFromLatest));
    }

    historyRevisions.Reverse();

    return new DeckHistoryView(
      latestRevision.RevisionId,
      groupingId,
      latestRevision.Name,
      latestRevision.Format,
      historyRevisions);
  }

  private static List<DeckHistoryChangeView> ComputeDeltas(
    DeckRevisionView source,
    DeckRevisionView target)
  {
    var changes = new List<DeckHistoryChangeView>();
    ComputeZoneDeltas(source.Mainboard, target.Mainboard, "Mainboard", changes);
    ComputeZoneDeltas(source.Sideboard, target.Sideboard, "Sideboard", changes);
    return changes;
  }

  private static void ComputeZoneDeltas(
    List<CardEntry> sourceEntries,
    List<CardEntry> targetEntries,
    string zone,
    List<DeckHistoryChangeView> changes)
  {
    var sourceMap = sourceEntries
      .GroupBy(c => c.catalogId)
      .ToDictionary(g => g.Key, g => (Card: g.First(), Qty: g.Sum(c => c.quantity)));
    var targetMap = targetEntries
      .GroupBy(c => c.catalogId)
      .ToDictionary(g => g.Key, g => (Card: g.First(), Qty: g.Sum(c => c.quantity)));

    var allCatalogIds = sourceMap.Keys.Union(targetMap.Keys).Distinct();
    foreach (int catalogId in allCatalogIds)
    {
      sourceMap.TryGetValue(catalogId, out var src);
      targetMap.TryGetValue(catalogId, out var tgt);

      int delta = src.Qty - tgt.Qty;
      if (delta != 0)
      {
        string name = !string.IsNullOrWhiteSpace(src.Card?.name) ? src.Card.name : tgt.Card?.name ?? "";
        CardEntry? card = tgt.Card ?? src.Card;
        if (card == null)
          continue;
        changes.Add(new DeckHistoryChangeView(
          catalogId,
          name,
          delta,
          zone,
          card.cmc,
          card.colors ?? [],
          card.types ?? [],
          card.rarity));
      }
    }
  }

  public async Task<IReadOnlyDictionary<long, DeckRevisionView>> GetRevisionsAsync(
    IEnumerable<long> revisionIds,
    CancellationToken cancellationToken = default)
  {
    await databaseReadiness.WaitAsync(cancellationToken);
    await using var scope = scopeFactory.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
    var result = new Dictionary<long, DeckRevisionView>();
    foreach (long revisionId in revisionIds.Distinct())
    {
      DeckRevisionView? revision =
        await LoadRevisionAsync(context, revisionId, cancellationToken);
      if (revision != null)
        result[revisionId] = revision;
    }
    return result;
  }

  public async Task<IReadOnlyDictionary<long, long>> GetCardGroupingIdsAsync(
    IEnumerable<long> revisionIds,
    CancellationToken cancellationToken = default)
  {
    long[] ids = revisionIds.Distinct().ToArray();
    if (ids.Length == 0)
      return new Dictionary<long, long>();

    await databaseReadiness.WaitAsync(cancellationToken);
    await using var scope = scopeFactory.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
    return await context.CardGroupingRevisions
      .AsNoTracking()
      .Where(revision =>
        ids.Contains(revision.Id) &&
        revision.CardGrouping.Kind == CardGroupingKind.Deck)
      .ToDictionaryAsync(
        revision => revision.Id,
        revision => revision.CardGroupingId,
        cancellationToken);
  }
  public async Task<IReadOnlyDictionary<long, long>> GetRevisionGroupingIdsAsync(
    IEnumerable<long> cardGroupingIds,
    CancellationToken cancellationToken = default)
  {
    long[] ids = cardGroupingIds.Distinct().ToArray();
    if (ids.Length == 0)
      return new Dictionary<long, long>();

    await databaseReadiness.WaitAsync(cancellationToken);
    await using var scope = scopeFactory.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
    return await context.CardGroupingRevisions
      .AsNoTracking()
      .Where(revision => ids.Contains(revision.CardGroupingId))
      .ToDictionaryAsync(
        revision => revision.Id,
        revision => revision.CardGroupingId,
        cancellationToken);
  }

  public async Task SetEnrichmentAsync(
    long revisionId,
    string? archetype,
    string? featuredCard,
    CancellationToken cancellationToken = default)
  {
    await databaseReadiness.WaitAsync(cancellationToken);
    await using var scope = scopeFactory.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
    var enrichment = await context.DeckRevisionEnrichments.FindAsync(
      [revisionId],
      cancellationToken);
    if (enrichment == null)
    {
      bool isDeckRevision = await context.CardGroupingRevisions
        .AnyAsync(
          revision =>
            revision.Id == revisionId &&
            revision.CardGrouping.Kind == CardGroupingKind.Deck,
          cancellationToken);
      if (!isDeckRevision)
        throw new InvalidOperationException(
          $"Card grouping revision {revisionId} is not a deck revision.");

      enrichment = new DeckRevisionEnrichmentModel
      {
        CardGroupingRevisionId = revisionId,
      };
      context.DeckRevisionEnrichments.Add(enrichment);
    }

    enrichment.Archetype = archetype;
    enrichment.FeaturedCard = featuredCard;
    await context.SaveChangesAsync(cancellationToken);
  }

  private async Task<DeckRevisionView?> LoadRevisionAsync(
    CollectionContext context,
    long revisionId,
    CancellationToken cancellationToken)
  {
    var target = await context.CardGroupingRevisions
      .AsNoTracking()
      .Include(revision => revision.CardGrouping)
      .Include(revision => revision.DeckEnrichment)
      .SingleOrDefaultAsync(
        revision => revision.Id == revisionId,
        cancellationToken);
    if (target == null ||
        target.RevisionType == CardGroupingRevisionType.Deleted ||
        target.CardGrouping.Kind != CardGroupingKind.Deck)
    {
      return null;
    }

    long? snapshotId = await context.CardGroupingRevisions
      .Where(revision =>
        revision.CardGroupingId == target.CardGroupingId &&
        revision.Id <= revisionId &&
        revision.RevisionType == CardGroupingRevisionType.Snapshot)
      .Select(revision => (long?)revision.Id)
      .MaxAsync(cancellationToken);
    if (!snapshotId.HasValue)
      return null;

    var revisions = await context.CardGroupingRevisions
      .AsNoTracking()
      .Where(revision =>
        revision.CardGroupingId == target.CardGroupingId &&
        revision.Id >= snapshotId.Value &&
        revision.Id <= revisionId)
      .OrderBy(revision => revision.Id)
      .ToListAsync(cancellationToken);
    await HydrateCatalogFromVidereAsync(
      CollectCatalogIds(target.CardGrouping, revisions),
      cancellationToken);
    return MaterializeRevision(
      target.CardGrouping,
      revisions,
      target.DeckEnrichment);
  }

  private static DeckRevisionView? MaterializeRevision(
    CardGroupingModel grouping,
    IReadOnlyList<CardGroupingRevisionModel> revisions,
    DeckRevisionEnrichmentModel? enrichment)
  {
    if (revisions.Count == 0)
      return null;

    CardGroupingRevisionModel target = revisions[^1];
    if (target.RevisionType == CardGroupingRevisionType.Deleted)
      return null;

    CardGroupingState? state = CollectionHistoryWriter.Replay(
      grouping,
      revisions);
    if (state == null)
      return null;

    var mainboard = BuildEntries(state, DeckRegion.MainDeck);
    var sideboard = BuildEntries(state, DeckRegion.Sideboard);
    var colors = ResolveColors(mainboard);

    return new DeckRevisionView(
      target.Id,
      grouping.Id,
      grouping.AccountId,
      grouping.NetDeckId,
      target.ObservedAt,
      GetContentTimestamp(grouping, revisions, target.ObservedAt),
      state.Name ?? "",
      GetDisplayFormat(state.FormatCode),
      mainboard,
      sideboard,
      colors,
      enrichment?.Archetype,
      enrichment?.FeaturedCard);
  }

  private static DateTime GetContentTimestamp(
    CardGroupingModel grouping,
    IReadOnlyList<CardGroupingRevisionModel> revisions,
    DateTime fallback)
  {
    DateTime contentTimestamp = fallback;
    CardGroupingState? state = null;

    foreach (CardGroupingRevisionModel revision in revisions)
    {
      if (revision.RevisionType == CardGroupingRevisionType.Snapshot)
      {
        if (revision.Payload == null)
          continue;

        state = CardGroupingRevisionCodec.DeserializeSnapshot(
          grouping.Kind,
          grouping.NetDeckId,
          revision.Payload);
        // The grouping timestamp describes the deck state itself. The
        // revision observation time is only a fallback for missing metadata.
        contentTimestamp = state.Timestamp ?? revision.ObservedAt;
        continue;
      }

      if (revision.RevisionType != CardGroupingRevisionType.Delta ||
          revision.Payload == null ||
          state == null)
      {
        continue;
      }

      CardGroupingDeltaPayload delta =
        CardGroupingRevisionCodec.DeserializeDelta(revision.Payload);
      state = CardGroupingRevisionCodec.ApplyDelta(state, revision.Payload);
      if (delta.ItemChanges.Length > 0)
        contentTimestamp = state.Timestamp ?? revision.ObservedAt;
    }

    return contentTimestamp;
  }

  private static List<CardEntry> BuildEntries(
    CardGroupingState state,
    DeckRegion region) =>
    state.Items
      .Where(item => item.Region == (int)region && item.Quantity > 0)
      .Select(item => {
        CatalogData catalogData = ResolveCatalogData(item.CatalogId);
        return new CardEntry(
          item.CatalogId,
          catalogData.Name,
          item.Quantity,
          catalogData.Cmc,
          SplitStoredColors(catalogData.Colors),
          catalogData.Types,
          catalogData.Rarity);
      })
      .ToList();

  private static bool HasCompleteCatalogData(CatalogData data) =>
    data.Types.Count > 0 || data.Cmc > 0;

  private static CatalogData PlaceholderCatalogData(int catalogId) =>
    new(
      catalogId.ToString(CultureInfo.InvariantCulture),
      0,
      null,
      new List<string>(),
      "common");

  private static CatalogData ResolveCatalogData(int catalogId) =>
    s_catalog.TryGetValue(catalogId, out CatalogData? cached)
      ? cached
      : PlaceholderCatalogData(catalogId);

  private static List<string> SplitStoredColors(string? colors)
  {
    if (string.IsNullOrWhiteSpace(colors))
      return [];

    IEnumerable<string> values = colors.Contains(',')
      ? colors.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
      : colors.Select(color => color.ToString());
    return VidereCardColors.Normalize(values).ToList();
  }

  private static List<string> ParseTypeLine(string? typeLine)
  {
    if (string.IsNullOrWhiteSpace(typeLine))
      return [];

    int separator = typeLine.IndexOf('—');
    if (separator < 0)
      separator = typeLine.IndexOf(" - ", StringComparison.Ordinal);
    string front = separator < 0 ? typeLine : typeLine[..separator];
    return front
      .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
      .ToList();
  }

  private static CatalogData CatalogDataFromVidere(VidereCatalogMetadataResult card)
  {
    IReadOnlyList<string> colors = card.Colors is { Count: > 0 }
      ? VidereCardColors.Normalize(card.Colors).ToList()
      : [];
    return new CatalogData(
      string.IsNullOrWhiteSpace(card.Name)
        ? card.Id.ToString(CultureInfo.InvariantCulture)
        : card.Name,
      Math.Max(0, (int)Math.Floor(card.ManaValue ?? 0)),
      colors.Count == 0 ? null : string.Join(',', colors),
      ParseTypeLine(card.TypeLine),
      string.IsNullOrWhiteSpace(card.Rarity) ? "common" : card.Rarity);
  }

  private static IEnumerable<int> CollectCatalogIds(
    CardGroupingModel grouping,
    IReadOnlyList<CardGroupingRevisionModel> revisions)
  {
    var ids = new HashSet<int>();
    var accumulated = new List<CardGroupingRevisionModel>();
    foreach (CardGroupingRevisionModel revision in revisions)
    {
      if (revision.RevisionType == CardGroupingRevisionType.Snapshot)
        accumulated.Clear();
      accumulated.Add(revision);
      CardGroupingState? state = CollectionHistoryWriter.Replay(grouping, accumulated);
      if (state == null)
        continue;
      foreach (CardGroupingItemState item in state.Items)
      {
        if (item.CatalogId > 0)
          ids.Add(item.CatalogId);
      }
    }
    return ids;
  }

  private async Task HydrateCatalogFromVidereAsync(
    IEnumerable<int> catalogIds,
    CancellationToken cancellationToken)
  {
    int[] ids = catalogIds.Where(id => id > 0).Distinct().ToArray();
    int[] missing = ids.Where(id =>
        !s_catalog.TryGetValue(id, out CatalogData? cached) ||
        !HasCompleteCatalogData(cached))
      .ToArray();
    if (missing.Length > 0)
    {
      try
      {
        await using var scope = scopeFactory.CreateAsyncScope();
        var videreAPIClient = scope.ServiceProvider.GetRequiredService<VidereAPIClient>();
        IReadOnlyDictionary<int, VidereCatalogMetadataResult> metadata =
          await videreAPIClient.GetCatalogMetadataAsync(missing, cancellationToken);
        foreach (VidereCatalogMetadataResult card in metadata.Values)
        {
          if (card.Id <= 0)
            continue;
          CatalogData incoming = CatalogDataFromVidere(card);
          s_catalog.AddOrUpdate(
            card.Id,
            incoming,
            (_, existing) => MergeCatalogData(existing, incoming));
        }
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        throw;
      }
      catch (Exception ex)
      {
        Log.Trace(
          "Could not hydrate deck catalog metadata from the Videre API: {Message}",
          ex.Message);
      }
    }
    foreach (int id in ids)
    {
      if (s_catalog.TryGetValue(id, out CatalogData? card))
        s_catalog[id] = card with { Types = ResolveCardTypes(id, card.Types) };
    }
  }

  private static List<string> ResolveColors(IEnumerable<CardEntry> mainboard)
  {
    var colors = new HashSet<string>(StringComparer.Ordinal);
    foreach (CardEntry entry in mainboard)
    {
      foreach (string color in entry.colors ?? [])
        colors.Add(color);
      foreach (string color in SplitStoredColors(
        ResolveCatalogData(entry.catalogId).Colors))
      {
        colors.Add(color);
      }
    }
    return VidereCardColors.Normalize(colors).ToList();
  }

  private async Task WarmCurrentCatalogAsync(
    IReadOnlyList<CardGroupingModel> groupings,
    IReadOnlyDictionary<long, List<CardGroupingRevisionModel>> revisionsByGrouping,
    CancellationToken cancellationToken)
  {
    var catalogIds = new HashSet<int>();
    foreach (CardGroupingModel grouping in groupings)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (!revisionsByGrouping.TryGetValue(grouping.Id, out var revisions))
        continue;
      foreach (int catalogId in CollectCatalogIds(grouping, revisions))
        catalogIds.Add(catalogId);
    }

    await HydrateCatalogFromVidereAsync(catalogIds, cancellationToken);
  }

  private static string GetDisplayFormat(string? formatCode)
  {
    string code = formatCode?.Trim() ?? "";
    if (code.Length == 0)
      return "";

    string compact = code.ToUpperInvariant() switch
    {
      "C100S" => "100 Card Singleton",
      "CCMDR" => "Commander",
      "CCMDR1V1" => "Commander (1 v 1)",
      "CCMDRBRAWL" => "Brawl",
      "CCMDRDUEL" => "Duel Commander",
      "CFREECMD" => "Freeform Commander",
      "CFREEVAN" => "Freeform Vanguard",
      "CMOMIR" => "Momir Basic",
      "CPLANECHAS" => "Planechase",
      "CSINGLE" => "Standard Singleton",
      _ when code.Length > 1 && code == code.ToUpperInvariant() &&
             code[0] == 'C' &&
             code.Skip(1).All(char.IsLetter) => code[1..],
      _ => code,
    };

    return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
      compact.ToLowerInvariant());
  }
}
