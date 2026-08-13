/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

using Tracker.Services.MTGO;
using Tracker.Tests.Infrastructure;


namespace Tracker.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class GameReplayControllerTests : ControllerTestBase
{
  [Test]
  public async Task Test_NotFound()
  {
    using var response = await Host.Client.GetAsync(
      "/api/games/game/999999/replay");

    await AssertStatusAsync(response, HttpStatusCode.NotFound, "replay");
  }

  [Test]
  public async Task Test_Replay()
  {
    await ControllerFixtureData.SeedGameAsync(Host);
    Host.ClientProvider.SetReady(new UserIdentity(7, "player1"));

    using var response = await Host.Client.GetAsync(
      "/api/games/game/1003/replay");
    await AssertStatusAsync(response, HttpStatusCode.OK, "replay");
    using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    JsonElement root = body.RootElement;

    Assert.That(root.GetProperty("gameId").GetInt32(), Is.EqualTo(1003));
    Assert.That(root.GetProperty("perspectivePlayerIndex").GetInt32(),
      Is.EqualTo(0));
    Assert.That(root.GetProperty("players").GetArrayLength(), Is.EqualTo(2));
    Assert.That(root.GetProperty("players")[0].GetProperty("name").GetString(),
      Is.EqualTo("player1"));
    Assert.That(root.GetProperty("cards")[0].GetProperty("name").GetString(),
      Is.EqualTo("Island"));
    Assert.That(root.GetProperty("snapshots").GetArrayLength(), Is.EqualTo(1));
    JsonElement snapshot = root.GetProperty("snapshots")[0];
    Assert.That(snapshot.GetProperty("turnNumber").GetInt32(), Is.EqualTo(1));
    Assert.That(snapshot.GetProperty("promptText").GetString(),
      Is.EqualTo("Play a land."));
    Assert.That(snapshot.GetProperty("actions")[0].GetProperty("cardName")
      .GetString(), Is.EqualTo("Island"));
    Assert.That(snapshot.GetProperty("logs")[0].GetProperty("data").GetString(),
      Does.Contain("Island"));
  }
}
