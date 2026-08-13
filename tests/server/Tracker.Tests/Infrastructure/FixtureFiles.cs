/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;
using System.IO;
using System.Text.Json;


namespace Tracker.Tests.Infrastructure;

internal static class FixtureFiles
{
  public static string EventDatabase => Path.Combine(FixtureDirectory, "event.db");
  public static string EventMetadata => Path.Combine(FixtureDirectory, "event.json");

  public static string FixtureDirectory => Path.Combine(
    TestContext.CurrentContext.TestDirectory,
    "fixtures");

  public static EventFixtureMetadata ReadEventMetadata()
  {
    using FileStream stream = File.OpenRead(EventMetadata);
    return JsonSerializer.Deserialize<EventFixtureMetadata>(
      stream,
      new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
  }
}

internal sealed class EventFixtureMetadata
{
  public string Scenario { get; set; } = "";
  public string Source { get; set; } = "";
  public bool Sanitized { get; set; }
  public int EventId { get; set; }
  public int MatchId { get; set; }
  public List<int> GameIds { get; set; } = [];
  public Dictionary<string, Dictionary<string, int>> OpeningHands { get; set; } = [];
}
