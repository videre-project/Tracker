/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace Tracker.Database.Models.Trades;

public sealed class TradeEscrowErrorModel
{
  public long Id { get; set; }
  public long TradeEscrowId { get; set; }
  public DateTime ObservedAt { get; set; }
  public int ErrorCode { get; set; }

  public TradeEscrowModel TradeEscrow { get; set; } = null!;
}
