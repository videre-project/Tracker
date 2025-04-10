/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;


namespace Tracker.Database.Models;

public class EventModel
{
  public required int Id { get; set; }

  public required string Format { get; set; }

  public required string Description { get; set; }

  public string? DeckHash { get; set; }
  public DeckModel? Deck { get; set; }

  public List<MatchModel> Matches { get; set; } = new();
}
