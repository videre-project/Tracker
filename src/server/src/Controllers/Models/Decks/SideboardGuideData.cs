/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

using MTGOTracker.Database.Models;


namespace MTGOTracker.Controllers.Models.Decks;

public sealed record SideboardGuideData(
  int Id, DeckReferenceData Deck, string OpponentArchetype, string Notes,
  DateTime UpdatedAt, IReadOnlyList<CardEntry> BringIn, IReadOnlyList<CardEntry> TakeOut);
