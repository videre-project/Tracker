/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;

using Tracker.WebView;
using Tracker.WebView.Logging;


namespace Tracker.Services;

/// <summary>
/// Provides methods for configuring the WebView2 DevTools Console.
/// </summary>
public static class ConsoleAPIService
{
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
    // Redirect logging to the WebView2 console.
    builder.Logging.ClearProviders();
    builder.Logging.AddProvider(new ConsoleLoggerProvider(hostForm));

    return builder;
  }
}
