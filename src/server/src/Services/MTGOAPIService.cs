/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

using MTGOSDK.API;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting;


namespace Tracker.Services;

/// <summary>
/// Provides methods for configuring the MTGOSDK API service.
/// </summary>
public static class MTGOAPIService
{
  public static IHostApplicationBuilder UseMTGOAPIClient(
    this IHostApplicationBuilder builder,
    ClientOptions options = default)
  {
    builder.Services.AddSingleton(_ => new Client(options));
    builder.Services.AddHostedService<ClientService>();

    return builder;
  }

  /// <summary>
  /// A background service that monitors the MTGOSDK client connection.
  /// </summary>
  public class ClientService(IServiceProvider provider) : BackgroundService
  {
    private bool _hasHooks = false;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private CancellationTokenSource _cancellationTokenSource = new();

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
      await _semaphore.WaitAsync(cancellationToken);
      try
      {
        // Cancel any ongoing StopAsync tasks
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource = new CancellationTokenSource();

        // Check if a MTGO process currently exists that we can connect to.
        if (Client.HasStarted)
        {
          var client = provider.GetRequiredService<Client>();
          if (!_hasHooks)
          {
            _hasHooks = true;
            client.IsConnectedChanged += delegate(object? sender)
            {
              if (!Client.IsConnected)
              {
                client.Dispose();
                _hasHooks = false;
              }
            };
          }
        }
      }
      finally
      {
        _semaphore.Release();
      }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
      await _semaphore.WaitAsync(cancellationToken);
      try
      {
        var client = provider.GetRequiredService<Client>();
        client.ClearCaches();
        client.Dispose();
        _hasHooks = false;
      }
      finally
      {
        _semaphore.Release();
      }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
      Log.Information("Timed Hosted Service running.");

      // When the timer should have no due-time, then do the work once now.
      Heartbeat();

      using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));

      try
      {
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
          Heartbeat();
        }
      }
      catch (OperationCanceledException)
      {
        Log.Information("Timed Hosted Service is stopping.");
      }
    }

    private void Heartbeat()
    {
      if (RemoteClient.CheckHeartbeat()) return;

      // Restart the client if the heartbeat fails.
      RemoteClient.Dispose();
      RemoteClient.EnsureInitialize();
    }
  }
}
