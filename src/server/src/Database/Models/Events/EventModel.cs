/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;


namespace Tracker.Database.Models.Events;

public class EventModel
{
  public required int Id { get; set; }

  public required string Format { get; set; }

  public required string Description { get; set; }
  public DateTime StartTime { get; set; }
  public DateTime? EndTime { get; set; }

  /// <summary>
  /// Reference to Collection.db/CardGroupingRevisions.Id.
  /// </summary>
  /// <remarks>
  /// This is a logical referencea as SQLite cannot enforce a foreign key
  /// across the two database files.
  /// </remarks>
  public long? DeckRevisionId { get; set; }

  public List<MatchModel> Matches { get; set; } = new();
}
