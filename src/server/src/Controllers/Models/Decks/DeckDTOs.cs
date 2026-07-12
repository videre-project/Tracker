/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

using Tracker.Database.Models;


namespace Tracker.Controllers.Models.Decks;

public class DeckDTO
{
  public required string Hash { get; set; }
  public required int Id { get; set; }
  public required string Name { get; set; }
  public required string Format { get; set; }
  public required DateTime Timestamp { get; set; }
  public required int MainboardCount { get; set; }
  public required int SideboardCount { get; set; }
  public string? Archetype { get; set; }
  public List<string> Colors { get; set; } = new();
  public List<CardEntry> FeaturedCards { get; set; } = new();
}
