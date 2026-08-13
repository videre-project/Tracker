/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Tracker.Controllers.Base;


namespace Tracker.Tests;

[TestFixture]
public sealed class StreamLifecycleTests
{
  [Test]
  public async Task Test_Coalesce()
  {
    var controller = new ProbeController();
    Assert.That(await controller.CoalesceAsync(),
      Is.EqualTo(new[] { "new", "second" }));
  }

  [Test]
  public async Task Test_Unsubscribe()
  {
    using var cancellation = new CancellationTokenSource();
    var controller = new ProbeController(cancellation.Token);
    EventHandler<int>? subscribed = null;
    var unsubscribeCount = 0;
    var processed = new TaskCompletionSource<int>(
      TaskCreationOptions.RunContinuationsAsynchronously);

    Task stream = controller.StreamAsync(
      callback => subscribed = callback,
      callback =>
      {
        unsubscribeCount++;
      },
      (_, value) =>
      {
        processed.TrySetResult(value);
        return Task.CompletedTask;
      });

    await SpinWaitAsync(() => subscribed != null);
    subscribed!(null, 7);
    Assert.That(await processed.Task.WaitAsync(TimeSpan.FromSeconds(2)),
      Is.EqualTo(7));

    cancellation.Cancel();
    await stream.WaitAsync(TimeSpan.FromSeconds(2));
    subscribed(null, 8);

    Assert.That(unsubscribeCount, Is.EqualTo(1));
    Assert.That(processed.Task.Result, Is.EqualTo(7));
  }

  private static async Task SpinWaitAsync(Func<bool> condition)
  {
    for (var attempt = 0; attempt < 100 && !condition(); attempt++)
      await Task.Delay(10);
    Assert.That(condition(), Is.True);
  }

  private sealed class ProbeController : APIController
  {
    private readonly CancellationToken _cancellationToken;

    public ProbeController(CancellationToken cancellationToken = default)
    {
      _cancellationToken = cancellationToken;
      ControllerContext = new ControllerContext
      {
        HttpContext = new DefaultHttpContext
        {
          RequestAborted = cancellationToken,
        },
      };
    }

    public async Task<IReadOnlyList<string>> CoalesceAsync()
    {
      var queue = new CoalescingUpdateQueue<int, string>();
      await using var enumerator = queue.ReadAllAsync(CancellationToken.None)
        .GetAsyncEnumerator();

      Assert.That(queue.Enqueue(1, "old"), Is.True);
      Assert.That(queue.Enqueue(1, "new"), Is.True);
      Assert.That(queue.Enqueue(2, "second"), Is.True);

      var values = new List<string>();
      Assert.That(await enumerator.MoveNextAsync(), Is.True);
      values.Add(enumerator.Current);
      Assert.That(await enumerator.MoveNextAsync(), Is.True);
      values.Add(enumerator.Current);

      queue.Complete();
      Assert.That(await enumerator.MoveNextAsync(), Is.False);
      return values;
    }

    public Task<IActionResult> StreamAsync(
      Action<EventHandler<int>> subscribe,
      Action<EventHandler<int>> unsubscribe,
      Func<object?, int, Task> onEvent) =>
      StreamNdjsonEventHandler(
        subscribe,
        unsubscribe,
        onEvent,
        _cancellationToken);
  }
}
