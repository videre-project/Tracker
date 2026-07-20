/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.Database.Models.Trades;

public enum TradeEscrowItemRole
{
  LocalOffer = 0,
  RemoteOffer = 1,
  InferredOutput = 2,
}

public sealed class TradeEscrowItemModel
{
  public long TradeEscrowId { get; set; }
  public TradeEscrowItemRole Role { get; set; }
  public int CatalogId { get; set; }
  public int Quantity { get; set; }

  public TradeEscrowModel TradeEscrow { get; set; } = null!;
}
