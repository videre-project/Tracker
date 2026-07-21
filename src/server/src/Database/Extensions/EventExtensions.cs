/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Leagues;


namespace Tracker.Database.Extensions;

public static class EventExtensions
{
  public static int GetDatabaseId(this Event eventObj)
  {
    return eventObj is League league ? league.CourseId : eventObj.Id;
  }
}
