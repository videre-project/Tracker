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
      IntPtr stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
      SafeFileHandle safeFileHandle = new(stdHandle, true);
      FileStream fileStream = new(safeFileHandle, FileAccess.Write);
      Encoding encoding = Encoding.UTF8;
      StreamWriter standardOutput = new(fileStream, encoding) { AutoFlush = true };
      Console.SetOut(standardOutput);
      SetConsoleOutputCP(65001); // Set console to UTF-8

      // Enable ANSI support
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

    var options = new ApplicationOptions(args);
    Theme.Initialize(options);

    Application.SetHighDpiMode(HighDpiMode.SystemAware);
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);

    // Configure the HostForm and the WebView2 control.
    var hostForm = new HostForm(options) { Source = options.Url };
    hostForm.ControllerThread.Name ??= "UI Thread";

    // Configure the Web API service.
    var builder = WebAPIService.CreateHostBuilder(options);
    {
      builder.Services.Configure<HostOptions>(options =>
      {
        options.ShutdownTimeout = TimeSpan.FromSeconds(5);
        options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
      });
    }
    builder.UseConsole(hostForm); // Only logging after this point is redirected
    builder.UseDatabase<EventContext>(options);
    builder.RegisterClientSingleton();
    builder.RegisterGameService();

    // Create a new thread to run the ASP.NET Core Web API.
    var api = builder.Build();
    api.UseClientMiddleware();
    api.CreateAPIService();
    var apiThread = new Thread(() =>
    {
      Log.Debug("Starting the API thread.");
      api.OnShutdown(Application.Exit).Run();
    })
    {
      Name = "API Thread",
      IsBackground = true,
    };
    apiThread.Start();

    // Start the application.
    Log.Trace("Starting the application.");
    Application.Run(hostForm);
  }
}
