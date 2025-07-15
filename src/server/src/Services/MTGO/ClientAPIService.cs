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

    // Start a background task to initialize the client
    SyncThread.EnqueueAsync(async () =>
    {
      if (provider.Client == null)
      {
        Log.Trace("Initializing the MTGO client with options: {options}", options);
        await provider.WaitForRemoteClientAsync(options);
      }
      else
      {
        Log.Trace("MTGO client is already initialized.");
      }
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
        // Abort if the response has already started sending.
        if (!context.Response.HasStarted) return;

        // If a new process has started since the exception was thrown,
        // reinitialize and re-register the client with the service collection.
        var provider = context.RequestServices.GetRequiredService<IClientAPIProvider>();
        await provider.WaitForRemoteClientAsync(null, context.RequestAborted);

        // If we cannot reinitialize the client, throw the exception
        if (!RemoteClient.IsInitialized) throw;

        // Otherwise, retry the request.
        await next(context);
      }
      catch (Exception ex)
      {
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
