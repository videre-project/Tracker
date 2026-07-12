/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.Models.API.Games;

public class DashboardStatsDTO
{
  public double OverallWinrate { get; set; }
  public int TotalMatches { get; set; }
  public int Wins { get; set; }
  public int Losses { get; set; }
  public int Ties { get; set; }
  public double PlayWinrate { get; set; }
  public int PlayMatches { get; set; }
  public double DrawWinrate { get; set; }
  public int DrawMatches { get; set; }
  public string AverageDuration { get; set; } = "0m 0s";
  public string DurationTwoGames { get; set; } = "0m 0s";
  public string DurationThreeGames { get; set; } = "0m 0s";
}
