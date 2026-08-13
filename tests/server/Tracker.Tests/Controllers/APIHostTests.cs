/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using Tracker.Database;
using Tracker.Database.Models.Trades;
using Tracker.Services.MTGO;
using Tracker.Services.MTGO.Trade;
using Tracker.Tests.Infrastructure;


namespace Tracker.Tests;

[TestFixture]
[NonParallelizable]
public sealed class APIHostTests
{
  private TrackerTestHost _host = null!;

  [SetUp]
  public async Task SetUp()
  {
    _host = await TrackerTestHost.StartAsync();
  }

  [TearDown]
  public async Task TearDown()
  {
    await _host.DisposeAsync();
  }

  [Test]
  public async Task Test_GetState()
  {
    using var response = await _host.Client.GetAsync("/api/client/getstate");

    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    var body = await response.Content.ReadFromJsonAsync<ClientStateResponse>();
    Assert.That(body, Is.Not.Null);
    Assert.That(body!.Status, Is.EqualTo("disconnected"));
    Assert.That(body.IsConnected, Is.False);
  }

  [Test]
  public async Task Test_OpenEvent()
  {
    using var response = await _host.Client.PostAsync(
      "/api/events/openevent/42",
      content: null);

    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
    Assert.That(_host.CommandGateway.OpenedEventId, Is.EqualTo(42));
  }

  [Test]
  public async Task Test_GetEvents()
  {
    using var response = await _host.Client.GetAsync(
      "/api/events/geteventslist");

    Assert.That(response.StatusCode,
      Is.EqualTo(HttpStatusCode.ServiceUnavailable));
  }

  [Test]
  public async Task Test_ReadOnly()
  {
    using var diagnostics = await _host.Client.GetAsync(
      "/api/diagnostics/getmetrics");
    using var trades = await _host.Client.GetAsync("/api/trades");
    using var collection = await _host.Client.GetAsync("/api/collection/cards");
    using var tradeHistory = await _host.Client.GetAsync("/api/trades/history");

    Assert.That(diagnostics.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    Assert.That(trades.StatusCode,
      Is.EqualTo(HttpStatusCode.ServiceUnavailable));
    Assert.That(collection.StatusCode,
      Is.EqualTo(HttpStatusCode.ServiceUnavailable));
    Assert.That(tradeHistory.StatusCode, Is.EqualTo(HttpStatusCode.OK));

    using var search = await _host.Client.PostAsJsonAsync(
      "/api/collection/cards/search", new { query = "" });
    Assert.That(search.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    JsonDocument? searchBody = await search.Content.ReadFromJsonAsync<JsonDocument>();
    Assert.That(searchBody, Is.Not.Null);
    Assert.That(searchBody!.RootElement.GetProperty("catalogIds").GetArrayLength(),
      Is.EqualTo(0));
  }

  [Test]
  public async Task Test_WatchState()
  {
    using var response = await _host.Client.GetAsync(
      "/api/client/watchstate",
      HttpCompletionOption.ResponseHeadersRead);

    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    Assert.That(response.Content.Headers.ContentType?.MediaType,
      Is.EqualTo("application/x-ndjson"));

    await using var stream = await response.Content.ReadAsStreamAsync();
    using var reader = new StreamReader(stream);
    string? line = await reader.ReadLineAsync();

    Assert.That(line, Is.Not.Null.And.Not.Empty);
    using var json = JsonDocument.Parse(line!);
    Assert.That(json.RootElement.GetProperty("status").GetString(),
      Is.EqualTo("disconnected"));
  }

  [Test]
  public async Task Test_TradeHistory()
  {
    using (var scope = _host.CreateScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<TradeContext>();
      var writer = new TradeHistoryWriter();
      await writer.UpsertAccountAsync(
        context, new UserIdentity(7, "fixture-user"), default);
      await writer.ApplyAsync(context, new TradeEscrowWrite(
        7,
        Guid.Parse("00000000-0000-0000-0000-000000000021"),
        21,
        TradeEscrowKind.Player,
        8,
        "Fixture Partner",
        DateTime.UnixEpoch,
        DateTime.UnixEpoch.AddMinutes(1),
        2,
        TradeEscrowResult.Completed,
        TradeAttributionStatus.Inferred,
        [
          new TradeItemWrite(TradeEscrowItemRole.LocalOffer, 102392, 2),
          new TradeItemWrite(TradeEscrowItemRole.RemoteOffer, 9231, 1),
        ],
        [new TradeMessageWrite(DateTime.UnixEpoch, 8, "Fixture Partner", "done")],
        []), default);
      await writer.ApplyAsync(context, new TradeEscrowWrite(
        7,
        Guid.Parse("00000000-0000-0000-0000-000000000022"),
        22,
        TradeEscrowKind.Player,
        9,
        "Second Partner",
        DateTime.UnixEpoch.AddMinutes(2),
        null,
        1,
        TradeEscrowResult.InProgress,
        TradeAttributionStatus.Pending,
        [], [], []), default);
    }

    using var page = await _host.Client.GetAsync(
      "/api/trades/history?accountId=7&limit=1");
    Assert.That(page.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    using JsonDocument pageBody = JsonDocument.Parse(
      await page.Content.ReadAsStringAsync());
    JsonElement pageRoot = pageBody.RootElement;
    Assert.That(pageRoot.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
    long nextBeforeId = pageRoot.GetProperty("nextBeforeId").GetInt64();
    Assert.That(nextBeforeId, Is.GreaterThan(0));

    using var nextPage = await _host.Client.GetAsync(
      $"/api/trades/history?accountId=7&limit=1&beforeId={nextBeforeId}");
    Assert.That(nextPage.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    using JsonDocument nextPageBody = JsonDocument.Parse(
      await nextPage.Content.ReadAsStringAsync());
    JsonElement completedSummary = nextPageBody.RootElement
      .GetProperty("items")[0];
    long completedId = completedSummary.GetProperty("id").GetInt64();

    using var detail = await _host.Client.GetAsync(
      $"/api/trades/history/{completedId}");
    Assert.That(detail.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    using JsonDocument detailBody = JsonDocument.Parse(
      await detail.Content.ReadAsStringAsync());
    Assert.That(detailBody.RootElement.GetProperty("items").GetArrayLength(),
      Is.EqualTo(2));
    Assert.That(detailBody.RootElement.GetProperty("effects").GetArrayLength(),
      Is.EqualTo(2));
    Assert.That(detailBody.RootElement.GetProperty("messages").GetArrayLength(),
      Is.EqualTo(1));
  }

  [Test]
  public async Task Test_TradeHistoryNotFound()
  {
    using var response = await _host.Client.GetAsync(
      "/api/trades/history/999999");

    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
  }

  [Test]
  public async Task Test_InvalidTradePostFormat()
  {
    _host.ClientProvider.SetReady(new UserIdentity(7, "fixture-user"));

    using var response = await _host.Client.GetAsync(
      "/api/trades/posts?page=1&pageSize=25&format=invalid");

    Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
  }

  [Test]
  public void Test_Databases()
  {
    Assert.That(File.Exists(Path.Combine(
      _host.UserDataFolder, "Database", "Event.db")), Is.True);
    Assert.That(File.Exists(Path.Combine(
      _host.UserDataFolder, "Database", "Collection.db")), Is.True);
    Assert.That(File.Exists(Path.Combine(
      _host.UserDataFolder, "Database", "Trade.db")), Is.True);
  }

  private sealed record ClientStateResponse(
    bool IsConnected,
    bool IsInitialized,
    ushort? ProcessId,
    string Status,
    long? MemoryUsage,
    long? WorkingSet,
    long? VirtualMemory);
}
