/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;

using MTGOSDK.API.Collection;
using MTGOSDK.API.Trade.Enums;


namespace Tracker.Controllers.Models.Trades;

/// <summary>
/// Immutable projection used by MTGOSDK's bulk marketplace serializer.
/// </summary>
public interface ITradePostSnapshot
{
  string PosterName { get; }
  TradePostFormat Format { get; }
  string Message { get; }
  IEnumerable<CardQuantityPair>? Wanted { get; }
  IEnumerable<CardQuantityPair>? Offered { get; }
}
