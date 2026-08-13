/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Tracker.Database;
using Tracker.Database.Models.Events;
using Tracker.Services.MTGO.Events;
using Tracker.Tests.Infrastructure;


namespace Tracker.Tests;

[TestFixture]
[NonParallelizable]
public sealed class EventWriterTests
{
  private TrackerTestHost _host = null!;

  [SetUp]
  public async Task SetUp() => _host = await TrackerTestHost.StartAsync();

  [TearDown]
  public async Task TearDown() => await _host.DisposeAsync();

  [Test]
  public async Task Test_EndTimeOnce()
  {
    using (var scope = _host.CreateScope())
    {
      var context = scope.ServiceProvider.GetRequiredService<EventContext>();
      context.Events.Add(new EventModel
      {
        Id = 1001,
        Format = "Fixture",
        Type = EventType.Match,
        Description = "Fixture match",
        StartTime = DateTime.UnixEpoch,
      });
      await context.SaveChangesAsync();
    }

    var writer = new EventDatabaseWriter(_host.Services);
    DateTime firstEnd = DateTime.UnixEpoch.AddMinutes(10);
    DateTime laterEnd = firstEnd.AddMinutes(10);

    Assert.That(writer.TryUpdateEventEndTime(1001, firstEnd), Is.True);
    Assert.That(writer.TryUpdateEventEndTime(1001, laterEnd), Is.False);

    using var verifyScope = _host.CreateScope();
    var verifyContext = verifyScope.ServiceProvider.GetRequiredService<EventContext>();
    var persisted = await verifyContext.Events.SingleAsync(item => item.Id == 1001);
    Assert.That(persisted.EndTime, Is.EqualTo(firstEnd));
  }
}
