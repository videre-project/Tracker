/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;


namespace Tracker.Models.API.Decks;

public class CardSearchResultDTO
{
  public required string Id { get; set; }
  public required int MtgoId { get; set; }
  public required string SetCode { get; set; }
  public required string Name { get; set; }
  public required string Type { get; set; }
  public required string Text { get; set; }
  public List<string> Colors { get; set; } = new();
  public required string ImageUrl { get; set; }
  public string? Power { get; set; }
  public string? Toughness { get; set; }
  public string? Loyalty { get; set; }
  public string? Defense { get; set; }
}
