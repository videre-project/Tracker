/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.Controllers.Models.Decks;

public class DeckIdentifierDTO
{
  public required long RevisionId { get; set; }
  public required int NetDeckId { get; set; }
  public required string Name { get; set; }
  public required string Format { get; set; }
}
