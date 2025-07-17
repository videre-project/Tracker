/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore.ChangeTracking;

using Tracker.Database.Models;


namespace Tracker.Database.Extensions;

public static class SideboardChangesComparerExtensions
{
  public static ValueComparer<Dictionary<int, List<CardEntry>>> SideboardChangesComparer => new(
    (d1, d2) =>
      d1 != null && d2 != null && d1.Count == d2.Count &&
      d1.Keys.All(k => d2.ContainsKey(k) &&
        CardEntryComparerExtensions.CardEntryListComparer.Equals(d1[k], d2[k])),
    d =>
      d != null
      ? d.Aggregate(0, (a, v) => HashCode.Combine(a, v.Key.GetHashCode(), CardEntryComparerExtensions.CardEntryListComparer.GetHashCode(v.Value)))
      : 0,
    d =>
      d != null
        ? d.ToDictionary(e => e.Key, e => CardEntryComparerExtensions.CardEntryListComparer.Snapshot(e.Value))
        : null!
  );
}