/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Tracker.Database;
using Tracker.Services.MTGO;
using Tracker.Tests.Infrastructure;


namespace Tracker.Tests;

[TestFixture]
[NonParallelizable]
public sealed class FixturePersistenceTests
{
  private TrackerTestHost _host = null!;
  private EventFixtureMetadata _fixture = null!;

  [SetUp]
  public async Task SetUp()
  {
    _fixture = FixtureFiles.ReadEventMetadata();
    _host = await TrackerTestHost.StartAsync(FixtureFiles.EventDatabase);
    _host.ClientProvider.SetReady(new UserIdentity(1, "PlayerA"));
  }

  [TearDown]
  public async Task TearDown() => await _host.DisposeAsync();

  [Test]
  public async Task Test_Replay()
  {
    using (var scope = _host.CreateScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<EventContext>();
      var persisted = await context.Events
        .Include(item => item.Matches)
          .ThenInclude(item => item.Games)
            .ThenInclude(item => item.States)
              .ThenInclude(item => item.Actions)
        .Include(item => item.Matches)
          .ThenInclude(item => item.Games)
            .ThenInclude(item => item.States)
              .ThenInclude(item => item.Logs)
        .Include(item => item.Matches)
          .ThenInclude(item => item.Games)
            .ThenInclude(item => item.States)
              .ThenInclude(item => item.ZoneTransfers)
        .SingleAsync(item => item.Id == _fixture.EventId);

      Assert.That(persisted.Matches, Has.Count.EqualTo(1));
      Assert.That(persisted.Matches[0].Id, Is.EqualTo(_fixture.MatchId));
      Assert.That(persisted.Matches[0].Games, Has.Count.EqualTo(3));
      var replayGame = persisted.Matches[0].Games
        .Single(game => game.Id == _fixture.GameIds[0]);
      Assert.That(replayGame.States.Count, Is.GreaterThan(100));
      Assert.That(replayGame.States.Any(state => state.Actions.Count > 0),
        Is.True);
      Assert.That(replayGame.States.Any(state => state.Logs.Count > 0),
        Is.True);
      Assert.That(replayGame.States.Any(state => state.ZoneTransfers.Count > 0),
        Is.True);
    }

    using var replay = await _host.Client.GetAsync(
      $"/api/games/game/{_fixture.GameIds[0]}/replay");
    Assert.That(replay.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    using JsonDocument replayBody = JsonDocument.Parse(
      await replay.Content.ReadAsStringAsync());
    JsonElement replayRoot = replayBody.RootElement;
    Assert.That(replayRoot.GetProperty("gameId").GetInt32(),
      Is.EqualTo(_fixture.GameIds[0]));
    Assert.That(replayRoot.GetProperty("snapshots").GetArrayLength(),
      Is.GreaterThan(100));
    Assert.That(replayRoot.GetProperty("players").GetArrayLength(),
      Is.EqualTo(2));
    Assert.That(replayRoot.GetProperty("cards").GetArrayLength(),
      Is.GreaterThan(10));
    Assert.That(replayRoot.GetProperty("snapshots").EnumerateArray()
      .Any(snapshot => snapshot.GetProperty("actions").GetArrayLength() > 0),
      Is.True);
    Assert.That(replayRoot.GetProperty("snapshots").EnumerateArray()
      .Any(snapshot => snapshot.GetProperty("logs").GetArrayLength() > 0),
      Is.True);
  }

  [Test]
  public async Task Test_OpeningHand()
  {
    using var details = await _host.Client.GetAsync(
      $"/api/games/match/{_fixture.MatchId}");
    Assert.That(details.StatusCode, Is.EqualTo(HttpStatusCode.OK),
      await details.Content.ReadAsStringAsync());
    using JsonDocument body = JsonDocument.Parse(
      await details.Content.ReadAsStringAsync());

    foreach (JsonElement game in body.RootElement.GetProperty("games")
      .EnumerateArray())
    {
      int gameId = game.GetProperty("id").GetInt32();
      Dictionary<string, int> expected = _fixture.OpeningHands[gameId.ToString()];
      Dictionary<string, int> actual = CountOpeningHand(
        game.GetProperty("logs"));

      Assert.That(actual.Values.Sum(), Is.EqualTo(7), $"game {gameId}");
      Assert.That(actual, Is.EquivalentTo(expected), $"game {gameId}");
    }
  }

  private static Dictionary<string, int> CountOpeningHand(JsonElement logs)
  {
    var entries = logs.EnumerateArray().ToList();
    int keepIndex = entries.FindIndex(log =>
      log.GetProperty("gameLogType").GetString() == "GameAction" &&
      IsKeep(log.GetProperty("data").GetString()));
    Assert.That(keepIndex, Is.GreaterThanOrEqualTo(0));

    var counts = CountArrivals(entries.Take(keepIndex));
    if (counts.Count == 0)
    {
      DateTime keepTime = entries[keepIndex].GetProperty("timestamp")
        .GetDateTime();
      counts = CountArrivals(entries.TakeWhile(log =>
        log.GetProperty("timestamp").GetDateTime() <= keepTime));
    }

    return counts;
  }

  private static bool IsKeep(string? data)
  {
    if (string.IsNullOrWhiteSpace(data)) return false;
    try
    {
      using JsonDocument action = JsonDocument.Parse(data);
      JsonElement root = action.RootElement;
      string? name = root.TryGetProperty("name", out JsonElement nameElement)
        ? nameElement.GetString()
        : null;
      string? response = root.TryGetProperty("response", out JsonElement responseElement)
        ? responseElement.GetString()
        : null;
      return string.Equals(name, "Keep", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(response, "Keep", StringComparison.OrdinalIgnoreCase);
    }
    catch (JsonException)
    {
      return false;
    }
  }

  private static Dictionary<string, int> CountArrivals(
    IEnumerable<JsonElement> logs)
  {
    var counts = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (JsonElement log in logs)
    {
      string? type = log.GetProperty("gameLogType").GetString();
      if (type is not ("ZoneChange" or "Reveal")) continue;

      using JsonDocument payload = JsonDocument.Parse(
        log.GetProperty("data").GetString() ?? "[]");
      foreach (JsonElement transfer in payload.RootElement.EnumerateArray())
      {
        if (transfer.GetProperty("toZone").GetString() != "Hand") continue;
        string? name = transfer.GetProperty("cardName").GetString();
        if (string.IsNullOrWhiteSpace(name)) continue;
        counts[name] = counts.GetValueOrDefault(name) + 1;
      }
    }

    return counts;
  }
}
