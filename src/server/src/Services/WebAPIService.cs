/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;

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
  /// <param name="appOptions">The application options.</param>
  /// <returns>A new <see cref="WebApplicationBuilder"/> instance.</returns>
  public static WebApplicationBuilder CreateHostBuilder(
    ApplicationOptions appOptions)
  {
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
      Args = appOptions.Args,
      ContentRootPath = appOptions.ContentRootPath,
    });

    // Register ApplicationOptions as a singleton for dependency injection
    builder.Services.AddSingleton(appOptions);

    // Configure Kestrel with HTTP/2 over HTTPS (multiplexes streams over single connection)
    builder.WebHost.ConfigureKestrel(options =>
    {
      options.ListenLocalhost(appOptions.Url.Port, listenOptions =>
      {
        listenOptions.UseHttps();
        // Enable HTTP/2 with HTTP/1.1 fallback
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
      });
    });

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

      // Add support for serializing enums as strings with capitalized camel case values.
      jsonOptions.Converters.Add(new JsonStringEnumConverter(
        new SerializationPolicies.CapitalizedCamelCaseNamingPolicy()));
    });

    // Register HttpClient factory for external API calls
    builder.Services.AddHttpClient();

    // Enable CORS for frontend development
    builder.Services.AddCors(options =>
    {
      options.AddDefaultPolicy(policy =>
      {
        policy.SetIsOriginAllowed(origin => new Uri(origin).Host == "localhost")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
      });
    });

    // Configure Swagger/OpenAPI
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
      options.SwaggerDoc("v1", new OpenApiInfo
      {
        Title = "Videre Tracker API",
        Version = "v1",
        Description = "Magic Online Tournament Tracker API",
      });

      // Include XML comments for better documentation
      var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
      var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
      if (File.Exists(xmlPath))
      {
        options.IncludeXmlComments(xmlPath);
      }

      // Enable annotations for better documentation
      options.EnableAnnotations();

      // Custom schema IDs to avoid conflicts
      options.CustomSchemaIds(type => type.FullName?.Replace("+", "."));

      // Support for streaming responses
      options.MapType<IAsyncEnumerable<object>>(() => new OpenApiSchema
      {
        Type = JsonSchemaType.Array,
        Items = new OpenApiSchema { Type = JsonSchemaType.Object }
      });
    });

    return builder;
  }

  /// <summary>
  /// Initializes the ASP.NET Core Web API service.
  /// </summary>
  /// <param name="api">The Web API application.</param>
  /// <returns>A new <see cref="WebApplication"/> instance.</returns>
  public static WebApplication CreateAPIService(
    this WebApplication api,
    ApplicationOptions options)
  {
    api.UseHttpsRedirection();

    // Use the embedded static files provided by the client.
    if (!options.DisableUI)
    {
      api.UseFileServer(new FileServerOptions
      {
        FileProvider = new ManifestEmbeddedFileProvider(Assembly.GetEntryAssembly()!),
        EnableDefaultFiles = true,
        EnableDirectoryBrowsing = false,
      });
      api.UseDefaultFiles();
    }

    // Configure the HTTP request pipeline.
    if (api.Environment.IsDevelopment())
    {
      // Use Swagger to generate OpenAPI documentation.
      api.UseSwagger(o => o.RouteTemplate = "/openapi/{documentName}.json");

      api.MapOpenApi();
      api.MapScalarApiReference(endpointPrefix: "/docs");
    }
    api.UseRouting();
    api.UseCors();
    api.UseAuthorization();

    api.MapControllers();
    if (!options.DisableUI)
    {
      api.MapFallbackToFile("/index.html");
    }

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
