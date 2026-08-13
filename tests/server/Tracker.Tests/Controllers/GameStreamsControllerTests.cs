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
internal sealed class GameStreamsControllerTests : ControllerTestBase
{
  [Test]
  public async Task Test_Unavailable()
  {
    foreach (string path in new[]
    {
      "/api/games/match/1002/watch",
      "/api/games/history/watch",
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
