/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

using MTGOSDK.API;
using MTGOSDK.Core.Exceptions;
using MTGOSDK.Core.Remoting;
using static MTGOSDK.Core.Reflection.DLRWrapper;


namespace Tracker.Services;

/// <summary>
/// Provides methods for configuring the MTGOSDK API service.
/// </summary>
public static class MTGOAPIService
{
  public static IHostApplicationBuilder RegisterClientSingleton(
    this IHostApplicationBuilder builder,
    ClientOptions options = default)
  {
    builder.Services.AddSingleton(_ => new Client(options));
    return builder;
  }

  public static IApplicationBuilder UseClientMiddleware(
        this IApplicationBuilder builder)
  {
    return builder.UseMiddleware<ClientMiddleware>();
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
      catch (ProcessCrashedException ex)
      {
        // Abort if the response has already started sending.
        if (!context.Response.HasStarted) return;

        // If a new process has started since the exception was thrown,
        // reinitialize and re-register the client with the service collection.
        if (await WaitUntil(() => RemoteClient.Port != (ushort)ex.ProcessId) &&
            RemoteClient.HasStarted)
        {
          // Wait for the previous client to dispose.
          await RemoteClient.WaitForDisposeAsync();
          // Register a new client instance.
          if (RemoteClient.Port == null)
          {
            var provider = context.RequestServices;
            var services = provider.GetRequiredService<IServiceCollection>();
            services.Remove(services.First(d => d.ServiceType == typeof(Client)));
            services.AddSingleton(new Client());
          }
        }

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
