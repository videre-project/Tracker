/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Linq;

using MTGOSDK.API.Collection;
using MTGOSDK.API.Play;


namespace Tracker.Services.MTGO.Collection;

internal static class DeckFormatResolver
{
  public static PlayFormat Resolve(string? codeOrName)
  {
    string value = codeOrName?.Trim() ?? "";
    return CollectionManager.PlayFormats.FirstOrDefault(format =>
      string.Equals(format.Code, value, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(format.Name, value, StringComparison.OrdinalIgnoreCase))
      ?? throw new ArgumentException($"Unknown MTGO deck format '{value}'.");
  }
}
