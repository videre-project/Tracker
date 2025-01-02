/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MTGOSDK.API;
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
    //
    // Register an instance of the MTGOSDK client with the service provider.
    //
    // This lets us use a single instance of the client to manage global state
    // and event subscriptions that might otherwise create duplicate instances.
    //
    builder.Services.AddSingleton(serviceProvider =>
    {
      return new Client(options);
    });

    //
    // Register a hosted client service to dispose when the application stops.
    //
    // Since the underlying RemoteClient is a singleton that automatically gets
    // re-initialized when any remote objects are accessed, we only need to
    // manage the lifecycle of the client at startup and shutdown to ensure that
    // the client's resources are cleaned up after the application exits.
    //
    builder.Services.AddTransient<IHostedService>(serviceProvider =>
    {
      return new ClientService(serviceProvider);
    });

    return builder;
  }

  /// <summary>
  /// A hosted service wrapper that manages the lifecycle of the MTGOSDK client.
  /// </summary>
  private class ClientService(IServiceProvider provider) : IHostedService
  {
    private bool _hasHooks = false;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private CancellationTokenSource _cancellationTokenSource = new();

    public async Task StartAsync(CancellationToken cancellationToken)
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
        RemoteClient.EnsureInitialize();
        _semaphore.Release();
      }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
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
  }
}
