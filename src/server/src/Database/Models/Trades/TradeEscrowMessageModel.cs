/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace Tracker.Database.Models.Trades;

public sealed class TradeEscrowMessageModel
{
  public long Id { get; set; }
  public long TradeEscrowId { get; set; }
  public DateTime Timestamp { get; set; }
  public int? SenderId { get; set; }
  public string? SenderName { get; set; }
  public required string Text { get; set; }

  public TradeEscrowModel TradeEscrow { get; set; } = null!;
}
