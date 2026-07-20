/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

namespace Tracker.Database.Models.Collection;

public sealed class DeckRevisionEnrichmentModel
{
  public long CardGroupingRevisionId { get; set; }
  public string? Archetype { get; set; }
  public string? FeaturedCard { get; set; }

  public CardGroupingRevisionModel CardGroupingRevision { get; set; } = null!;
}
