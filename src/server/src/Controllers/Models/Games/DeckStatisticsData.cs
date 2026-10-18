/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;

using MTGOTracker.Database.Models;


namespace MTGOTracker.Controllers.Models.Games;

/// <summary>
/// Results from recorded games whose submitted starting list contained the card.
/// Copy counts describe the requested revision, even when observations span older lists.
/// </summary>
public sealed record DeckCardStatisticsData(
  CardEntry Card, int MainboardCopies, int SideboardCopies,
  WinLossRecord InHand, WinLossRecord OpeningHand,
  WinLossRecord LibraryToHand, WinLossRecord Cast,
  WinLossRecord InHandOnPlay, WinLossRecord InHandOnDraw);

public sealed record DeckStatisticsData(
  WinLossRecord Games, int RecordedGames, IReadOnlyList<DeckCardStatisticsData> Cards);
