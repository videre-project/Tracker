/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using Tracker.Services.MTGO;
using Tracker.Tests.Fakes;


namespace Tracker.Tests;

[TestFixture]
public sealed class ClientStateMonitorTests
{
  [Test]
  public void Test_Disconnect()
  {
    var provider = new FakeClientAPIProvider();
    provider.SetReady(new UserIdentity(7, "fixture-user"));

    using var monitor = new ClientStateMonitor(provider);
    Assert.That(monitor.IsClientReady, Is.True);
    Assert.That(monitor.Token.IsCancellationRequested, Is.False);

    provider.SetDisconnected();

    Assert.That(monitor.IsClientReady, Is.False);
    Assert.That(monitor.Token.IsCancellationRequested, Is.True);
  }

  [Test]
  public void Test_Reconnect()
  {
    var provider = new FakeClientAPIProvider();
    provider.SetReady(new UserIdentity(7, "fixture-user"), processId: 101);

    using var previous = new ClientStateMonitor(provider);
    provider.SetDisconnected();
    Assert.That(previous.Token.IsCancellationRequested, Is.True);

    provider.SetReady(new UserIdentity(8, "fixture-user-2"), processId: 202);

    // The monitor is request-scoped: reconnect does not revive the old token.
    Assert.That(previous.Token.IsCancellationRequested, Is.True);

    using var current = new ClientStateMonitor(provider);
    Assert.That(current.IsClientReady, Is.True);
    Assert.That(current.Token.IsCancellationRequested, Is.False);
    Assert.That(provider.CurrentUser, Is.EqualTo(
      new UserIdentity(8, "fixture-user-2")));
  }
}
