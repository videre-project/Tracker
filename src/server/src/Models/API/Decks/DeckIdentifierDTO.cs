/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.Models.API.Decks;

public class DeckIdentifierDTO
{
  public required string Hash { get; set; }
  public required int Id { get; set; }
  public required string Name { get; set; }
  public required string Format { get; set; }
}
