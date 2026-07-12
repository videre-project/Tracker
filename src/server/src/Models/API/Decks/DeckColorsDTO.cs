/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;


namespace Tracker.Models.API.Decks;

public class DeckColorsDTO
{
  public required string Hash { get; set; }
  public required List<string> Colors { get; set; }
  public required string ColorString { get; set; }
}
