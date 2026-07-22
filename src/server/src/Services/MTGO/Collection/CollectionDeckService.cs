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

using Tracker.Controllers.Models.Decks;
using Tracker.Database;
using Tracker.Database.Models;
using Tracker.Database.Models.Collection;
using Tracker.Services.Videre;
using static Tracker.Services.DatabaseService;


namespace Tracker.Services.MTGO.Collection;

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

/// <summary>
/// Minimal card-definition projection used by MTGOSDK's bulk serializer.
/// </summary>
public interface IDeckCatalogData
{
  int Id { get; }
  string Name { get; }
  string Colors { get; }
}

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
        enrichment,
        allowRemoteCatalogLookup: false);
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
        enrichment,
        allowRemoteCatalogLookup: false);

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
        CatalogData catalogData = ResolveCatalogData(catalogId, allowRemoteLookup: true);
        List<string> colorList = !string.IsNullOrWhiteSpace(catalogData.Colors)
          ? catalogData.Colors.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList()
          : new List<string>();

        changes.Add(new DeckHistoryChangeView(
          catalogId,
          name,
          delta,
          zone,
          catalogData.Cmc,
          colorList,
          catalogData.Types,
          catalogData.Rarity));
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

  private static async Task<DeckRevisionView?> LoadRevisionAsync(
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
    return MaterializeRevision(
      target.CardGrouping,
      revisions,
      target.DeckEnrichment);
  }

  private static DeckRevisionView? MaterializeRevision(
    CardGroupingModel grouping,
    IReadOnlyList<CardGroupingRevisionModel> revisions,
    DeckRevisionEnrichmentModel? enrichment,
    bool allowRemoteCatalogLookup = true)
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

    var mainboard = BuildEntries(
      state,
      DeckRegion.MainDeck,
      allowRemoteCatalogLookup);
    var sideboard = BuildEntries(
      state,
      DeckRegion.Sideboard,
      allowRemoteCatalogLookup);
    var colors = ResolveColors(
      mainboard,
      allowRemoteCatalogLookup);

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
    DeckRegion region,
    bool allowRemoteCatalogLookup) =>
    state.Items
      .Where(item => item.Region == (int)region && item.Quantity > 0)
      .Select(item => {
        var catalogData = ResolveCatalogData(item.CatalogId, allowRemoteCatalogLookup);
        var colorList = !string.IsNullOrWhiteSpace(catalogData.Colors)
          ? catalogData.Colors.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList()
          : new List<string>();
        return new CardEntry(
          item.CatalogId,
          catalogData.Name,
          item.Quantity,
          catalogData.Cmc,
          colorList,
          catalogData.Types,
          catalogData.Rarity);
      })
      .ToList();

  private static CatalogData ResolveCatalogData(
    int catalogId,
    bool allowRemoteLookup = true)
  {
    if (s_catalog.TryGetValue(catalogId, out CatalogData? cached))
      return cached;

    if (!allowRemoteLookup)
    {
      return new CatalogData(
        catalogId.ToString(CultureInfo.InvariantCulture),
        0,
        null,
        new List<string>(),
        "common");
    }

    try
    {
      Card card = CollectionManager.GetCard(catalogId);
      var catalog = new CatalogData(
        card.Name,
        card.ConvertedManaCost,
        card.Colors,
        card.Types?.ToList() ?? new List<string>(),
        card.Rarity ?? "common");
      s_catalog.TryAdd(catalogId, catalog);
      return catalog;
    }
    catch (Exception ex)
    {
      Log.Trace(
        "Could not resolve catalog ID {CatalogId} while replaying a deck revision: {Message}",
        catalogId,
        ex.Message);
      return new CatalogData(
        catalogId.ToString(CultureInfo.InvariantCulture),
        0,
        null,
        new List<string>(),
        "common");
    }
  }

  private static List<string> ResolveColors(
    IEnumerable<CardEntry> mainboard,
    bool allowRemoteCatalogLookup)
  {
    var colors = new HashSet<char>();
    foreach (CardEntry entry in mainboard)
    {
      string? cardColors = ResolveCatalogData(
        entry.catalogId,
        allowRemoteCatalogLookup).Colors;
      if (string.IsNullOrEmpty(cardColors))
        continue;

      foreach (char color in cardColors)
      {
        if (VidereCardColors.IsCanonical(color))
          colors.Add(color);
      }
    }
    return VidereCardColors.Normalize(colors).ToList();
  }

  private static async Task WarmCurrentCatalogAsync(
    IReadOnlyList<CardGroupingModel> groupings,
    IReadOnlyDictionary<long, List<CardGroupingRevisionModel>> revisionsByGrouping,
    CancellationToken cancellationToken)
  {
    foreach (CardGroupingModel grouping in groupings)
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (!revisionsByGrouping.TryGetValue(grouping.Id, out var revisions))
        continue;

      CardGroupingState? state = CollectionHistoryWriter.Replay(
        grouping,
        revisions);
      if (state == null ||
          state.Items.All(item => s_catalog.ContainsKey(item.CatalogId)))
      {
        continue;
      }

      try
      {
        Deck deck = CollectionManager.GetDeck(grouping.NetDeckId);
        IList<IDeckCatalogData> cards =
          await deck.SerializeItemsAsAsync<IDeckCatalogData>();
        foreach (IDeckCatalogData card in cards)
        {
          if (card.Id <= 0)
            continue;

          s_catalog.TryAdd(
            card.Id,
            new CatalogData(
              string.IsNullOrWhiteSpace(card.Name)
                ? card.Id.ToString(CultureInfo.InvariantCulture)
                : card.Name,
              0,
              card.Colors,
              new List<string>(),
              "common"));
        }
      }
      catch (Exception ex)
      {
        Log.Trace(
          "Could not bulk-resolve catalog metadata for deck {NetDeckId}: {Message}",
          grouping.NetDeckId,
          ex.Message);
      }
    }
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
      _ when code.Length > 1 &&
             (code[0] == 'C' || code[0] == 'c') &&
             code.Skip(1).All(char.IsLetter) => code[1..],
      _ => code,
    };

    return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(
      compact.ToLowerInvariant());
  }
}
