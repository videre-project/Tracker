/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

using MTGOTracker.Controllers.Models.Decks;

namespace MTGOTracker.Controllers.Models.Games;

public sealed record WinLossRecord(int Wins, int Losses, int Draws)
{
  public int Total => Wins + Losses + Draws;
  public double? Winrate => Total == 0 ? null : Math.Round(100.0 * Wins / Total, 1);
}

public sealed record MatchupData(
  DeckReferenceData Deck, string OpponentArchetype,
  WinLossRecord Matches, WinLossRecord Games,
  WinLossRecord GameOne, WinLossRecord Postboard,
  WinLossRecord OnPlay, WinLossRecord OnDraw,
  WinLossRecord GameOneOnPlay, WinLossRecord GameOneOnDraw,
  WinLossRecord PostboardOnPlay, WinLossRecord PostboardOnDraw,
  IReadOnlyList<string>? OpponentColors);

public sealed record MatchupOverviewData(
  IReadOnlyList<DeckReferenceData> Decks, IReadOnlyList<MatchupData> Matchups);
