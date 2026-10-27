/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;


namespace MTGOTracker.Database.Models;

public sealed class SideboardGuideModel
{
  public int Id { get; set; }
  public int AccountId { get; set; }
  public long DeckRevisionId { get; set; }
  public required string OpponentArchetype { get; set; }
  public string Notes { get; set; } = string.Empty;
  public DateTime UpdatedAt { get; set; }
  public List<SideboardGuideCardModel> Cards { get; set; } = new();
}

public sealed class SideboardGuideCardModel
{
  public int Id { get; set; }
  public int SideboardGuideId { get; set; }
  public int CatalogId { get; set; }
  public int Quantity { get; set; }
  public bool BringIn { get; set; }
}
