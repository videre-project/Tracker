/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

using Microsoft.EntityFrameworkCore;


namespace Tracker.Database.Models;

public class MatchModel
{
  public int Id { get; set; }

  public int EventId { get; set; }
  public EventModel Event { get; set; }

  public List<GameModel> Games { get; set; } = new();
}
