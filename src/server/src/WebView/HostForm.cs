/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable WFO1000 // .NET 9: Disable code serialization warnings.

using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Microsoft.Web.WebView2.WinForms;

using MTGOSDK.Core.Logging;

using Tracker.Services;
using Tracker.WebView.Components;
using Tracker.WebView.Extensions;


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

  private readonly DwmTitleBar? _dwmTitleBar;
  private readonly InvisibleResizePanel[] _resizePanels;

  public HostForm(ApplicationOptions options)
  {
    AppDomain.CurrentDomain.UnhandledException += Error_MessageBox;
    LoggerBase.SetProviderInstance(this.RegisterProvider());

    // Enable double buffering to reduce flicker
    this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);

    InitializeComponent();

    // Initialize resize panels for custom title bar mode
    _resizePanels = new InvisibleResizePanel[8];

    if (Theme.UseCustomTitleBar)
    {
      // Additional optimization for custom title bar
      this.SetStyle(ControlStyles.OptimizedDoubleBuffer, true);

      this.FormBorderStyle = FormBorderStyle.None;
      _dwmTitleBar = new DwmTitleBar(this);

      // Create invisible resize panels for all edges and corners
      _resizePanels[0] = new InvisibleResizePanel(this, ResizeDirection.Left);
      _resizePanels[1] = new InvisibleResizePanel(this, ResizeDirection.Right);
      _resizePanels[2] = new InvisibleResizePanel(this, ResizeDirection.Top);
      _resizePanels[3] = new InvisibleResizePanel(this, ResizeDirection.Bottom);
      _resizePanels[4] = new InvisibleResizePanel(this, ResizeDirection.TopLeft);
      _resizePanels[5] = new InvisibleResizePanel(this, ResizeDirection.TopRight);
      _resizePanels[6] = new InvisibleResizePanel(this, ResizeDirection.BottomLeft);
      _resizePanels[7] = new InvisibleResizePanel(this, ResizeDirection.BottomRight);

      // Add resize panels to the form (they will be behind the WebView)
      foreach (var panel in _resizePanels)
      {
        this.Controls.Add(panel);
        panel.BringToFront();
      }

      // Position WebView2 to account for custom title bar
      this.webView21.SetBounds(0, SystemInformation.CaptionHeight,
          this.ClientSize.Width,
          this.ClientSize.Height - SystemInformation.CaptionHeight);
      this.SizeChanged += (s, e) => {
        var isMaximized = this.WindowState == FormWindowState.Maximized;
        var topPadding = isMaximized ? 8 : 0;  // 8px top padding when maximized
        var titleBarHeight = SystemInformation.CaptionHeight + topPadding;

        this.webView21.SetBounds(0, titleBarHeight,
            this.ClientSize.Width,
            this.ClientSize.Height - titleBarHeight);

        // Hide resize panels when maximized, show when normal
        var showResizePanels = this.WindowState != FormWindowState.Maximized;
        foreach (var panel in _resizePanels)
        {
          panel.Visible = showResizePanels;
        }
      };
    }
    else
    {
      // Use default Windows title bar
      this.FormBorderStyle = FormBorderStyle.Sizable;
      this.webView21.Dock = DockStyle.Fill;
    }

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
      WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
      HostForm_Show(sender, e);
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

  protected override void OnPaint(PaintEventArgs e)
  {
    base.OnPaint(e);

    if (Theme.UseCustomTitleBar && _dwmTitleBar != null)
    {
      _dwmTitleBar.PaintTitleBarInClientArea(e.Graphics);
    }
  }

  protected override void WndProc(ref Message m)
  {
    if (Theme.UseCustomTitleBar && _dwmTitleBar?.HandleMessage(ref m) == true)
    {
      // If the message was handled by our custom title bar, return.
      // This prevents the default window procedure from processing it.
      return;
    }
    base.WndProc(ref m);
  }

  protected override CreateParams CreateParams
  {
    get
    {
      var cp = base.CreateParams;
      if (Theme.UseCustomTitleBar)
      {
        // Add WS_THICKFRAME, WS_MINIMIZEBOX, WS_MAXIMIZEBOX, WS_SYSMENU for resizing/snap, but NOT WS_CAPTION
        const int WS_THICKFRAME = 0x00040000;
        const int WS_MINIMIZEBOX = 0x00020000;
        const int WS_MAXIMIZEBOX = 0x00010000;
        const int WS_SYSMENU = 0x00080000;
        cp.Style |= WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX | WS_SYSMENU;
        // Do NOT add WS_CAPTION (0x00C00000)
      }
      return cp;
    }
  }

  private static void Error_MessageBox(object sender, UnhandledExceptionEventArgs e)
  {
    var cts = new CancellationTokenSource();
    ThreadPool.QueueUserWorkItem(delegate
    {
      if (e.ExceptionObject is Exception ex && e.IsTerminating)
      {
        DisplayError(ex, $"{Application.ProductName} encountered an unexpected error.");
      }
      cts.Cancel();
    });
    cts.Token.WaitHandle.WaitOne();
  }

  public static void DisplayError(Exception ex, string label)
  {
    using var errorWindow = new ErrorWindow(ex, label);
    errorWindow.ShowDialog();
    errorWindow.BringToFront();
  }
}
