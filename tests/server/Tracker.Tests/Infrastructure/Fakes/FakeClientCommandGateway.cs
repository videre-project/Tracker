/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading;
using System.Threading.Tasks;

using Tracker.Services.MTGO;


namespace Tracker.Tests.Fakes;

internal sealed class FakeClientCommandGateway : IClientCommandGateway
{
  public int? OpenedEventId { get; private set; }

  public Task OpenEventAsync(
    int eventId,
    CancellationToken cancellationToken = default)
  {
    OpenedEventId = eventId;
    return Task.CompletedTask;
  }
}
