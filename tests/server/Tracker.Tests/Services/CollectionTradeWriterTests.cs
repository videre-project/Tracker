/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Tracker.Database;
using Tracker.Database.Models.Collection;
using Tracker.Database.Models.Trades;
using Tracker.Services.MTGO;
using Tracker.Services.MTGO.Collection;
using Tracker.Services.MTGO.Trade;
using Tracker.Tests.Infrastructure;


namespace Tracker.Tests;

[TestFixture]
[NonParallelizable]
public sealed class CollectionTradeWriterTests
{
  private TrackerTestHost _host = null!;

  [SetUp]
  public async Task SetUp() => _host = await TrackerTestHost.StartAsync();

  [TearDown]
  public async Task TearDown() => await _host.DisposeAsync();

  [Test]
  public async Task Test_Reconcile()
  {
    using var scope = _host.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
    var writer = new CollectionHistoryWriter();
    var identity = new UserIdentity(7, "fixture-user");
    await writer.UpsertAccountAsync(context, identity, CancellationToken.None);

    var initial = new CardGroupingState(
      CardGroupingKind.Collection,
      0,
      null,
      null,
      null,
      [new CardGroupingItemState(102392, 0, 0, 4)]);
    var updated = initial with
    {
      Items = [
        new CardGroupingItemState(102392, 0, 0, 3),
        new CardGroupingItemState(9231, 0, 0, 1),
      ],
    };

    Assert.That(await writer.ReconcileAsync(
      context, 7, initial, DateTime.UnixEpoch, CancellationToken.None), Is.True);
    Assert.That(await writer.ReconcileAsync(
      context, 7, initial, DateTime.UnixEpoch.AddMinutes(1), CancellationToken.None), Is.False);
    Assert.That(await writer.ReconcileAsync(
      context, 7, updated, DateTime.UnixEpoch.AddMinutes(2), CancellationToken.None), Is.True);

    int deleted = await writer.MarkMissingDeletedAsync(
      context,
      7,
      new HashSet<(CardGroupingKind Kind, int NetDeckId)>(),
      DateTime.UnixEpoch.AddMinutes(3),
      CancellationToken.None);

    Assert.That(deleted, Is.EqualTo(1));
    var grouping = await context.CardGroupings.SingleAsync();
    Assert.That(grouping.IsDeleted, Is.True);
    Assert.That(await context.CardGroupingRevisions.CountAsync(), Is.EqualTo(3));
    Assert.That(await context.CardGroupingRevisions.CountAsync(
      revision => revision.RevisionType == CardGroupingRevisionType.Deleted), Is.EqualTo(1));
  }

  [Test]
  public async Task Test_Trade()
  {
    using var scope = _host.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<TradeContext>();
    var writer = new TradeHistoryWriter();
    var token = Guid.Parse("00000000-0000-0000-0000-000000000007");

    await writer.UpsertAccountAsync(
      context, new UserIdentity(7, "fixture-user"), CancellationToken.None);
    await writer.ApplyAsync(context, new TradeEscrowWrite(
      7, token, 123, TradeEscrowKind.Player, 8, "Partner", DateTime.UnixEpoch,
      null, 1, TradeEscrowResult.InProgress, TradeAttributionStatus.Pending,
      [new TradeItemWrite(TradeEscrowItemRole.LocalOffer, 102392, 4)],
      [new TradeMessageWrite(DateTime.UnixEpoch, 8, "Partner", "hello")],
      []), CancellationToken.None);
    await writer.ApplyAsync(context, new TradeEscrowWrite(
      7, token, 123, TradeEscrowKind.Player, 8, "Partner", DateTime.UnixEpoch,
      DateTime.UnixEpoch.AddMinutes(1), 2, TradeEscrowResult.Completed,
      TradeAttributionStatus.Inferred,
      [new TradeItemWrite(TradeEscrowItemRole.LocalOffer, 102392, 2)], [], []),
      CancellationToken.None);

    Assert.That(await context.TradeEscrows.CountAsync(), Is.EqualTo(1));
    var escrow = await context.TradeEscrows.Include(item => item.Items).SingleAsync();
    Assert.That(escrow.Result, Is.EqualTo(TradeEscrowResult.Completed));
    Assert.That(escrow.AttributionStatus, Is.EqualTo(TradeAttributionStatus.Inferred));
    Assert.That(escrow.Items.Single().Quantity, Is.EqualTo(2));
    Assert.That(await context.TradeEscrowMessages.CountAsync(), Is.EqualTo(1));

    await writer.ApplyAsync(context, new TradeEscrowWrite(
      7, Guid.Parse("00000000-0000-0000-0000-000000000008"), null,
      TradeEscrowKind.Player, 9, "Other", DateTime.UnixEpoch, null, 1,
      TradeEscrowResult.InProgress, TradeAttributionStatus.Pending, [], [], []),
      CancellationToken.None);
    await writer.EndSessionAsync(context, 7, CancellationToken.None);

    var interrupted = await context.TradeEscrows
      .AsNoTracking()
      .SingleAsync(item => item.Token.ToString().EndsWith("0008"));
    Assert.That(interrupted.Result, Is.EqualTo(TradeEscrowResult.Interrupted));
    Assert.That(interrupted.AttributionStatus,
      Is.EqualTo(TradeAttributionStatus.Unavailable));
  }

  [Test]
  public async Task Test_Cancel()
  {
    using var scope = _host.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<CollectionContext>();
    var writer = new CollectionHistoryWriter();
    using var cancellation = new CancellationTokenSource();
    cancellation.Cancel();

    var state = new CardGroupingState(
      CardGroupingKind.Collection, 0, null, null, null,
      [new CardGroupingItemState(102392, 0, 0, 1)]);

    Assert.CatchAsync<OperationCanceledException>(async () =>
      await writer.ReconcileAsync(
        context, 7, state, DateTime.UnixEpoch, cancellation.Token));
  }
}
