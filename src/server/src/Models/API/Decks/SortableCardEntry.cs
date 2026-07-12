/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;


namespace Tracker.Models.API.Decks;

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
