/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace Tracker.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class EventStreamsControllerTests : ControllerTestBase
{
  [Test]
  public async Task Test_Unavailable()
  {
    foreach (string path in new[]
    {
      "/api/events/watchtournamentupdates/1001",
      "/api/events/watchplayercount",
      "/api/events/watchstandings/1001",
    })
    {
      using var request = new HttpRequestMessage(HttpMethod.Get, path);
      request.Headers.Accept.Add(
        new MediaTypeWithQualityHeaderValue("application/x-ndjson"));
      using var response = await Host.Client.SendAsync(
        request, HttpCompletionOption.ResponseHeadersRead);

      await AssertStatusAsync(response, HttpStatusCode.ServiceUnavailable, path);
    }
  }
}
