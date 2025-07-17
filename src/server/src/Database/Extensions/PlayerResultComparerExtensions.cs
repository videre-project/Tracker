/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore.ChangeTracking;

using MTGOSDK.API.Play;


namespace Tracker.Database.Extensions;

public static class PlayerResultComparerExtensions
{
  public static ValueComparer<List<PlayerResult>> PlayerResultListComparer => new(
    (c1, c2) =>
      c1 != null && c2 != null && c1.Count == c2.Count &&
      c1.Zip(c2, (pr1, pr2) =>
          pr1.Player == pr2.Player &&
          pr1.Result == pr2.Result)
      .All(equal => equal),
    c =>
      c != null
      ? c.Aggregate(0, (a, v) => HashCode.Combine(a, v.Player.GetHashCode(), v.Result.GetHashCode()))
      : 0,
    c =>
      c != null
        ? c.Select(e => new PlayerResult(e.Player, e.Result, e.Wins, e.Losses, e.Draws)).ToList()
        : null!
  );
}
