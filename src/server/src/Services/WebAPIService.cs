/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

using Scalar.AspNetCore;

using MTGOSDK.Core.Reflection.Serialization;


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
    });

    // Set the HTTPS endpoint for the Web API.
    builder.WebHost.UseUrls(options.Url.ToString());

    // Add services to the container.
    builder.Services.AddControllers().AddJsonOptions(options =>
    {
      // Configures the JSON serializer options to match JsonSerializableBase.
      var jsonOptions = options.JsonSerializerOptions;
      jsonOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
      jsonOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
      jsonOptions.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
      jsonOptions.IncludeFields = false;
      jsonOptions.Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
      jsonOptions.WriteIndented = true;

      // Add the JsonSerializableBaseConverter to the serializer options.
      // This allows for safely serializing remote objects to JSON.
      jsonOptions.Converters.Add(new JsonSerializableConverter());
      jsonOptions.Converters.Add(new JsonSerializableEnumerableConverter());
    });
    builder.Services.AddOpenApi();

    return builder;
  }

  /// <summary>
  /// Initializes the ASP.NET Core Web API service.
  /// </summary>
  /// <param name="builder">The builder for the Web API host.</param>
  /// <param name="hostForm">The host form for the WebView2 control.</param>
  /// <returns>A new <see cref="WebApplication"/> instance.</returns>
  public static WebApplication CreateAPIService(this WebApplication api)
  {
    api.UseHttpsRedirection();

    // Use the embedded static files provided by the client.
    api.UseFileServer(new FileServerOptions
    {
      FileProvider = new ManifestEmbeddedFileProvider(Assembly.GetEntryAssembly()!),
      EnableDefaultFiles = true,
      EnableDirectoryBrowsing = false,
    });
    api.UseDefaultFiles();

    // Configure the HTTP request pipeline.
    if (api.Environment.IsDevelopment())
    {
      api.MapOpenApi();
      api.MapScalarApiReference();
    }
    api.UseRouting();
    api.UseAuthorization();

    api.MapControllers();
    api.MapFallbackToFile("/index.html");

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
