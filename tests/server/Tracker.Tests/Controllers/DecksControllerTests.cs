/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Net;
using System.Threading.Tasks;

namespace Tracker.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class DecksControllerTests : ControllerTestBase
{
  [Test]
  public async Task Test_NotFound()
  {
    using var detail = await Host.Client.GetAsync("/api/decks/999999");
    using var history = await Host.Client.GetAsync(
      "/api/decks/999999/history");

    await AssertStatusAsync(detail, HttpStatusCode.NotFound, "decks/detail");
    await AssertStatusAsync(history, HttpStatusCode.NotFound, "decks/history");
  }

  [Test]
  public async Task Test_SheetValidation()
  {
    using var response = await Host.Client.GetAsync(
      "/api/decks/sheet?id=not-a-revision");

    await AssertStatusAsync(response, HttpStatusCode.BadRequest, "decks/sheet");
  }
}
