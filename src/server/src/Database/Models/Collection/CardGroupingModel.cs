/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;


namespace Tracker.Database.Models.Collection;

public enum CardGroupingKind
{
  Collection = 0,
  Deck = 1,
  Binder = 2,
  Wishlist = 3,
}

public enum DeckSyncOperation
{
  None = 0,
  Upsert = 1,
  Delete = 2,
}

public sealed class CardGroupingModel
{
  public long Id { get; set; }
  public int AccountId { get; set; }
  public CardGroupingKind Kind { get; set; }
  public int NetDeckId { get; set; }
  public DateTime? Timestamp { get; set; }
  public string? Name { get; set; }
  public string? FormatCode { get; set; }
  public bool IsDeleted { get; set; }
  /// <summary>
  /// True for Tracker-owned records that have not been reconciled to MTGO.
  /// </summary>
  public bool IsLocal { get; set; }
  /// <summary>
  /// The pending outbound operation that must be applied to MTGO.
  /// </summary>
  public DeckSyncOperation PendingOperation { get; set; }
  /// <summary>
  /// The most recent outbound reconciliation error, if any.
  /// </summary>
  public string? SyncError { get; set; }

  public AccountModel Account { get; set; } = null!;
  public ICollection<CardGroupingRevisionModel> Revisions { get; set; } = [];
}
