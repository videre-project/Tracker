/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.Linq;

using Microsoft.AspNetCore.Mvc;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.Core.Remoting;


namespace Tracker.Controllers;

[Route("api/[controller]/[action]")]
public class EventsController : ControllerBase
{
  [HttpGet] // GET /api/events/geteventslist
  public IEnumerable<Tournament> GetEventsList()
  {
    return EventManager.FeaturedEvents
      .Where(e => e.MinimumPlayers > 2)
      .OrderBy(e => e.StartTime);
  }

  [HttpGet("{id}")] // GET /api/events/getstandings/{id}
  public IEnumerable<StandingRecord> GetStandings(int id)
  {
    Tournament tournament = EventManager.GetEvent(id);
    return tournament.Standings;
  }

  [HttpGet("{id}")] // GET /api/events/openevent/{id}
  public ActionResult OpenEvent(int id)
  {
    EventManager.NavigateToEvent(id);
    RemoteClient.FocusWindow();
    return Ok();
  }
}
