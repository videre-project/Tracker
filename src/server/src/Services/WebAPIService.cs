/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;

using Scalar.AspNetCore;
using Swashbuckle.AspNetCore.SwaggerGen;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Reflection.Serialization;

using Tracker.Services.Base;
using Tracker.Services.MTGO.Events;
using Tracker.Services.Videre;
using Tracker.Services.Videre.Generated;


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

    // Configure Kestrel
    builder.WebHost.ConfigureKestrel(options =>
    {
      void ConfigureEndpoint(ListenOptions listenOptions)
      {
        if (!IsCIEnabled())
        {
          listenOptions.UseHttps(GetOrCreateLocalhostCertificate(
            appOptions.UserDataFolder));
        }
        // Enable HTTP/2 with HTTP/1.1 fallback
        listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
      }

      // Vite and the Linux URL bridge reach Kestrel over the Docker network.
      // Native and CI runs remain restricted to loopback.
      if (IsContainer())
      {
        options.Listen(IPAddress.Any, appOptions.Url.Port, ConfigureEndpoint);
      }
      else
      {
        // CI runs without a user certificate and are restricted to the local
        // process. All normal application runs continue to use HTTPS.
        options.ListenLocalhost(appOptions.Url.Port, ConfigureEndpoint);
      }
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
    builder.Services.AddHttpClient<IManafoldArchetypeClient, ManafoldArchetypeClient>();
    builder.Services.AddHttpClient<VidereOpenAPIClient>(ConfigureVidereAPIClient);
    builder.Services.AddHttpClient("VidereAPI", ConfigureVidereAPIClient);
    builder.Services.AddTransient(services => new VidereAPIClient(
      services.GetRequiredService<VidereOpenAPIClient>(),
      services.GetRequiredService<System.Net.Http.IHttpClientFactory>().CreateClient("VidereAPI"),
      services.GetRequiredService<ApplicationOptions>()));

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
    if (IsCIEnabled() && options.DisableUI)
    {
      api.MapPost("/api/ci/shutdown", (IHostApplicationLifetime lifetime) =>
      {
        lifetime.StopApplication();
        return Results.NoContent();
      })
      .RequireHost("localhost")
      .ExcludeFromDescription();
    }

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

  private static bool IsDevelopmentContainer()
  {
    string? value = Environment.GetEnvironmentVariable("TRACKER_DEV_CONTAINER");
    return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
      || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
  }

  private static bool IsContainer()
  {
    return IsDevelopmentContainer()
      || string.Equals(
        Environment.GetEnvironmentVariable("TRACKER_CONTAINER"),
        "1",
        StringComparison.OrdinalIgnoreCase)
      || string.Equals(
        Environment.GetEnvironmentVariable("TRACKER_CONTAINER"),
        "true",
        StringComparison.OrdinalIgnoreCase);
  }

  private static bool IsCIEnabled()
  {
    string? value = Environment.GetEnvironmentVariable("TRACKER_CI_TEST");
    return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
      || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Returns a self-signed X.509 certificate for localhost HTTPS.
  /// </summary>
  /// <remarks>
  /// The certificate is persisted as a PFX file inside the user data folder
  /// so it survives restarts. A new certificate is generated when the file
  /// is missing or the existing one has expired (or will expire within 7 days).
  /// </remarks>
  private static X509Certificate2 GetOrCreateLocalhostCertificate(
    string userDataFolder)
  {
    var certPath = Path.Combine(userDataFolder, "localhost.pfx");

    // Try to load an existing certificate.
    if (File.Exists(certPath))
    {
      try
      {
        var existing = X509CertificateLoader.LoadPkcs12FromFile(
          certPath,
          password: null,
          keyStorageFlags: X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);

        // Reuse if it won't expire in the next 7 days.
        if (existing.NotAfter > DateTime.UtcNow.AddDays(7))
        {
          return existing;
        }

        existing.Dispose();
      }
      catch
      {
        // Corrupted or unreadable.
      }
    }

    // Try to load prebuilt container certificate if available (e.g. Wine under Docker).
    var prebuiltCertPath = "/opt/tracker/localhost.pfx";
    if (File.Exists(prebuiltCertPath))
    {
      try
      {
        return X509CertificateLoader.LoadPkcs12FromFile(
          prebuiltCertPath,
          password: "tracker-localhost",
          keyStorageFlags: X509KeyStorageFlags.EphemeralKeySet);
      }
      catch
      {
        // Unreadable or corrupted fallback.
      }
    }

    // Generate a new self-signed certificate valid for 2 years.
    // Use RSACryptoServiceProvider (CAPI) on Windows/Wine to avoid CNG CopyWithEphemeralKey P/Invoke bugs in Wine's ncrypt.dll.
    using var rsa = OperatingSystem.IsWindows()
      ? (RSA)new RSACryptoServiceProvider(2048)
      : RSA.Create(2048);

    var request = new CertificateRequest(
      "CN=localhost",
      rsa,
      HashAlgorithmName.SHA256,
      RSASignaturePadding.Pkcs1);

    // Mark as a TLS server certificate.
    request.CertificateExtensions.Add(
      new X509KeyUsageExtension(
        X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
        critical: true));
    request.CertificateExtensions.Add(
      new X509EnhancedKeyUsageExtension(
        new OidCollection { new("1.3.6.1.5.5.7.3.1") }, // id-kp-serverAuth
        critical: false));

    // SAN: DNS=localhost + loopback IPs
    var sanBuilder = new SubjectAlternativeNameBuilder();
    sanBuilder.AddDnsName("localhost");
    sanBuilder.AddIpAddress(IPAddress.Loopback);
    sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);
    request.CertificateExtensions.Add(sanBuilder.Build());

    using var tempCert = request.CreateSelfSigned(
      DateTimeOffset.UtcNow.AddDays(-1),
      DateTimeOffset.UtcNow.AddYears(2));

    var pfxBytes = tempCert.Export(X509ContentType.Pfx);

    // Persist for future runs.
    Directory.CreateDirectory(userDataFolder);
    File.WriteAllBytes(certPath, pfxBytes);

    return X509CertificateLoader.LoadPkcs12(
      pfxBytes,
      password: null,
      keyStorageFlags: X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet);
  }

  private static void ConfigureVidereAPIClient(
    IServiceProvider services,
    System.Net.Http.HttpClient client)
  {
    var options = services.GetRequiredService<ApplicationOptions>();
    client.BaseAddress = new Uri($"{options.VidereAPIUrl.TrimEnd('/')}/");
    client.DefaultRequestHeaders.UserAgent.ParseAdd($"VidereTracker/{ProductInfo.Version}");
  }

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
