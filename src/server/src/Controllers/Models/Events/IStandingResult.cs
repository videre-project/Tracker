/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.Controllers.Models.Events;

public interface IStandingResult
{
  int Rank { get; }
  int Points { get; }
  string Record { get; }
  string OpponentMatchWinPercentage { get; }
  string GameWinPercentage { get; }
  string OpponentGameWinPercentage { get; }
}
