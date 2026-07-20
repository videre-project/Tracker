/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using MTGOSDK.API.Collection;

using Tracker.Database.Models.Collection;


namespace Tracker.Services.MTGO.Collection;

public sealed class CollectionSnapshotReader
{
  public IReadOnlyList<CardGroupingState> ReadAll()
  {
    var states = new Dictionary<(CardGroupingKind Kind, int NetDeckId), CardGroupingState>();

    Add(states, ReadCollection());

    foreach (var deck in CollectionManager.Decks)
    {
      if (deck.NetDeckId > 0)
        Add(states, ReadDeck(deck));
    }

    foreach (var binder in CollectionManager.Binders)
    {
      if (binder.IsMegaBinder || binder.NetDeckId <= 0)
        continue;

      Add(states, ReadBinder(binder));
    }

    Binder wishList = CollectionManager.WishList;
    if (!wishList.IsMegaBinder && wishList.NetDeckId > 0)
      Add(states, ReadBinder(wishList));

    return states.Values
      .OrderBy(state => state.Kind)
      .ThenBy(state => state.NetDeckId)
      .ToArray();
  }

  public CardGroupingState? TryRead(
    CardGroupingKind kind,
    int netDeckId)
  {
    try
    {
      CardGroupingState? state = kind switch
      {
        CardGroupingKind.Collection when netDeckId == 0 => ReadCollection(),
        CardGroupingKind.Deck when netDeckId > 0 =>
          ReadDeck(CollectionManager.GetDeck(netDeckId)),
        CardGroupingKind.Binder when netDeckId > 0 =>
          ReadBinder(CollectionManager.GetBinder(netDeckId)),
        CardGroupingKind.Wishlist when netDeckId > 0 =>
          ReadBinder(CollectionManager.WishList),
        _ => null,
      };
      return state?.NetDeckId == netDeckId ? state : null;
    }
    catch
    {
      return null;
    }
  }

  private static CardGroupingState ReadCollection()
  {
    var items = CollectionManager.Collection.GetItemSnapshot()
      .Where(item => item.CatalogId > 0 && item.Quantity > 0)
      .Select(item => new CardGroupingItemState(
        item.CatalogId,
        (int)item.Region,
        item.Annotation,
        item.Quantity))
      .ToArray();

    return new CardGroupingState(
      CardGroupingKind.Collection,
      0,
      null,
      null,
      null,
      items).Normalize();
  }

  public CardGroupingState ReadDeck(Deck deck)
  {
    var items = ReadItems(deck.GetItemSnapshot());

    return new CardGroupingState(
      CardGroupingKind.Deck,
      deck.NetDeckId,
      deck.Timestamp,
      deck.Name,
      deck.Format?.Code,
      items).Normalize();
  }

  private static CardGroupingState ReadBinder(Binder binder)
  {
    IReadOnlyList<CardGroupingItemState> items =
      ReadItems(binder.GetItemSnapshot());

    return new CardGroupingState(
      binder.IsWishList
        ? CardGroupingKind.Wishlist
        : CardGroupingKind.Binder,
      binder.NetDeckId,
      binder.Timestamp,
      binder.Name,
      binder.Format?.Code,
      items).Normalize();
  }

  private static IReadOnlyList<CardGroupingItemState> ReadItems(
    IReadOnlyList<CardGroupingItemSnapshot> items) =>
    items
      .Where(item => item.CatalogId > 0 && item.Quantity > 0)
      .Select(item => new CardGroupingItemState(
        item.CatalogId,
        (int)item.Region,
        item.Annotation,
        item.Quantity))
      .ToArray();

  private static void Add(
    IDictionary<(CardGroupingKind Kind, int NetDeckId), CardGroupingState> states,
    CardGroupingState state) =>
    states[(state.Kind, state.NetDeckId)] = state;
}
