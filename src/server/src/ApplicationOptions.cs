/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;


namespace Tracker;

public class ApplicationOptions(string[] args = null!)
{
  /// <summary>
  /// The command-line arguments.
  /// </summary>
  public string[] Args { get; internal set; } = args ?? Array.Empty<string>();

  /// <summary>
  /// The port number to use for the Web API.
  /// </summary>
  public int Port { get; internal set; } = 7101;

  /// <summary>
  /// The URL for the Web API.
  /// </summary>
  public Uri Url => new($"https://localhost:{Port}");

  /// <summary>
  /// The URL that the WebView2 UI should load.
  /// </summary>
  /// <remarks>
  /// In Development, this defaults to the Vite dev server.
  /// Override with the <c>TRACKER_UI_URL</c> environment variable.
  /// </remarks>
  public Uri UiUrl
  {
    get
    {
      var overrideUrl = Environment.GetEnvironmentVariable("TRACKER_UI_URL");
      if (!string.IsNullOrWhiteSpace(overrideUrl) &&
          Uri.TryCreate(overrideUrl, UriKind.Absolute, out var uiUrl))
      {
        return uiUrl;
      }

      if (IsDevelopment)
      {
        // Keep in sync with the SpaProxyServerUrl in server.csproj (defaults to 5279)
        var devPort = Environment.GetEnvironmentVariable("DEV_SERVER_PORT")
          ?? Environment.GetEnvironmentVariable("SPA_DEV_SERVER_PORT");

        if (int.TryParse(devPort, out var port) && port > 0)
        {
          return new Uri($"https://localhost:{port}");
        }

        return new Uri("https://localhost:5279");
      }

      return Url;
    }
  }

  public bool IsDarkMode { get; internal set; } =
    Environment.GetEnvironmentVariable("APP_THEME") == "Dark";

  /// <summary>
  /// Indicates whether the application is running in development mode.
  /// </summary>
  public bool IsDevelopment { get; internal set; } =
    Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development";

  /// <summary>
  /// Controls whether to render a custom title bar and window controls.
  /// </summary>
  public bool UseCustomTitleBar { get; internal set; } = true;

  /// <summary>
  /// The base directory path for the application's static content.
  /// </summary>
  /// <remarks>
  /// Defaults to the install directory executing the application.
  /// </remarks>
  public string ContentRootPath { get; internal set; } =
    AppDomain.CurrentDomain.BaseDirectory;

  /// <summary>
  /// The base directory path for user application data.
  /// </summary>
  /// <remarks>
  /// Defaults to <c>%LocalAppData%\Videre Tracker</c>
  /// </remarks>
  public string UserDataFolder { get; internal set; } =
    Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      typeof(Program).Assembly.GetName().Name!
    );

  /// <summary>
  /// The full path to the SQLite database file.
  /// </summary>
  public string DatabasePath => Path.Combine(this.UserDataFolder, "Database");

  /// <summary>
  /// Whether to disable the WebView2 UI and run the Web API only.
  /// </summary>
  public bool DisableUI { get; internal set; } = false;

  /// <summary>
  /// The base URL for the NBAC (Naive Bayes Archetype Classification) API.
  /// </summary>
  /// <remarks>
  /// Defaults to the production endpoint. Can be overridden via NBAC_API_URL environment variable.
  /// </remarks>
  public string NbacApiUrl { get; internal set; } =
    Environment.GetEnvironmentVariable("NBAC_API_URL")
      ?? "https://ml.videreproject.com/nbac";
}
