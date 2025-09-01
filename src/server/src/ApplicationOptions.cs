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
}
