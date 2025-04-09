/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

using MTGOSDK.Core.Logging;

using Tracker.WebView;
using Tracker.WebView.Logging;


namespace Tracker.Services;

/// <summary>
/// Provides methods for configuring the WebView2 DevTools Console.
/// </summary>
public static class ConsoleAPIService
{
  private static ConsoleLoggerProvider? s_provider;

  /// <summary>
  /// Registers a new console logger provider for the HostForm.
  /// </summary>
  public static ConsoleLoggerProvider RegisterProvider(this HostForm hostForm)
  {
    s_provider = new ConsoleLoggerProvider(hostForm);
    return s_provider;
  }

  /// <summary>
  /// Redirects logging to the HostForm's WebView2 console.
  /// </summary>
  /// <param name="builder">The <see cref="WebApplicationBuilder"/> to configure.</param>
  /// <param name="hostForm">The <see cref="HostForm"/> to redirect logging to.</param>
  /// <returns>The <see cref="WebApplicationBuilder"/> for chaining.</returns>
  public static WebApplicationBuilder UseConsole(
    this WebApplicationBuilder builder,
    HostForm hostForm)
  {
    // Register the console logger provider if it hasn't been registered yet.
    s_provider ??= new ConsoleLoggerProvider(hostForm);

    // Redirect logging to the WebView2 console.
    builder.Logging.ClearProviders();
    builder.Logging.AddProvider(s_provider);
    Log.Debug("Logging redirected to the WebView2 console.");

    // Adds an ETW event source logger as a fallback provider.
    builder.Logging.AddEventSourceLogger();

    // Configure the logging levels for the application.
    builder.Services.AddLogging(s =>
    {
      s.AddFilter("Microsoft.Hosting.Lifetime",               LogLevel.Warning);
      s.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
      s.AddFilter("Microsoft.AspNetCore.StaticFiles.StaticFileMiddleware",
                                                              LogLevel.Warning);
      s.AddFilter("Microsoft.EntityFrameworkCore.Database.Command",
                                                              LogLevel.Warning);
    });

    return builder;
  }
}
