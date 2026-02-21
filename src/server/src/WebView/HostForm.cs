/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable WFO1000 // .NET 9: Disable code serialization warnings.

using System;
using System.IO;
using System.Reflection;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Microsoft.Extensions.Logging;

using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Core;

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
  private bool _hasUpdatedTitleBarColor;
  private SplashForm? _splashForm;

  public HostForm(ApplicationOptions options)
  {
    AppDomain.CurrentDomain.UnhandledException += Error_MessageBox;
    LoggerBase.SetProviderInstance(this.RegisterProvider());

    // Enable double buffering to reduce flicker
    this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);

    InitializeComponent();

    // Set the application icon
    try
    {
      this.Icon = Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
    }
    catch { /* Ignore icon load failure */ }

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

      // Position WebView2 to account for custom title bar (use EffectiveCaptionHeight)
      int titleBarHeight = _dwmTitleBar?.EffectiveCaptionHeight ?? SystemInformation.CaptionHeight;
      
      this.webView21.SetBounds(
          0, 
          titleBarHeight,
          this.ClientSize.Width,
          this.ClientSize.Height - titleBarHeight);

      this.SizeChanged += (s, e) => {
        int titleBarHeight = _dwmTitleBar?.EffectiveCaptionHeight ?? SystemInformation.CaptionHeight;

        this.webView21.SetBounds(
            0, 
            titleBarHeight,
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

      // Allow insecure content and localhost without certificates
      WebView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false;
      WebView.CoreWebView2.PermissionRequested += (s, args) =>
          args.State = CoreWebView2PermissionState.Allow;

      // Handle certificate errors to allow localhost without valid certificates
      WebView.CoreWebView2.ServerCertificateErrorDetected += (s, args) =>
      {
        // Allow localhost and 127.0.0.1 without valid certificates
        if (args.RequestUri.Contains("localhost") ||
            args.RequestUri.Contains("127.0.0.1"))
        {
          args.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
        }
      };

      Log.Information("Initialized WebView2 environment.");
    };

    WebView.NavigationCompleted += OnNavigationCompleted;

    // Initialize Splash Screen
    InitializeSplashScreen();
  }

  private async void OnNavigationCompleted(
    object? sender,
    CoreWebView2NavigationCompletedEventArgs e)
  {
    var title = WebView.CoreWebView2.DocumentTitle;
    if (string.IsNullOrEmpty(title)) return;

    // Check if there is a main-frame-error element, indicating an error.
    try
    {
      if (title == "localhost" || title.Contains("Error"))
      {
        var errorCheck = await WebView.CoreWebView2.ExecuteScriptAsync("document.getElementById('main-frame-error') != null");
        if (errorCheck == "true")
        {
          Log.Debug("Connection failed, retrying...");
          await Task.Delay(1000);
          WebView.Reload();
          return;
        }
      }
    }
    catch (Exception ex)
    {
      Log.Error(ex, "Failed to check for main-frame-error element.");
    }

    if (title == "SPA proxy launch page")
    {
      Log.Debug("Waiting for SPA proxy to launch...");
      return;
    }

    WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
    HostForm_Show(sender, e);
    
    WebView.NavigationCompleted -= OnNavigationCompleted;

    // Update titlebar color when page is ready
    _ = UpdateTitleBarColorFromPage(0.5f); // 50% darker than the sidebar
  }

  private void InitializeSplashScreen()
  {
    // Hide HostForm initially
    AllowShowDisplay = false;

    // Set default application size immediately since we aren't morphing
    this.MinimumSize = new Size(1550, 925);
    this.ClientSize = new Size(1550, 925);
    this.CenterToScreen();
    
    // Create and show splash form
    _splashForm = new SplashForm();
    _splashForm.Show();
  }

  /// <summary>
  /// Updates the status text on the splash screen.
  /// </summary>
  /// <summary>
  /// Updates the status text on the splash screen.
  /// </summary>
  public void UpdateSplashStatus(string message, LogLevel level = LogLevel.Information, string? timestamp = null, string? header = null, string? label = null)
  {
    if (_splashForm != null && !_splashForm.IsDisposed)
    {
      _splashForm.UpdateStatus(message, level, timestamp, header, label);
    }
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

  /// <summary>
  /// Updates the titlebar color based on the CSS variables from the loaded page.
  /// </summary>
  /// <param name="darkenPercentage">Percentage to darken the background color (0.0 = no change, 1.0 = completely black)</param>
  private async Task UpdateTitleBarColorFromPage(float darkenPercentage = 0.0f)
  {
    if (!Theme.UseCustomTitleBar || _dwmTitleBar == null || _hasUpdatedTitleBarColor)
      return;

    try
    {
      // Wait a bit for the page to fully render and CSS to be applied
      await Task.Delay(500);

      // Extract the sidebar background color from CSS
      var script = @"
        (function() {
          const root = document.documentElement;
          const computedStyle = window.getComputedStyle(root);
          const sidebarBg = computedStyle.getPropertyValue('--sidebar-background').trim();
          const foreground = computedStyle.getPropertyValue('--foreground').trim();

          // Convert HSL to RGB
          function hslToRgb(hslString) {
            const values = hslString.split(' ').map(v => parseFloat(v.replace('%', '')));
            const [h, s, l] = values;

            const c = (1 - Math.abs(2 * l / 100 - 1)) * s / 100;
            const x = c * (1 - Math.abs((h / 60) % 2 - 1));
            const m = l / 100 - c / 2;

            let r, g, b;
            if (h >= 0 && h < 60) [r, g, b] = [c, x, 0];
            else if (h >= 60 && h < 120) [r, g, b] = [x, c, 0];
            else if (h >= 120 && h < 180) [r, g, b] = [0, c, x];
            else if (h >= 180 && h < 240) [r, g, b] = [0, x, c];
            else if (h >= 240 && h < 300) [r, g, b] = [x, 0, c];
            else [r, g, b] = [c, 0, x];

            r = Math.round((r + m) * 255);
            g = Math.round((g + m) * 255);
            b = Math.round((b + m) * 255);

            return [r, g, b];
          }

          if (sidebarBg && foreground) {
            const bgRgb = hslToRgb(sidebarBg);
            const fgRgb = hslToRgb(foreground);

            return {
              background: bgRgb,
              foreground: fgRgb,
              isDark: document.documentElement.classList.contains('dark')
            };
          }

          return null;
        })();
      ";

      var result = await Exec(script);

      if (!string.IsNullOrEmpty(result) && result != "null")
      {
        // Parse the JSON result
        var colorData = System.Text.Json.JsonSerializer.Deserialize<ColorData>(result);

        if (colorData?.background != null && colorData.foreground != null)
        {
          // Darken the background color if specified
          var originalBgColor = Color.FromArgb(colorData.background[0], colorData.background[1], colorData.background[2]);
          var fgColor = Color.FromArgb(colorData.foreground[0], colorData.foreground[1], colorData.foreground[2]);

          _dwmTitleBar.UpdateColors(originalBgColor, fgColor);
          _hasUpdatedTitleBarColor = true;

          // Force repaint
          this.Invalidate();
        }
      }
    }
    catch (Exception ex)
    {
      Log.Warning($"Failed to update titlebar color: {ex.Message}");
    }
  }

  /// <summary>
  /// Darkens a color by the specified percentage.
  /// </summary>
  /// <param name="color">The original color</param>
  /// <param name="percentage">Darkening percentage (0.0 = no change, 1.0 = black)</param>
  /// <returns>The darkened color</returns>
  private static Color DarkenColor(Color color, float percentage)
  {
    // Clamp percentage between 0 and 1
    percentage = Math.Max(0.0f, Math.Min(1.0f, percentage));

    // Calculate the darkened RGB values
    var r = (int)(color.R * (1.0f - percentage));
    var g = (int)(color.G * (1.0f - percentage));
    var b = (int)(color.B * (1.0f - percentage));

    return Color.FromArgb(r, g, b);
  }

  private class ColorData
  {
    public int[]? background { get; set; }
    public int[]? foreground { get; set; }
    public bool isDark { get; set; }
  }

  //
  // WinForms Form Visibility - Hide the form until the WebView has loaded.
  //

  /// <summary>
  /// Determines whether the Form is allowed to be displayed.
  /// </summary>
  public bool AllowShowDisplay { get; private set; } = true;

  /// <summary>
  /// Determines whether the Form is ready to be displayed.
  /// </summary>
  public EventHandler? OnReady;

  /// <summary>
  /// Reveals the Form when the WebView has loaded.
  /// </summary>
  private void HostForm_Show(object? sender, EventArgs e)
  {
    if (_splashForm != null)
    {
      _splashForm.Close();
      _splashForm.Dispose();
      _splashForm = null;
    }

    AllowShowDisplay = true;
    this.Visible = true;
    this.BringToFront();
    this.TopMost = true;
    this.TopMost = false;
    this.Activate();
    this.Focus();

    // Notify any listeners that the HostForm is ready.
    OnReady?.Invoke(this, EventArgs.Empty);

    Log.Debug("Finished initializing HostForm.");
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

  public static void Error_MessageBox(object sender, UnhandledExceptionEventArgs e)
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
    Log.Error(label, ex);
    using var errorWindow = new ErrorWindow(ex, label);
    errorWindow.ShowDialog();
    errorWindow.BringToFront();
  }
}
