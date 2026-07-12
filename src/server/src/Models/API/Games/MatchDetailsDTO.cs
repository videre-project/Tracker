/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;


namespace Tracker.Models.API.Games;

public class MatchDetailsDTO
{
  public int Id { get; set; }
  public int EventId { get; set; }
  public required string EventName { get; set; }
  public required string Format { get; set; }
  public DateTime StartTime { get; set; }
  public required string Result { get; set; }
  public required string Record { get; set; }
  public required string Duration { get; set; }
  public string? DeckHash { get; set; }
  public string? DeckName { get; set; }
  public string? DeckArchetype { get; set; }
  public List<string>? DeckColors { get; set; }
  public string? OpponentName { get; set; }
  public string? OpponentDeckName { get; set; }
  public string? OpponentDeckArchetype { get; set; }
  public List<string>? OpponentDeckColors { get; set; }
  public bool IsActive { get; set; }

  public List<GameDetailsDTO> Games { get; set; } = new();
}
