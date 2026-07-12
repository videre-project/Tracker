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
using Swashbuckle.AspNetCore.SwaggerGen;

using MTGOSDK.Core.Reflection.Serialization;

using Tracker.Services.Base;
using Tracker.Services.MTGO.Events;
using Tracker.Services.Videre;


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
    builder.Services.AddHttpClient<INBACArchetypeClient, NBACArchetypeClient>();
    builder.Services.AddHttpClient<VidereAPIClient>();

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

      // Custom schema IDs to avoid conflicts while keeping generated TypeScript
      // names readable for closed generics.
      options.CustomSchemaIds(GetOpenApiSchemaId);

      // Support for streaming responses
      options.MapType<IAsyncEnumerable<object>>(() => new OpenApiSchema
      {
        Type = JsonSchemaType.Array,
        Items = new OpenApiSchema { Type = JsonSchemaType.Object }
      });

      // Match MTGOSDK's runtime JSON converter behavior: properties marked
      // [NonSerializable] are not emitted by default serialization.
      options.SchemaFilter<MTGONonSerializableSchemaFilter>();

      // Include event data DTOs in the schema (not directly referenced by
      // endpoints, but needed for frontend type generation from the Data
      // field of GameLogDTO).
      options.DocumentFilter<GameEventDataSchemaFilter>();
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

    // Use Swagger to generate OpenAPI documentation.
    api.UseSwagger(o => o.RouteTemplate = "/openapi/{documentName}.json");

    api.MapOpenApi();
    api.MapScalarApiReference("/docs", scalarOptions =>
    {
      scalarOptions.WithTitle("Videre Tracker API");
      scalarOptions.WithTheme(ScalarTheme.DeepSpace);
      scalarOptions.ForceDarkMode();
      scalarOptions.HideDarkModeToggle();
      scalarOptions.HideClientButton();
      scalarOptions.HideDeveloperTools();
      scalarOptions.DisableAgent();
      scalarOptions.WithCustomCss("""
        :root {
          --scalar-background-1: #020817;
          --scalar-background-2: #06111f;
          --scalar-background-3: #0f172a;
          --scalar-border-color: rgba(148, 163, 184, 0.18);
          --scalar-color-1: #f8fafc;
          --scalar-color-2: #cbd5e1;
          --scalar-color-3: #94a3b8;
          --scalar-accent: #38bdf8;
        }

        body {
          background: #020817;
          margin: 0;
        }

        * {
          scrollbar-width: thin;
          scrollbar-color: rgba(148, 163, 184, 0.48) transparent;
        }

        *:hover {
          scrollbar-color: rgba(203, 213, 225, 0.64) transparent;
        }

        *::-webkit-scrollbar {
          width: 8px;
          height: 8px;
        }

        *::-webkit-scrollbar-track {
          background: #020817;
        }

        *::-webkit-scrollbar-thumb {
          background: rgba(148, 163, 184, 0.44);
          border: 2px solid #020817;
          border-radius: 999px;
        }

        *::-webkit-scrollbar-thumb:hover {
          background: rgba(203, 213, 225, 0.64);
        }

        *::-webkit-scrollbar-corner {
          background: #020817;
        }

        .scalar-app,
        .scalar-api-reference {
          background: #020817;
        }

        .scalar-app aside,
        .scalar-api-reference aside {
          background:
            linear-gradient(180deg, rgba(15, 23, 42, 0.84), rgba(2, 8, 23, 0.98)),
            #020817;
          border-color: rgba(148, 163, 184, 0.18);
        }
        """);
    });

    api.UseRouting();
    api.UseCors();
    api.UseAuthorization();
    api.UseMiddleware<RequestMetricsMiddleware>();

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

  private static string GetOpenApiSchemaId(Type type)
  {
    if (!type.IsGenericType)
    {
      return NormalizeOpenApiSchemaId(type.FullName ?? type.Name);
    }

    var genericDefinitionName =
      type.GetGenericTypeDefinition().FullName ??
      type.GetGenericTypeDefinition().Name;
    var arityIndex = genericDefinitionName.IndexOf('`');
    if (arityIndex >= 0)
    {
      genericDefinitionName = genericDefinitionName[..arityIndex];
    }

    var genericArguments = type.GetGenericArguments();
    if (HasOnlyObjectGenericArguments(genericArguments))
    {
      return NormalizeOpenApiSchemaId(genericDefinitionName);
    }

    var genericArgumentNames = new string[genericArguments.Length];
    for (int i = 0; i < genericArguments.Length; i++)
    {
      genericArgumentNames[i] = GetOpenApiGenericArgumentName(genericArguments[i]);
    }

    return NormalizeOpenApiSchemaId(
      $"{genericDefinitionName}{string.Join("", genericArgumentNames)}");
  }

  private static string GetOpenApiGenericArgumentName(Type type)
  {
    if (type.IsArray)
    {
      return $"{GetOpenApiGenericArgumentName(type.GetElementType()!)}Array";
    }

    if (!type.IsGenericType)
    {
      return NormalizeOpenApiSchemaId(type.Name);
    }

    var genericDefinitionName = type.GetGenericTypeDefinition().Name;
    var arityIndex = genericDefinitionName.IndexOf('`');
    if (arityIndex >= 0)
    {
      genericDefinitionName = genericDefinitionName[..arityIndex];
    }

    var genericArguments = type.GetGenericArguments();
    var genericArgumentNames = new string[genericArguments.Length];
    for (int i = 0; i < genericArguments.Length; i++)
    {
      genericArgumentNames[i] = GetOpenApiGenericArgumentName(genericArguments[i]);
    }

    return NormalizeOpenApiSchemaId(
      $"{genericDefinitionName}{string.Join("", genericArgumentNames)}");
  }

  private static string NormalizeOpenApiSchemaId(string schemaId) =>
    schemaId.Replace("+", ".");

  private static bool HasOnlyObjectGenericArguments(Type[] genericArguments)
  {
    if (genericArguments.Length == 0)
    {
      return false;
    }

    foreach (var genericArgument in genericArguments)
    {
      if (genericArgument != typeof(object))
      {
        return false;
      }
    }

    return true;
  }
}

/// <summary>
/// Includes game event data DTOs in the OpenAPI schema so they are available
/// for frontend type generation, even though no endpoint returns them directly.
/// </summary>
internal class GameEventDataSchemaFilter : IDocumentFilter
{
  public void Apply(OpenApiDocument document, DocumentFilterContext context)
  {
    context.SchemaGenerator.GenerateSchema(typeof(GameStateData), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(ZoneTransferData), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(CardChangeData), context.SchemaRepository);
    context.SchemaGenerator.GenerateSchema(typeof(PlayerChangeData), context.SchemaRepository);
  }
}
