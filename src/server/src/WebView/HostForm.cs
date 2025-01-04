/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable WFO1000 // .NET 9: Disable code serialization warnings.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Microsoft.Web.WebView2.WinForms;

using MTGOSDK.Core.Logging;

using Tracker.Services;
using Tracker.WebView.Extensions;
using Tracker.WebView.Components;


namespace Tracker.WebView;

public partial class HostForm : Form
{
  /// <summary>
  /// The WebView2 control used to host the web content.
  /// </summary>
  public WebView2 WebView => this.webView21;

  /// <summary>
  /// The source of the web content to display.
  /// </summary>
  public Uri Source { get => WebView.Source; set => WebView.Source = value; }

  /// <summary>
  /// The thread that controls the WebView2 control.
  /// </summary>
  public Thread ControllerThread { get; private set; } = Thread.CurrentThread;

  public HostForm(ApplicationOptions options)
  {
    AppDomain.CurrentDomain.UnhandledException += Error_MessageBox;
    LoggerBase.SetProviderInstance(this.RegisterProvider());

    InitializeComponent();

    // Initialize the WebView2 environment.
    WebView.CreateEnvironment(options.UserDataFolder);
    WebView.CoreWebView2InitializationCompleted += (sender, e) =>
    {
      WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
      WebView.CoreWebView2.Settings.IsPinchZoomEnabled = false;
      WebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
      WebView.CoreWebView2.Settings.IsSwipeNavigationEnabled = false;
      WebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
      WebView.CoreWebView2.Settings.UserAgent = "Windows/VidereTracker";
      Log.Information("Initialized WebView2 environment.");
    };

    WebView.NavigationCompleted += (sender, e) =>
    {
      // Check if the current environment is development.
      if (options.IsDevelopment)
      {
        WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
        WebView.NavigationCompleted += (s, e) =>
        {
          HostForm_Show(sender, e);
          WebView.CoreWebView2.OpenDevToolsWindow();
        };
      }
      else
      {
        HostForm_Show(sender, e);
      }
    };
  }

  private readonly SemaphoreSlim _semaphore = new(1, 1);
  private readonly ConcurrentQueue<Func<Task>> _commandQueue = new();

  /// <summary>
  /// Executes a script in the WebView2 control.
  /// </summary>
  /// <param name="script">The JavaScript to execute.</param>
  /// <returns>The result of the script execution.</returns>
  public async Task<string> Exec(string script)
  {
    async Task<string> runCommand()
    {
      if (WebView.InvokeRequired)
      {
        return await WebView.Invoke(new Func<Task<string>>(runCommand));
      }
      else
      {
        return await WebView.ExecuteScriptAsync(script);
      }
    }

    await _semaphore.WaitAsync();
    try
    {
      // Wait until the WebView2 control is ready before processing commands.
      if (!AllowShowDisplay)
      {
        _commandQueue.Enqueue(runCommand);
        return await Task.FromResult(string.Empty);
      }
      else if (!_commandQueue.IsEmpty)
      {
        while (_commandQueue.TryDequeue(out var command))
        {
          await command.Invoke();
        }
      }
    }
    finally
    {
      _ = _semaphore.Release();
    }

    return await runCommand();
  }

  //
  // WinForms Form Visibility - Hide the form until the WebView has loaded.
  //

  /// <summary>
  /// Determines whether the Form is allowed to be displayed.
  /// </summary>
  public bool AllowShowDisplay { get; private set; } = false;

  /// <summary>
  /// Determines whether the Form is ready to be displayed.
  /// </summary>
  public EventHandler? OnReady;

  /// <summary>
  /// Reveals the Form when the WebView has loaded.
  /// </summary>
  private void HostForm_Show(object? sender, EventArgs e)
  {
    if (!this.Visible)
    {
      AllowShowDisplay = true;
      this.Visible |= true;

      // Notify any listeners that the HostForm is ready.
      OnReady?.Invoke(this, EventArgs.Empty);

      WebView.NavigationCompleted -= HostForm_Show;
      Log.Debug("Finished initializing HostForm.");
    }
  }

  /// <summary>
  /// Controls whether the Form is visible.
  /// </summary>
  protected override void SetVisibleCore(bool value)
  {
    base.SetVisibleCore(AllowShowDisplay ? value : AllowShowDisplay);
  }

  private static void Error_MessageBox(object sender, UnhandledExceptionEventArgs e)
  {
    var cts = new CancellationTokenSource();
    ThreadPool.QueueUserWorkItem(delegate
    {
      if (e.ExceptionObject is Exception ex && e.IsTerminating)
      {
        string label = "An unhandled exception occurred in the application.";
        using var errorWindow = new ErrorWindow(ex, label);
        errorWindow.ShowDialog();
      }
      cts.Cancel();
    });
    cts.Token.WaitHandle.WaitOne();
  }
}
