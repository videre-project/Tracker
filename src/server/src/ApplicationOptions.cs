/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;


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
  /// When set, enables Wine-specific compatibility workarounds (native title
  /// bar and software-rendered WebView2). Auto-enabled when running under Wine
  /// or when <c>TRACKER_WINE_COMPAT</c> is set to a truthy value.
  /// </summary>
  public bool WineCompatibility { get; internal set; } =
    GetBoolEnv("TRACKER_WINE_COMPAT", false) || IsRunningUnderWine();

  /// <summary>
  /// Controls whether to render a custom title bar and window controls.
  /// </summary>
  public bool UseCustomTitleBar { get; internal set; } = true;

  /// <summary>
  /// Additional Chromium browser arguments passed to the WebView2 runtime.
  /// </summary>
  /// <remarks>
  /// Inherits <c>WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS</c> and, under Wine
  /// compatibility mode, appends software-rendering flags to avoid a blank
  /// (grey) WebView2 surface.
  /// </remarks>
  public string WebView2BrowserArguments
  {
    get => _webView2BrowserArguments
      ?? BuildWebView2BrowserArguments(WineCompatibility);
    internal set => _webView2BrowserArguments = value;
  }
  private string? _webView2BrowserArguments;

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
  /// When running from a versioned install directory (e.g. <c>&lt;installRoot&gt;\v0.0.0\</c>),
  /// this resolves to the parent <c>&lt;installRoot&gt;\</c> so that data is shared across
  /// versions and is independent of which install root the user selected during setup.
  /// Falls back to <c>%LocalAppData%\Videre Tracker</c> otherwise.
  /// </remarks>
  public string UserDataFolder { get; internal set; } = ResolveUserDataFolder();

  private static string ResolveUserDataFolder()
  {
    // If running from a versioned install subdirectory (e.g. <installRoot>\v0.0.0\),
    // use the parent as UserDataFolder so it matches the root the user chose at install time.
    var baseDir = AppContext.BaseDirectory
      .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    var dirName = Path.GetFileName(baseDir);

    if (!string.IsNullOrEmpty(dirName)
        && dirName.Length > 1
        && dirName[0] is 'v' or 'V'
        && char.IsDigit(dirName[1]))
    {
      var parentDir = Path.GetDirectoryName(baseDir);
      if (!string.IsNullOrEmpty(parentDir))
      {
        return parentDir;
      }
    }

    // Default: %LocalAppData%\Videre Tracker
    var defaultPath = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      typeof(Program).Assembly.GetName().Name!);

    return defaultPath;
  }

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

  /// <summary>
  /// The base URL for the Videre public API.
  /// </summary>
  /// <remarks>
  /// Defaults to the production endpoint. Can be overridden via VIDERE_API_URL environment variable.
  /// </remarks>
  public string VidereAPIUrl { get; internal set; } =
    Environment.GetEnvironmentVariable("VIDERE_API_URL")
      ?? "https://api.videreproject.com";

  /// <summary>
  /// Parses a boolean environment variable, returning <paramref name="defaultValue"/>
  /// when unset or unparseable.
  /// </summary>
  private static bool GetBoolEnv(string name, bool defaultValue)
  {
    var raw = Environment.GetEnvironmentVariable(name)?.Trim();
    if (bool.TryParse(raw, out var value))
    {
      return value;
    }

    return raw switch
    {
      "1" or "yes" or "on" => true,
      "0" or "no" or "off" => false,
      _ => defaultValue,
    };
  }

  /// <summary>
  /// Builds the WebView2 browser arguments, appending software-rendering flags
  /// when running under Wine compatibility mode.
  /// </summary>
  private static string BuildWebView2BrowserArguments(bool wineCompat)
  {
    var args = Environment.GetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS")
      ?? string.Empty;

    if (!wineCompat)
    {
      return args.Trim();
    }

    var extra = new[]
    {
      // WebView2's sandbox initialization is not compatible with Wine. Without
      // this flag the renderer can start but never present a painted surface.
      "--no-sandbox",
      "--disable-dev-shm-usage",
      // The API uses the local development certificate generated for the
      // container. WebView2 does not inherit Wine's host trust store.
      "--allow-insecure-localhost",
      "--ignore-certificate-errors",
    };

    var existing = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    return string.Join(" ", existing.Concat(extra)).Trim();
  }

  /// <summary>
  /// Detects whether the process is running under Wine by probing for the
  /// <c>wine_get_version</c> export in <c>ntdll</c>.
  /// </summary>
  private static bool IsRunningUnderWine()
  {
    try
    {
      return GetProcAddress(GetModuleHandle("ntdll.dll"), "wine_get_version")
        != IntPtr.Zero;
    }
    catch
    {
      return false;
    }
  }

  [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleA")]
  private static extern IntPtr GetModuleHandle(string moduleName);

  [DllImport("kernel32.dll", CharSet = CharSet.Ansi, EntryPoint = "GetProcAddress")]
  private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
}
