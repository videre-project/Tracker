/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace Tracker.Controllers.Models.Replays;

public class ReplayActionDTO
{
  public required string ActionType { get; set; }
  public string? ActionName { get; set; }
  public int? CardId { get; set; }
  public string? CardName { get; set; }
  public string? Targets { get; set; }
  public required string Data { get; set; }
  public DateTime ClientTimestamp { get; set; }
  public int Nonce { get; set; }
}
