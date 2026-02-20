/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.API.Play;
using MTGOSDK.API.Play.Tournaments;
using MTGOSDK.Core.Logging;


namespace Tracker.Services.MTGO.Events;

public class EventTracker : IDisposable
{
  private readonly Event m_event;
  private readonly EventDatabaseWriter _dbWriter;

  public EventTracker(Event eventObj, EventDatabaseWriter dbWriter)
  {
    m_event = eventObj;
    _dbWriter = dbWriter;

    // If the event is already completed, try to update the end time immediately.
    if (m_event.IsCompleted)
    {
      UpdateEndTime();
    }
  }

  private bool _disposed = false;

  public void Dispose()
  {
    if (_disposed) return;
    _disposed = true;

    // When disposing, we assume the event tracking is done, so we check if it's completed
    // or just mark it as ended if that's the logic we want.
    // However, usually we want to mark EndTime when the event actually completes.
    // If the tracker is disposed, it might be because the app is shutting down or the event is removed.
    // Let's check IsCompleted one last time.
    if (m_event.IsCompleted)
    {
      UpdateEndTime();
    }

    GC.SuppressFinalize(this);
  }

  private void UpdateEndTime()
  {
    DateTime endTime = DateTime.Now;

    if (m_event is Tournament tournament)
    {
      // Use the tournament's end time if available
      if (tournament.EndTime != default)
      {
        endTime = tournament.EndTime;
      }
    }
    else if (m_event is Match match)
    {
      // Use the match's end time if available
      if (match.EndTime != default)
      {
        endTime = match.EndTime;
      }
    }

    if (_dbWriter.TryUpdateEventEndTime(m_event.Id, endTime))
    {
      Log.Debug("Updated end time for event {Id} to {EndTime}", m_event.Id, endTime);
    }
  }
}
