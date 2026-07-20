/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

using Tracker.Database.Models;


namespace Tracker.Controllers.Models.Decks;

public class DeckDetailDTO
{
  public required long RevisionId { get; set; }
  public required int NetDeckId { get; set; }
  public required string Name { get; set; }
  public required string Format { get; set; }
  public required DateTime Timestamp { get; set; }
  public required List<CardEntry> Mainboard { get; set; }
  public required List<CardEntry> Sideboard { get; set; }
}
