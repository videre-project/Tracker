/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using MTGOSDK.API.Collection;


namespace Tracker.Database.Models;

[method: JsonConstructor]
public record CardEntry(int catalogId, string name, int quantity)
{
  public CardEntry(int catalogId, int quantity) :
      this(catalogId, CollectionManager.GetCard(catalogId).Name, quantity) { }
}

public class DeckModel
{
  public required int Id { get; set; }
  public required string Name { get; set; }
  public required string Format { get; set; }

  public required string Hash { get; set; }
  public required DateTime Timestamp { get; set; }

  public required List<CardEntry> Mainboard { get; set; }
  public required List<CardEntry> Sideboard { get; set; }

  public static DeckModel ToModel(Deck deck)
  {
    var model = new DeckModel
    {
      Id = deck.DeckId,
      Name = deck.Name,
      Format = deck.Format!,
      Timestamp = deck.Timestamp,
      Hash = deck.Hash,
      Mainboard = new(),
      Sideboard = new()
    };

    foreach (var card in deck.GetCards(DeckRegion.MainDeck))
    {
      model.Mainboard.Add(new CardEntry(card.Id, card.Card, card.Quantity));
    }

    foreach (var card in deck.GetCards(DeckRegion.Sideboard))
    {
      model.Sideboard.Add(new CardEntry(card.Id, card.Card, card.Quantity));
    }

    return model;
  }
}
