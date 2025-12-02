/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;

using MTGOSDK.API;
using MTGOSDK.Core;
using MTGOSDK.Core.Exceptions;
using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting;


namespace Tracker.Services.MTGO;

/// <summary>
/// Provides methods for configuring the MTGOSDK API service.
/// </summary>
public static class ClientAPIService
{
  public static IHostApplicationBuilder RegisterClientAPIProvider(
    this IHostApplicationBuilder builder,
    ClientOptions options = default)
  {
    var provider = new ClientAPIProvider();
    builder.Services.AddSingleton<IClientAPIProvider>(provider);

    // Register the client state monitor as scoped (per-request)
    builder.Services.AddScoped<ClientStateMonitor>();

    // Start a background task to initialize the client
    SyncThread.EnqueueAsync(async () =>
    {
      await provider.RunClientLoopAsync(options);
    });

    return builder;
  }

  public static IApplicationBuilder UseClientMiddleware(
        this IApplicationBuilder builder)
  {
    return builder.UseMiddleware<ClientMiddleware>();
  }

  public class ClientMiddleware(RequestDelegate next)
  {
    public async Task InvokeAsync(HttpContext context)
    {
      try
      {
        await next(context);
      }
      catch (ProcessCrashedException)
      {
        // Don't attempt to modify response if it has already started
        if (context.Response.HasStarted)
        {
          Log.Warning("MTGO process crashed during active response");
          return;
        }

        // If a new process has started since the exception was thrown,
        // reinitialize and re-register the client with the service collection.
        var provider = context.RequestServices.GetRequiredService<IClientAPIProvider>();
        await provider.WaitSemaphoreAsync(context.RequestAborted);

        // If we cannot reinitialize the client, return service unavailable
        if (!RemoteClient.IsInitialized)
        {
          context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
          await context.Response.WriteAsync("MTGO client is not available");
          return;
        }

        // Otherwise, retry the request.
        await next(context);
      }
      catch (OperationCanceledException)
      {
        // Request was cancelled, this is expected behavior when client disconnects
        if (context.Response.HasStarted)
        {
          Log.Information("Request cancelled during active response");
        }
        return;
      }
      catch (Exception ex)
      {
        // Don't attempt to modify response if it has already started
        if (context.Response.HasStarted)
        {
          Log.Error("Error occurred during active response: {ex}", ex);
          return;
        }

        // Log the exception properly as the developer middleware does not log
        // the stacktrace of the exception.
        Log.Error("An error occurred while processing the request: {ex}", ex);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsync(
          "An error occurred while processing the request.");
      }
    }
  }
}
