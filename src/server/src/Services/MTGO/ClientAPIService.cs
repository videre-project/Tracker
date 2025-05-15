/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;

using MTGOSDK.API;
using MTGOSDK.Core.Exceptions;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting;


namespace Tracker.Services.MTGO;

/// <summary>
/// Provides methods for configuring the MTGOSDK API service.
/// </summary>
public static class ClientAPIService
{
  private static ushort? s_pid;
  private static ClientOptions s_options;

  private static Client s_client = null!;
  public static Client Client => s_client;

  private static void InitializeClient(ClientOptions options)
  {
    s_client = new(options);

    s_pid = RemoteClient.Port;
    s_options = options;
  }

  private static readonly SemaphoreSlim _semaphore = new(1, 1);

  public static IHostApplicationBuilder RegisterClientSingleton(
    this IHostApplicationBuilder builder,
    ClientOptions options = default)
  {
    InitializeClient(options);
    builder.Services.AddSingleton(_ => Client);

    return builder;
  }

  public static IApplicationBuilder UseClientMiddleware(
        this IApplicationBuilder builder)
  {
    return builder.UseMiddleware<ClientMiddleware>();
  }

  public static async Task WaitForRemoteClientAsync(IServiceProvider provider)
  {
    // Register a new client instance.
    await _semaphore.WaitAsync();
    try
    {
      // Wait for the previous client to dispose.
      Log.Trace("Waiting for previous client to dispose...");
      await RemoteClient.WaitForDisposeAsync();

      Log.Trace("Waiting for a new MTGO process to start...");
      await Client.WaitForMTGOProcess(TimeSpan.FromMinutes(10));

      // Log the process ID of the new MTGO process.
      short pid = (short)RemoteClient.MTGOProcess()!.Id;
      Log.Trace("Found a new MTGO process with PID {pid}.", pid);

      Log.Trace("Waiting for the user to log in...");
      await Client.WaitForUserLogin(TimeSpan.FromMinutes(5));

      // Re-initialize the client (re-using the new RemoteClient instance).
      Log.Trace("User has logged in, re-initializing the client instance...");
      InitializeClient(s_options);
      if (pid != s_pid)
      {
        throw new InvalidOperationException(
          $"The MTGO process ID {pid} does not match the expected PID {s_pid}.");
      }

      Log.Trace("Waiting for the client to finish initializing...");
      await s_client.WaitForClientReady();
    }
    finally
    {
      _semaphore.Release();
    }
  }

  public class ClientMiddleware(RequestDelegate next)
  {
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Process an individual request.
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    public async Task InvokeAsync(HttpContext context)
    {
      await _semaphore.WaitAsync(context.RequestAborted);
      try
      {
        await next(context);
      }
      catch (ProcessCrashedException)
      {
        // Abort if the response has already started sending.
        if (!context.Response.HasStarted) return;

        // If a new process has started since the exception was thrown,
        // reinitialize and re-register the client with the service collection.
        await WaitForRemoteClientAsync(context.RequestServices);

        // If a new client is initialized, retry the request.
        if (RemoteClient.IsInitialized)
        {
          await next(context);
          return;
        }

        throw;
      }
      finally
      {
        _semaphore.Release();
      }
    }
  }
}
