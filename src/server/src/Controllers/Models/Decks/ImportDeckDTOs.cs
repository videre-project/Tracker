/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;


namespace Tracker.Controllers.Models.Decks;

public sealed class ImportDeckCardDTO
{
  public required int CatalogId { get; set; }
  public required string Name { get; set; }
  public required int Quantity { get; set; }
  public int Cmc { get; set; }
  public List<string> Colors { get; set; } = [];
  public List<string> Types { get; set; } = [];
  public string Rarity { get; set; } = "common";
}

public sealed class ImportDeckRequest
{
  public required string Name { get; set; }
  public required string Format { get; set; }
  public string? Archetype { get; set; }
  public List<ImportDeckCardDTO> Mainboard { get; set; } = [];
  public List<ImportDeckCardDTO> Sideboard { get; set; } = [];
}

public sealed class ImportDeckResponse
{
  public required long RevisionId { get; set; }
  public required int NetDeckId { get; set; }
  public required bool Created { get; set; }
}
