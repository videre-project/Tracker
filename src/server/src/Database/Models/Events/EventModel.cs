/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;


namespace Tracker.Database.Models.Events;


public enum EventType
{
  League,
  Tournament,
  Match
}

public class EventModel
{
  public required int Id { get; set; }

  public required string Format { get; set; }

  public required EventType Type { get; set; }

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

  /// <summary>
  /// The ID of the league this course is associated with.
  /// </summary>
  /// <remarks>
  /// This stores the ID of the league in MTGO's system for identifying the event.
  /// We otherwise use the CourseID as the primary key for the event in our database,
  /// using this field to link them as league IDs persist over the whole season.
  /// </remarks>
  public int? LeagueEventId { get; set; } 

  public List<MatchModel> Matches { get; set; } = new();
}
