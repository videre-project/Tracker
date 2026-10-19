/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

namespace MTGOTracker;

/// <summary>
/// Persisted user preferences exposed by the settings API.
/// </summary>
public sealed record ApplicationSettings
{
  public bool CheckForUpdates { get; init; } = true;
  public string Theme { get; init; } = "dark";
  public bool UseCustomTitleBar { get; init; } = true;
  public bool StartMaximized { get; init; }
  public string StartupPage { get; init; } = "/";
  public bool ShowActiveEvents { get; init; } = true;
  public bool ShowUpcomingEvents { get; init; } = true;
  public int HistoryRangeDays { get; init; } = 30;
  public int HistoryPageSize { get; init; } = 50;
  public string DeckSortMode { get; init; } = "cmc";
  public bool ShowSideboard { get; init; }
  public bool ShowDeckTools { get; init; } = true;
  public bool ReplayFollowLive { get; init; } = true;
  public bool ReplayKeyboardShortcuts { get; init; } = true;
  public bool ReplayExpandOpponentZones { get; init; } = true;
  public bool RecordReplays { get; init; } = true;
  public bool RecordTrades { get; init; } = true;
  public bool SynchronizeDecks { get; init; } = true;
}
