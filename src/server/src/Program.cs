/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;
using System.Windows.Forms;

using Microsoft.AspNetCore.Hosting;

using MTGOSDK.Core.Logging;

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

    var builder = WebAPIService.CreateHostBuilder(options);
    var hostForm = new HostForm(options)
    {
      Source = new Uri(builder.Configuration[WebHostDefaults.ServerUrlsKey]!),
    };
    hostForm.ControllerThread.Name ??= "UI Thread";
    hostForm.ControllerThread.Priority = ThreadPriority.AboveNormal;

    // Redirect all logging to the WebView2 console.
    LoggerBase.SetProviderInstance(hostForm.RegisterProvider());
    builder.UseConsole(hostForm);

    builder.UseMTGOAPIClient();

    // Create a new thread to run the ASP.NET Core Web API.
    var apiThread = new Thread(() =>
    {
      Log.Debug("Starting the API thread.");
      var api = builder.CreateAPIService();
      api.OnShutdown(Application.Exit);
      api.Run();
    })
    {
      Name = "API Thread",
      IsBackground = true,
      Priority = ThreadPriority.AboveNormal,
    };
    apiThread.Start();

    // Start the WebView2 application.
    Log.Debug("Starting the application.");
    Application.Run(hostForm);
  }
}
