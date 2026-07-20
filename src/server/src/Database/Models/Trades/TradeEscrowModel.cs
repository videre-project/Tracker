/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;


namespace Tracker.Database.Models.Trades;

public enum TradeEscrowKind
{
  Player = 0,
  NonPlayer = 1,
}

public enum TradeEscrowResult
{
  InProgress = 0,
  Completed = 1,
  Cancelled = 2,
  Failed = 3,
  ClosedUnknown = 4,
  Interrupted = 5,
}

public enum TradeAttributionStatus
{
  NotApplicable = 0,
  Pending = 1,
  Inferred = 2,
  InferredAmbiguous = 3,
  Unavailable = 4,
}

public sealed class TradeEscrowModel
{
  public long Id { get; set; }
  public int AccountId { get; set; }
  public Guid Token { get; set; }
  public int? EscrowId { get; set; }
  public TradeEscrowKind Kind { get; set; }
  public int? PartnerId { get; set; }
  public string? PartnerName { get; set; }
  public DateTime StartedAt { get; set; }
  public DateTime? ClosedAt { get; set; }
  public int State { get; set; }
  public TradeEscrowResult Result { get; set; }
  public TradeAttributionStatus AttributionStatus { get; set; }

  public AccountModel Account { get; set; } = null!;
  public ICollection<TradeEscrowItemModel> Items { get; set; } = [];
  public ICollection<TradeEscrowMessageModel> Messages { get; set; } = [];
  public ICollection<TradeEscrowErrorModel> Errors { get; set; } = [];
}
