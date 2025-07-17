/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore.ChangeTracking;

using MTGOSDK.API.Play.Games;


namespace Tracker.Database.Extensions;

public static class GamePlayerResultComparerExtensions
{
  public static ValueComparer<List<GamePlayerResult>> GamePlayerResultListComparer => new(
    (c1, c2) =>
      c1 != null && c2 != null && c1.Count == c2.Count &&
      c1.Zip(c2, (gpr1, gpr2) =>
          gpr1.Player == gpr2.Player &&
          gpr1.Result == gpr2.Result)
      .All(equal => equal),
    c =>
      c != null
      ? c.Aggregate(0, (a, v) => HashCode.Combine(a, v.Player.GetHashCode(), v.Result.GetHashCode()))
      : 0,
    c =>
      c != null
        ? c.Select(e => new GamePlayerResult(e.Player, e.PlayDraw, e.Result, e.Clock)).ToList()
        : null!
  );
}