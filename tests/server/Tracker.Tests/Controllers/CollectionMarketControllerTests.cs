/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Net;
using System.Threading.Tasks;

namespace Tracker.Tests;

[TestFixture]
[NonParallelizable]
internal sealed class CollectionMarketControllerTests : ControllerTestBase
{
  [Test]
  public async Task Test_InvalidCatalogIds()
  {
    using var priceHistory = await Host.Client.GetAsync(
      "/api/collection/prices/0/history");
    using var cardDetails = await Host.Client.GetAsync(
      "/api/collection/cards/0/details");

    await AssertStatusAsync(
      priceHistory, HttpStatusCode.BadRequest, "collection/prices");
    await AssertStatusAsync(
      cardDetails, HttpStatusCode.BadRequest, "collection/cards/details");
  }
}
