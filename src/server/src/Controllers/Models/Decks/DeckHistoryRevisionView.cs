/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;

using Tracker.Database.Models;


namespace Tracker.Controllers.Models.Decks;

public sealed record DeckHistoryRevisionView(
  long RevisionId,
  long CardGroupingId,
  DateTime ObservedAt,
  DateTime Timestamp,
  string Name,
  string Format,
  int MainboardCount,
  int SideboardCount,
  List<string> Colors,
  string? Archetype,
  string? FeaturedCard,
  List<CardEntry> Mainboard,
  List<CardEntry> Sideboard,
  List<DeckHistoryChangeView> ChangesFromPrevious,
  List<DeckHistoryChangeView> ChangesFromLatest);
