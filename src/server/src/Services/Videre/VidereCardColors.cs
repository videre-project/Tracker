/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;


namespace MTGOTracker.Services.Videre;

public static class VidereCardColors
{
  // Display order is a Magic convention, independent of generated enum values.
  private const string ColorOrder = "WUBRG";

  public static bool IsCanonical(char color) => ColorOrder.Contains(color);

  public static IEnumerable<string> Normalize(IEnumerable<char> colors) =>
    Normalize(colors.Select(color => color.ToString()));

  public static IEnumerable<string> Normalize(IEnumerable<string> colors) => colors
    .Where(color => color.Length == 1 && IsCanonical(color[0]))
    .Distinct(StringComparer.Ordinal)
    .OrderBy(color => ColorOrder.IndexOf(color[0]));
}
