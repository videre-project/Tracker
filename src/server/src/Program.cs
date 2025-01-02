/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;
using System.Windows.Forms;

using MTGOSDK.Core.Logging;

using Tracker.Database;
using Tracker.Services;
using Tracker.WebView;


namespace Tracker;

public class Program
{
  /// <summary>
  /// The main entry point for the application.
  /// </summary>
  [STAThread]
  public static void Main(string[] args)
  {
    var options = new ApplicationOptions(args);
    Application.SetHighDpiMode(HighDpiMode.SystemAware);
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);

    // Configure the HostForm and the WebView2 control.
    var hostForm = new HostForm(options) { Source = options.Url };
    hostForm.ControllerThread.Name ??= "UI Thread";
    hostForm.ControllerThread.Priority = ThreadPriority.AboveNormal;

    // Configure the Web API service.
    var builder = WebAPIService.CreateHostBuilder(options);
    builder.UseConsole(hostForm); // Only logging after this point is redirected
    builder.UseMTGOAPIClient();
    builder.UseDatabase<EventContext>(options);

    // Create a new thread to run the ASP.NET Core Web API.
    var api = builder.CreateAPIService();
    var apiThread = new Thread(() => api.OnShutdown(Application.Exit).Run())
    {
      Name = "API Thread",
      IsBackground = true,
      Priority = ThreadPriority.AboveNormal,
    };
    Log.Debug("Starting the API thread.");
    apiThread.Start();

    // Start the application.
    Log.Debug("Starting the application.");
    Application.Run(hostForm);
  }
}
