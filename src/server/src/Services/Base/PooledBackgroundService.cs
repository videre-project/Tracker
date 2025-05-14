/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;


namespace Tracker.Services.Base;

/// <summary>
/// Base class for implementing a long running <see cref="IHostedService"/>.
/// </summary>
/// <remarks>
/// This class is a copy of the <see cref="BackgroundService"/> class that
/// prevents direct invocation of the <see cref="ExecuteAsync(CancellationToken)"/>
/// method from blocking the ASP.NET Core hosting lifetime.
/// </remarks>
public abstract class PooledBackgroundService
    : BackgroundService, IHostedService
{
  private Task? _executeTask;
  private CancellationTokenSource? _stoppingCts;

  /// <inheritdoc />
  public override Task? ExecuteTask => _executeTask;

  protected async Task WrapExecute()
  {
    string? originalThreadName = Thread.CurrentThread.Name;
    try
    {
      Thread.CurrentThread.Name = this.GetType().Name;
      // Await the task returned by ExecuteAsync
      await ExecuteAsync(_stoppingCts!.Token).ConfigureAwait(false);
    }
    finally
    {
      // Restore the original thread name
      Thread.CurrentThread.Name = originalThreadName;
    }
  }

  /// <inheritdoc />
  public override Task StartAsync(CancellationToken cancellationToken)
  {
    _stoppingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    _executeTask = Task.Factory.StartNew(
      WrapExecute,
      _stoppingCts.Token,
      TaskCreationOptions.LongRunning,
      TaskScheduler.Default).Unwrap();

    if (_executeTask.IsCompleted)
    {
      return _executeTask;
    }

    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public override async Task StopAsync(CancellationToken cancellationToken)
  {
    // Stop called without start
    if (_executeTask == null) return;

    try
    {
      // Signal cancellation to the executing method
      _stoppingCts!.Cancel();
    }
    finally
    {
      await _executeTask.WaitAsync(cancellationToken)
        .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }
  }

  /// <inheritdoc />
  public override void Dispose()
  {
    _stoppingCts?.Cancel();
    GC.SuppressFinalize(this);
  }
}
