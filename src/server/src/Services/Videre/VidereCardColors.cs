/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;


namespace Tracker.Services.Videre;

public static class VidereCardColors
{
  private static readonly IReadOnlyList<string> s_colors =
    Enum.GetValues<Generated.Colors>()
      .Select(color => VidereAPIClient.GetEnumWireValue(color))
      .ToArray();

  private static readonly IReadOnlyDictionary<string, int> s_displayRanks =
    s_colors
      .Select((color, index) => (color, index))
      .ToDictionary(entry => entry.color, entry => entry.index, StringComparer.Ordinal);

  public static bool IsCanonical(char color) =>
    s_displayRanks.ContainsKey(color.ToString());

  public static IEnumerable<string> Normalize(IEnumerable<char> colors) => colors
    .Select(color => color.ToString())
    .Where(s_displayRanks.ContainsKey)
    .Distinct(StringComparer.Ordinal)
    .OrderBy(GetDisplayRank);

  public static IEnumerable<string> Normalize(IEnumerable<string> colors) => colors
    .Where(s_displayRanks.ContainsKey)
    .Distinct(StringComparer.Ordinal)
    .OrderBy(GetDisplayRank);

  private static int GetDisplayRank(string color) => s_displayRanks[color];
}
