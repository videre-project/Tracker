/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace Tracker.Models.API.Replays;

public class ReplayLogDTO
{
  public DateTime Timestamp { get; set; }
  public required string Data { get; set; }
}
