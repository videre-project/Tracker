/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using MTGOSDK.API;
using MTGOSDK.API.Collection;
using MTGOSDK.API.Play;


namespace MTGOTracker.Services.MTGO.Collection;

internal static class DeckFormatResolver
{
  private static readonly object s_lock = new();
  private static ushort? s_processId;
  private static long s_generation;
  private static long s_cachedGeneration = -1;
  private static Dictionary<string, PlayFormatType>? s_formatTypes;

  static DeckFormatResolver() => ObjectCache.OnReset += (_, _) => Interlocked.Increment(ref s_generation);

  /// <summary>
  /// Resolves historical format names against MTGO's format metadata. Unknown
  /// names stay unclassified rather than being assumed to be constructed.
  /// </summary>
  public static bool TryGetGameType(string? gameType, ushort? processId,
    out PlayFormatType? type, out string[] legacyFormats)
  {
    type = gameType?.ToLowerInvariant() switch
    {
      null or "" or "all" => null,
      "constructed" => PlayFormatType.Constructed,
      "limited" => PlayFormatType.Sealed | PlayFormatType.Draft,
      _ => PlayFormatType.Null,
    };
    legacyFormats = [];
    if (type == PlayFormatType.Null) return false;
    if (type is null) return true;
    var requestedType = type.Value;
    lock (s_lock)
    {
      long generation = Volatile.Read(ref s_generation);
      if (s_formatTypes is null || s_processId != processId || s_cachedGeneration != generation)
      {
        var types = new Dictionary<string, PlayFormatType>(StringComparer.OrdinalIgnoreCase);
        foreach (var format in CollectionManager.PlayFormats)
        {
          string name = format.Name.Trim();
          if (name.Length > 0) types[name] = types.GetValueOrDefault(name) | format.Type;
        }
        if (generation != Volatile.Read(ref s_generation))
          throw new InvalidOperationException("MTGO disconnected while loading format metadata.");
        s_processId = processId;
        s_cachedGeneration = generation;
        s_formatTypes = types;
      }
      legacyFormats = s_formatTypes.Where(entry => (entry.Value & requestedType) != 0)
        .Select(entry => entry.Key).ToArray();
    }
    return true;
  }

  public static PlayFormat Resolve(string? codeOrName)
  {
    string value = codeOrName?.Trim() ?? "";
    return CollectionManager.PlayFormats.FirstOrDefault(format =>
      string.Equals(format.Code, value, StringComparison.OrdinalIgnoreCase) ||
      string.Equals(format.Name, value, StringComparison.OrdinalIgnoreCase))
      ?? throw new ArgumentException($"Unknown MTGO deck format '{value}'.");
  }
}
