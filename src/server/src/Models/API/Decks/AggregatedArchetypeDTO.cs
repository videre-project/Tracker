/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;


namespace Tracker.Models.API.Decks;

public class AggregatedArchetypeDTO
{
  public required string Archetype { get; set; }
  public required List<string> Colors { get; set; }
  public required int Matches { get; set; }
  public required int Wins { get; set; }
  public required int Losses { get; set; }
  public required double Winrate { get; set; }
  public required string TopCard { get; set; }
  public required double TopCardAvgScore { get; set; }
  public required double TopCardAvgQuantity { get; set; }
}
