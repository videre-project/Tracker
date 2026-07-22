/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;

namespace Tracker.Controllers.Models.Decks;

public sealed record DeckHistoryView(
  long CurrentRevisionId,
  long CardGroupingId,
  string Name,
  string Format,
  List<DeckHistoryRevisionView> Revisions);
