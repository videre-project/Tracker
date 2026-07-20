/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace Tracker.Database.Models.Collection;

public enum CardGroupingRevisionType
{
  Snapshot = 0,
  Delta = 1,
  Deleted = 2,
}

public sealed class CardGroupingRevisionModel
{
  public long Id { get; set; }
  public long CardGroupingId { get; set; }
  public DateTime ObservedAt { get; set; }
  public CardGroupingRevisionType RevisionType { get; set; }
  public byte[]? Payload { get; set; }

  public CardGroupingModel CardGrouping { get; set; } = null!;
  public DeckRevisionEnrichmentModel? DeckEnrichment { get; set; }
}
