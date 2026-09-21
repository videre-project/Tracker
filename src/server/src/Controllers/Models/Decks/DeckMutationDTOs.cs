/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;


namespace Tracker.Controllers.Models.Decks;

public enum DeckCardZone
{
  Mainboard = 0,
  Sideboard = 1,
}

public sealed class DeckCardMutationRequest
{
  public required int CatalogId { get; set; }
  public string? Name { get; set; }
  public int Quantity { get; set; } = 1;
  public DeckCardZone Zone { get; set; }
  public uint Annotation { get; set; }
  public int Cmc { get; set; }
  public List<string> Colors { get; set; } = [];
  public List<string> Types { get; set; } = [];
  public string Rarity { get; set; } = "common";
}

public sealed class DeckMutationResponse
{
  public required long RevisionId { get; set; }
  public required int NetDeckId { get; set; }
  public required bool Deleted { get; set; }
}

public sealed class DeckCardMoveRequest
{
  public required DeckCardZone FromZone { get; set; }
  public required DeckCardZone ToZone { get; set; }
  public required List<DeckCardMoveItem> Cards { get; set; }
}

public sealed class DeckCardMoveItem
{
  public required int CatalogId { get; set; }
  public int Quantity { get; set; } = 1;
}
