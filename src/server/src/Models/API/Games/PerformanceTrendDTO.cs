/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;


namespace Tracker.Models.API.Games;

public class PerformanceTrendDTO
{
  public required string Date { get; set; }
  public required DateTime RawDate { get; set; }
  public double? Winrate { get; set; }
  public int Matches { get; set; }
  public double? RollingAvg { get; set; }
  public double[]? Ci95 { get; set; }
  public double[]? Ci80 { get; set; }
  public double[]? Ci50 { get; set; }
}
