/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

using Scalar.AspNetCore;


namespace Tracker.Services;

/// <summary>
/// Provides methods for configuring the ASP.NET Core Web API service.
/// </summary>
public static class WebAPIService
{
  /// <summary>
  /// Initializes the builder for the Web API host.
  /// </summary>
  /// <param name="options">The application options.</param>
  /// <returns>A new <see cref="WebApplicationBuilder"/> instance.</returns>
  public static WebApplicationBuilder CreateHostBuilder(ApplicationOptions options)
  {
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
      Args = options.Args,
      ContentRootPath = options.ContentRootPath,
      WebRootPath = options.IsDevelopment
        ? Path.Combine(options.ContentRootPath, "../../../..", "client", "dist")
        : Path.Combine(options.ContentRootPath, "wwwroot")
    });

    // Set the HTTPS endpoint for the Web API.
    builder.WebHost.UseUrls($"https://localhost:{options.Port}");

    // Add services to the container.
    builder.Services.AddControllers();
    builder.Services.AddOpenApi();

    return builder;
  }

  /// <summary>
  /// Initializes the ASP.NET Core Web API service.
  /// </summary>
  /// <param name="builder">The builder for the Web API host.</param>
  /// <param name="hostForm">The host form for the WebView2 control.</param>
  /// <returns>A new <see cref="WebApplication"/> instance.</returns>
  public static WebApplication CreateAPIService(this WebApplicationBuilder builder)
  {
    var api = builder.Build();

    // Use the embedded static files provided by the client.
    api.UseFileServer(new FileServerOptions
    {
      FileProvider = new ManifestEmbeddedFileProvider(typeof(Program).Assembly),
      EnableDefaultFiles = true,
      EnableDirectoryBrowsing = false,
    });

    // Configure the HTTP request pipeline.
    if (api.Environment.IsDevelopment())
    {
      api.MapOpenApi();
      api.MapScalarApiReference();
    }
    api.UseRouting();
    api.UseAuthorization();

    api.MapControllers();
    api.MapFallbackToFile("index.html");

    return api;
  }

  /// <summary>
  /// Registers a callback to be invoked when the Web API is shutting down.
  /// </summary>
  /// <param name="api">The <see cref="WebApplication"/> to configure.</param>
  /// <param name="callback">The callback to invoke.</param>
  /// <returns>The <see cref="WebApplication"/> for chaining.</returns>
  public static WebApplication OnShutdown(this WebApplication api, Action callback)
  {
    api.Lifetime.ApplicationStopping.Register(callback);
    return api;
  }
}
