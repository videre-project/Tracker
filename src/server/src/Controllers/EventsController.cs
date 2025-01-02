/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Mvc;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Tournaments;
using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace Tracker.Controllers;

public class EventSummary
{
  public int Id { get; set; }
  public string? Name { get; set; }
  public DateTime StartTime { get; set; }
  public DateTime EndTime { get; set; }
  public int MinPlayers { get; set; }
  public int MaxPlayers { get; set; }
  public int Players { get; set; }
  public int Rounds { get; set; }
  public bool IsCompleted { get; set; }
}

[Route("api/[controller]/[action]")]
public class EventsController : ControllerBase
{
  [HttpGet] // GET /api/events/geteventslist
  public IEnumerable<EventSummary> GetEventsList()
  {
    IEnumerable<Tournament> events = EventManager.Events
      .Where(e => e != null && e!.Description != string.Empty &&
          Try<bool>(() => (e as Tournament)!.MinimumPlayers > 2))
      .OrderBy(e => e.StartTime)
      .Select(e => (e as Tournament)!);

    return events.Select(e => new EventSummary
    {
      Id = e.Id,
      Name = e.ToString(),
      StartTime = e.StartTime,
      EndTime = e.EndTime,
      MinPlayers = e.MinimumPlayers,
      MaxPlayers = e.MaximumPlayers,
      Players = e.TotalPlayers,
      Rounds = e.TotalRounds,
      IsCompleted = e.IsCompleted
    })
    .ToArray();
  }

  [HttpGet("{id}")] // GET /api/events/openevent/{id}
  public ActionResult OpenEvent(int id)
  {
    EventManager.NavigateToEvent(id);
    return Ok();
  }
}
