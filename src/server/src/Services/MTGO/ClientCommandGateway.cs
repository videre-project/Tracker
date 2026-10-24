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
using MTGOSDK.API.Play;

using MTGOTracker.Services.MTGO.Collection;


namespace MTGOTracker.Services.MTGO;

/// <summary>
/// Applies Tracker-owned commands to the connected MTGO client.
/// </summary>
public sealed class ClientCommandGateway : IClientCommandGateway
{
  private static readonly TimeSpan s_importTimeout = TimeSpan.FromSeconds(30);

  public Task OpenEventAsync(
    int eventId,
    CancellationToken cancellationToken = default) =>
    Task.CompletedTask;

  public async Task<DeckSyncResult> ImportDeckAsync(
    DeckSyncState deck,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(deck);
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
      new Deck(mainboard, sideboard, deck.Name, format),
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

    return new DeckSyncResult(imported.NetDeckId, imported.Name);
  }

  public Task UpdateDeckAsync(
    DeckSyncState deck,
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

  private static CardQuantityPair ToCardQuantityPair(DeckSyncCard card) =>
    new(card.CatalogId, card.Quantity, card.Annotation);
}
