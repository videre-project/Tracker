/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

using Tracker.Tests.Infrastructure;


namespace Tracker.Tests;

internal abstract class ControllerTestBase
{
  protected TrackerTestHost Host { get; private set; } = null!;

  [SetUp]
  public async Task StartHost() => Host = await TrackerTestHost.StartAsync();

  [TearDown]
  public async Task StopHost() => await Host.DisposeAsync();

  protected static async Task AssertStatusAsync(
    HttpResponseMessage response,
    HttpStatusCode expected,
    string path)
  {
    string body = await response.Content.ReadAsStringAsync();
    Assert.That(response.StatusCode, Is.EqualTo(expected),
      $"{path}: {body}");
  }
}
