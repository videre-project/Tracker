/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace Tracker.Controllers.Models.Replays;

public class ReplayLogDTO
{
  public DateTime Timestamp { get; set; }
  public required string Data { get; set; }
}
