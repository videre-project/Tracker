/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tracker.Plugins;

namespace Example;

public sealed record SessionSnapshot(bool Connected, int? UserId, int GameCount);

public sealed class SessionObserver(
  IMTGOContext context,
  ObserverOptions options,
  ILogger<SessionObserver> logger) : BackgroundService
{
  private readonly Channel<bool> _updates = Channel.CreateBounded<bool>(
    new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
  private SessionSnapshot _snapshot = new(false, null, 0);

  public SessionSnapshot Snapshot => Volatile.Read(ref _snapshot);

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    // Subscribe before the initial read to cover sessions already open at startup.
    context.Changed += OnChanged;
    _updates.Writer.TryWrite(true);
    try
    {
      await foreach (bool _ in _updates.Reader.ReadAllAsync(stoppingToken))
      {
        var snapshot = new SessionSnapshot(context.Client is not null,
          context.UserId, context.Games.Count);
        Volatile.Write(ref _snapshot, snapshot);
        try
        {
          await File.WriteAllTextAsync(Path.Combine(options.DataDirectory, "session.json"),
            JsonSerializer.Serialize(snapshot), stoppingToken);
        }
        catch (IOException exception)
        {
          logger.LogWarning(exception, "Could not write the session snapshot.");
        }
        catch (UnauthorizedAccessException exception)
        {
          logger.LogWarning(exception, "Could not write the session snapshot.");
        }
      }
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    finally
    {
      context.Changed -= OnChanged;
      logger.LogInformation("Session observer stopped.");
    }
  }

  // Notifications may overlap and run on recording/connection worker threads.
  // Queue work instead of doing IO or blocking the host in this callback.
  private void OnChanged(object? sender, EventArgs args) => _updates.Writer.TryWrite(true);
}
