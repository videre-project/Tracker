/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MTGOSDK.API.Collection;
using MTGOSDK.API.Interface.ViewModels;
using MTGOSDK.API.Play.Leagues;
using MTGOSDK.API.Play;
using static MTGOSDK.Core.Reflection.DLRWrapper;

using MTGOTracker.Services.MTGO.Collection;


namespace MTGOTracker.Services.MTGO;

/// <summary>
/// Applies Tracker-owned commands to the connected MTGO client.
/// </summary>
public sealed class ClientCommandGateway(CollectionSnapshotReader snapshotReader) : IClientCommandGateway
{
  private static readonly TimeSpan s_importTimeout = TimeSpan.FromSeconds(30);

  public Task OpenEventAsync(
    int eventId,
    CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    try
    {
      EventManager.NavigateToEvent(eventId);
    }
    catch (KeyNotFoundException)
    {
      // League courses are maintained separately from the event manager's
      // match/tournament/queue lookup, but use the same navigation command.
      var league = LeagueManager.OpenLeagues.FirstOrDefault(item => item.Id == eventId);
      if (league is null) throw;
      using var viewModel = new BasicToastViewModel("", "");
      viewModel.SetNavigateToViewCommand(league);
      viewModel.ExecuteViewCommand();
    }
    return Task.CompletedTask;
  }

  /// <summary>
  /// Resolves a queued import by its chosen name, format, and original cards.
  /// A conflicting list must not be overwritten or mistaken for this import.
  /// </summary>
  public CardGroupingState? FindDeck(CardGroupingState deck)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(deck.Name);
    if (CollectionManager.GetUniqueDeckName(deck.Name) == deck.Name) return null;

    // MTGO enforces case-insensitive unique names. Look up the persisted native
    // spelling; a reserved name can still be waiting for its server ID.
    Deck? native = Try<Deck>(() => CollectionManager.GetDeck(deck.Name));
    if (native == null || native.NetDeckId <= 0)
      throw new InvalidOperationException($"MTGO has not finished registering deck '{deck.Name}'.");

    var existing = snapshotReader.ReadDeck(native);
    if (!CardGroupingRevisionCodec.StateEquals(deck with { NetDeckId = existing.NetDeckId, Name = existing.Name }, existing))
      throw new InvalidOperationException($"MTGO deck '{deck.Name}' has different cards or format than the queued import.");
    return existing;
  }

  public async Task<CardGroupingState> ImportDeckAsync(
    CardGroupingState deck,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(deck);
    cancellationToken.ThrowIfCancellationRequested();
    if (FindDeck(deck) is { } existing) return existing;
    var mainboard = deck.Items
      .Where(item => item.Region == (int)DeckRegion.MainDeck)
      .Select(ToCardQuantityPair)
      .ToArray();
    var sideboard = deck.Items
      .Where(item => item.Region == (int)DeckRegion.Sideboard)
      .Select(ToCardQuantityPair)
      .ToArray();

    PlayFormat format = DeckFormatResolver.Resolve(deck.FormatCode);
    Deck imported = CollectionManager.ImportDeck(
      new Deck(mainboard, sideboard, deck.Name!, format),
      DeckImportMode.Add);
    DateTime deadline = DateTime.UtcNow + s_importTimeout;
    while (imported.NetDeckId <= 0 && DateTime.UtcNow < deadline)
    {
      await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
    }
    if (imported.NetDeckId <= 0)
    {
      throw new TimeoutException(
        $"MTGO did not assign an ID to imported deck '{imported.Name}'.");
    }

    return snapshotReader.ReadDeck(imported);
  }

  public Task UpdateDeckAsync(
    CardGroupingState deck,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(deck);
    cancellationToken.ThrowIfCancellationRequested();
    Deck existing = CollectionManager.GetDeck(deck.NetDeckId);
    existing.ReplaceCards(
      deck.Items.Select(item => new CardGroupingItemSnapshot(
        item.CatalogId,
        (DeckRegion)item.Region,
        item.Annotation,
        item.Quantity)),
      enforceLegality: false);
    CollectionManager.SavePendingUpdates();
    return Task.CompletedTask;
  }

  public Task DeleteDeckAsync(
    int netDeckId,
    CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    try
    {
      CollectionManager.DeleteDeck(CollectionManager.GetDeck(netDeckId));
    }
    catch (KeyNotFoundException)
    {
      // Deletion is idempotent: the deck may already have disappeared from
      // MTGO while the Tracker operation was queued.
    }
    return Task.CompletedTask;
  }

  private static CardQuantityPair ToCardQuantityPair(CardGroupingItemState card) =>
    new(card.CatalogId, card.Quantity, card.Annotation);
}
