/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;


namespace Tracker.Controllers.Models.Decks;

public sealed record DeckHistoryChangeView(
  int CatalogId,
  string Name,
  int QuantityDelta,
  string Zone,
  int Cmc,
  List<string> Colors,
  List<string> Types,
  string Rarity);
