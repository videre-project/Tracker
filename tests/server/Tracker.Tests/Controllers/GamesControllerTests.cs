/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

using Tracker.Services.MTGO;
using Tracker.Tests.Infrastructure;


namespace Tracker.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class GamesControllerTests : ControllerTestBase
{
  [Test]
  public async Task Test_Unavailable()
  {
    foreach (string path in new[]
    {
      "/api/games/history",
      "/api/games/match/1002",
      "/api/games/dashboard-stats",
      "/api/games/performance-trend",
    })
    {
      using var response = await Host.Client.GetAsync(path);
      await AssertStatusAsync(response, HttpStatusCode.BadRequest, path);
    }

    using var formats = await Host.Client.GetAsync("/api/games/formats");
    await AssertStatusAsync(formats, HttpStatusCode.OK, "games/formats");
    using var body = JsonDocument.Parse(await formats.Content.ReadAsStringAsync());
    Assert.That(body.RootElement.GetArrayLength(), Is.EqualTo(0));
  }

  [Test]
  public async Task Test_HistoryAndDashboard()
  {
    await ControllerFixtureData.SeedGameAsync(Host);
    Host.ClientProvider.SetReady(new UserIdentity(7, "player1"));

    using var history = await Host.Client.GetAsync("/api/games/history");
    await AssertStatusAsync(history, HttpStatusCode.OK, "games/history");
    using var historyBody = JsonDocument.Parse(
      await history.Content.ReadAsStringAsync());
    JsonElement historyRoot = historyBody.RootElement;
    Assert.That(historyRoot.GetProperty("totalCount").GetInt32(), Is.EqualTo(1));
    JsonElement historyEvent = historyRoot.GetProperty("items")[0];
    Assert.That(historyEvent.GetProperty("isEvent").GetBoolean(), Is.True);
    Assert.That(historyEvent.GetProperty("eventName").GetString(),
      Is.EqualTo("Fixture match"));
    Assert.That(historyEvent.GetProperty("format").GetString(),
      Is.EqualTo("Pauper"));
    JsonElement historyMatch = historyEvent.GetProperty("matches")[0];
    Assert.That(historyMatch.GetProperty("id").GetInt32(), Is.EqualTo(1002));
    Assert.That(historyMatch.GetProperty("result").GetString(), Is.EqualTo("Win"));
    Assert.That(historyMatch.GetProperty("record").GetString(), Is.EqualTo("1-0"));
    Assert.That(historyMatch.GetProperty("opponentName").GetString(),
      Is.EqualTo("player2"));

    using var details = await Host.Client.GetAsync("/api/games/match/1002");
    await AssertStatusAsync(details, HttpStatusCode.OK, "games/match");
    using var detailsBody = JsonDocument.Parse(
      await details.Content.ReadAsStringAsync());
    JsonElement detailsRoot = detailsBody.RootElement;
    Assert.That(detailsRoot.GetProperty("eventName").GetString(),
      Is.EqualTo("Fixture match"));
    Assert.That(detailsRoot.GetProperty("result").GetString(), Is.EqualTo("Win"));
    Assert.That(detailsRoot.GetProperty("record").GetString(), Is.EqualTo("1-0"));
    Assert.That(detailsRoot.GetProperty("opponentName").GetString(),
      Is.EqualTo("player2"));
    JsonElement game = detailsRoot.GetProperty("games")[0];
    Assert.That(game.GetProperty("id").GetInt32(), Is.EqualTo(1003));
    Assert.That(game.GetProperty("result").GetString(), Is.EqualTo("Win"));
    Assert.That(game.GetProperty("sideboardChanges")[0].GetProperty("name")
      .GetString(), Is.EqualTo("Force Spike"));

    using var hidden = await Host.Client.GetAsync("/api/games/match/2002");
    await AssertStatusAsync(hidden, HttpStatusCode.Forbidden, "games/match/other");

    using var dashboard = await Host.Client.GetAsync(
      "/api/games/dashboard-stats");
    await AssertStatusAsync(dashboard, HttpStatusCode.OK, "games/dashboard");
    using var dashboardBody = JsonDocument.Parse(
      await dashboard.Content.ReadAsStringAsync());
    JsonElement dashboardRoot = dashboardBody.RootElement;
    Assert.That(dashboardRoot.GetProperty("totalMatches").GetInt32(),
      Is.EqualTo(1));
    Assert.That(dashboardRoot.GetProperty("wins").GetInt32(), Is.EqualTo(1));
    Assert.That(dashboardRoot.GetProperty("losses").GetInt32(), Is.EqualTo(0));

    using var trend = await Host.Client.GetAsync(
      "/api/games/performance-trend");
    await AssertStatusAsync(trend, HttpStatusCode.OK, "games/trend");
    using var trendBody = JsonDocument.Parse(
      await trend.Content.ReadAsStringAsync());
    Assert.That(trendBody.RootElement.GetArrayLength(), Is.EqualTo(1));
    Assert.That(trendBody.RootElement[0].GetProperty("matches").GetInt32(),
      Is.EqualTo(1));
    Assert.That(trendBody.RootElement[0].GetProperty("winrate").GetDouble(),
      Is.EqualTo(100));
  }
}
