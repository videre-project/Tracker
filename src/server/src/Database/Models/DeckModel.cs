/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

using Microsoft.EntityFrameworkCore;

using MTGOSDK.API.Collection;


namespace Tracker.Database.Models;

public record CardEntry(int catalogId, string name, int quantity);

public class DeckModel
{
  public int Id { get; set; }
  public string Name { get; set; }
  public string Format { get; set; }

  public DateTime Timestamp { get; set; }
  public string Hash { get; set; }

  public List<CardEntry> Mainboard { get; set; }
  public List<CardEntry> Sideboard { get; set; }

  public DeckModel(Deck deck)
  {
    Id = deck.DeckId;
    Name = deck.Name;
    Format = deck.Format!;

    Timestamp = deck.Timestamp;
    Hash = deck.Hash;

    Mainboard = new List<CardEntry>();
    foreach (var card in deck.GetCards(DeckRegion.MainDeck))
    {
      Mainboard.Add(new CardEntry(card.Id, card.Card, card.Quantity));
    }

    Sideboard = new List<CardEntry>();
    foreach (var card in deck.GetCards(DeckRegion.Sideboard))
    {
      Sideboard.Add(new CardEntry(card.Id, card.Card, card.Quantity));
    }
  }
}
