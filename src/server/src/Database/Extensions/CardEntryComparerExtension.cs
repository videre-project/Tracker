/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore.ChangeTracking;


using Tracker.Database.Models;


namespace Tracker.Database.Extensions;

public static class CardEntryComparerExtensions
{
  public static ValueComparer<List<CardEntry>> CardEntryListComparer => new(
    // Compare two lists for equality
    (c1, c2) =>
      c1 != null && c2 != null && c1.Count == c2.Count &&
      c1.Zip(c2, (card1, card2) =>
          card1.catalogId == card2.catalogId &&
          card1.name == card2.name &&
          card1.quantity == card2.quantity)
      .All(equal => equal),

    // Generate a hashcode for a list
    c =>
      c != null
      ? c.Aggregate(0, (a, v) =>
          HashCode.Combine(a, v.catalogId.GetHashCode(), v.name.GetHashCode(), v.quantity.GetHashCode()))
      : 0,

    // Create a snapshot clone of the list
    c =>
      c != null
        ? c.Select(e => new CardEntry(e.catalogId, e.name, e.quantity)).ToList()
        : null!
  );
}
