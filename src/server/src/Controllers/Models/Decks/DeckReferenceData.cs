/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

using MTGOTracker.Database.Models;


namespace MTGOTracker.Controllers.Models.Decks;

/// <summary>
/// A deck identity. Archetype groups omit the saved deck and revision IDs.
/// </summary>
public sealed record DeckReferenceData(
  long? Id, long? RevisionId, string Name, string Format, string? Archetype,
  DateTime Timestamp, IReadOnlyList<string> Colors, IReadOnlyList<CardEntry> FeaturedCards);
