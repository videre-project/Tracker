/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32.SafeHandles;

using MTGOSDK.Core.Logging;

using Tracker.Database;
using Tracker.Services;
using Tracker.Services.MTGO;
using Tracker.WebView;


namespace Tracker;

public class Program
{
  #region P/Invoke Console Spawning

  [DllImport("kernel32.dll",
    EntryPoint = "GetStdHandle",
    SetLastError = true,
    CharSet = CharSet.Auto,
    CallingConvention = CallingConvention.StdCall)]
  private static extern IntPtr GetStdHandle(int nStdHandle);

  [DllImport("kernel32.dll",
    EntryPoint = "AllocConsole",
    SetLastError = true,
    CharSet = CharSet.Auto,
    CallingConvention = CallingConvention.StdCall)]
  [return: MarshalAs(UnmanagedType.Bool)]
  private static extern bool AllocConsole();

  [DllImport("kernel32.dll")]
  private static extern bool SetConsoleOutputCP(uint wCodePageID);

  [DllImport("kernel32.dll")]
  private static extern bool GetConsoleMode(
    IntPtr hConsoleHandle,
    out uint lpMode
  );

  [DllImport("kernel32.dll")]
  private static extern bool SetConsoleMode(
    IntPtr hConsoleHandle,
    uint dwMode
  );

  private const int STD_OUTPUT_HANDLE = -11;
  private const uint ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

  #endregion

  public static void RedirectConsole()
  {
    if (!Debugger.IsAttached && AllocConsole())
    {
      // Redirect the standard output to the console.
      IntPtr stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
      SafeFileHandle safeFileHandle = new(stdHandle, true);
      FileStream fileStream = new(safeFileHandle, FileAccess.Write);
      StreamWriter standardOutput = new(fileStream, new UTF8Encoding(false))
      {
        AutoFlush = true
      };
      Console.SetOut(standardOutput);
      SetConsoleOutputCP(65001); // Set console to UTF-8

      // Enable ANSI support by overriding the console mode.
      IntPtr handle = GetStdHandle(STD_OUTPUT_HANDLE);
      if (GetConsoleMode(handle, out uint mode))
      {
        SetConsoleMode(handle, mode | ENABLE_VIRTUAL_TERMINAL_PROCESSING);
      }
    }
  }

  /// <summary>
  /// The main entry point for the application.
  /// </summary>
  [STAThread]
  public static void Main(string[] args)
  {
#if DEBUG
    RedirectConsole(); // Ensure console output is redirected.
#endif

    // Optimize thread pool for bursty workloads
    ThreadPool.SetMinThreads(32, 32);

    var options = new ApplicationOptions(args)
    {
      // Disable UI if the application is loading this assembly via reflection.
      DisableUI = Application.ProductName != ProductInfo.Name,
    };
    Theme.Initialize(options);

    Application.SetHighDpiMode(HighDpiMode.SystemAware);
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);

    // Configure the HostForm and the WebView2 control.
    HostForm hostForm = null!;
    if (!options.DisableUI)
    {
      hostForm = new HostForm(options) { Source = options.UiUrl };
      hostForm.ControllerThread.Name ??= "UI Thread";
    }
    else
    {
      Log.Information("UI is disabled. Running in headless mode.");
#if DEBUG
      AppDomain.CurrentDomain.UnhandledException += HostForm.Error_MessageBox;
#endif
    }

    // Create a new thread to run the ASP.NET Core Web API.
    var apiThread = new Thread(() =>
    {
      // Configure the Web API service.
      var builder = WebAPIService.CreateHostBuilder(options);
      {
        builder.Services.Configure<HostOptions>(options =>
        {
          options.ServicesStartConcurrently = true;
          options.ServicesStopConcurrently = true;
          options.ShutdownTimeout = TimeSpan.FromSeconds(5);
          options.BackgroundServiceExceptionBehavior =
              BackgroundServiceExceptionBehavior.Ignore;
        });
      }

      // Redirect logging to the WebView2 console if the UI is enabled.
      if (!options.DisableUI)
      {
        builder.UseConsole(hostForm);
      }

      // Configure API services and database context.
      builder.UseDatabase<EventContext>(options);
      builder.RegisterClientAPIProvider();
      builder.RegisterGameService();

      // Configure the Web API middleware.
      var api = builder.Build();
      api.UseClientMiddleware();
      api.CreateAPIService(options);

      Log.Debug("Starting the API thread.");
      api.OnShutdown(Application.Exit).Run();
    })
    {
      Name = "API Thread",
      IsBackground = true,
    };
    apiThread.Start();

    // Start the application.
    if (!options.DisableUI && hostForm != null)
    {
      Log.Trace("Starting the application with UI.");
      Application.Run(hostForm);
    }
    else
    {
      Log.Trace("Starting the application without UI.");
      apiThread.Join();
    }
  }
}
