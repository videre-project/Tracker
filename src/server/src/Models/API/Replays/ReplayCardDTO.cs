/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.Models.API.Replays;

public class ReplayCardDTO
{
  public int CardId { get; set; }
  public required string Name { get; set; }
  public string? RulesText { get; set; }
  public string? ManaCost { get; set; }
  public int? CatalogId { get; set; }
  public required string InitialZone { get; set; }
  public string? InitialPower { get; set; }
  public string? InitialToughness { get; set; }
  public int OwnerId { get; set; }
  public int? SourceId { get; set; }
  public bool IsTapped { get; set; }
  public bool IsToken { get; set; }
  public bool IsLand { get; set; }
  public bool IsActivatedAbility { get; set; }
  public bool IsTriggeredAbility { get; set; }
  public int FirstSeenSnapshotIndex { get; set; }
}
