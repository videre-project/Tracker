/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;
using System.Windows.Forms;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

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
    Application.SetHighDpiMode(HighDpiMode.SystemAware);
    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);

    var options = new ApplicationOptions(args);
    var builder = WebAPIService.CreateHostBuilder(options);
    var hostForm = new HostForm(options)
    {
      Source = new Uri(builder.Configuration[WebHostDefaults.ServerUrlsKey]!),
    };

    // Label the controller thread for logging purposes.
    hostForm.ControllerThread.Name ??= "UI Thread";

    // Create a new thread to run the ASP.NET Core Web API.
    WebApplication api = builder.UseConsole(hostForm).CreateAPIService();
    var apiThread = new Thread(() => api.OnShutdown(Application.Exit).Run())
    {
      Name = "API Thread",
      IsBackground = true
    };
    apiThread.Start();

    // Start the WebView2 application.
    Application.Run(hostForm);
  }
}
