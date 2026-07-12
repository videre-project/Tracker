/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

using Tracker.Database.Models;


namespace Tracker.Models.API.Decks;

public class DeckDTO
{
  public required string Hash { get; set; }
  public required int Id { get; set; }
  public required string Name { get; set; }
  public required string Format { get; set; }
  public required DateTime Timestamp { get; set; }
  public required int MainboardCount { get; set; }
  public required int SideboardCount { get; set; }
  public string? Archetype { get; set; }
  public List<string> Colors { get; set; } = new();
  public List<CardEntry> FeaturedCards { get; set; } = new();
}

public class DeckDetailDTO
{
  public required string Hash { get; set; }
  public required int Id { get; set; }
  public required string Name { get; set; }
  public required string Format { get; set; }
  public required DateTime Timestamp { get; set; }
  public required List<CardEntry> Mainboard { get; set; }
  public required List<CardEntry> Sideboard { get; set; }
}

public class AggregatedArchetypeDTO
{
  public required string Archetype { get; set; }
  public required List<string> Colors { get; set; }
  public required int Matches { get; set; }
  public required int Wins { get; set; }
  public required int Losses { get; set; }
  public required double Winrate { get; set; }
  public required string TopCard { get; set; }
  public required double TopCardAvgScore { get; set; }
  public required double TopCardAvgQuantity { get; set; }
}

public class DeckColorsDTO
{
  public required string Hash { get; set; }
  public required List<string> Colors { get; set; }
  public required string ColorString { get; set; }
}

  public class CardSearchResultDTO
  {
  public required string Id { get; set; }
  public required int MtgoId { get; set; }
  public required string SetCode { get; set; }
  public required string Name { get; set; }
  public required string Type { get; set; }
    public required string Text { get; set; }
    public List<string> Colors { get; set; } = new();
    public required string ImageUrl { get; set; }
    public string? Power { get; set; }
    public string? Toughness { get; set; }
    public string? Loyalty { get; set; }
    public string? Defense { get; set; }
  }

/// <summary>
/// Card entry with sortable metadata (CMC, Colors, Types)
/// </summary>
public class SortableCardEntry
{
  public int Index { get; set; }
  public int OriginalIndex { get; set; }
  public required int CatalogId { get; set; }
  public required string Name { get; set; }
  public int Quantity { get; set; }
  public int Cmc { get; set; }
  public List<string> Colors { get; set; } = new();
  public List<string> Types { get; set; } = new();
  public required string Rarity { get; set; }
  public string Zone { get; set; } = "Mainboard";
}

public class DeckIdentifierDTO
{
  public required string Hash { get; set; }
  public required int Id { get; set; }
  public required string Name { get; set; }
  public required string Format { get; set; }
}
