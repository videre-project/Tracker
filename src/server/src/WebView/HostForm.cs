/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
#pragma warning disable WFO1000 // .NET 9: Disable code serialization warnings.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

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

  public HostForm(ApplicationOptions options)
  {
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
    };
    WebView.NavigationCompleted += HostForm_Show;
  }

  /// <summary>
  /// Executes a script in the WebView2 control.
  /// </summary>
  /// <param name="script">The JavaScript to execute.</param>
  /// <returns>The result of the script execution.</returns>
  public async Task<string> Exec(string script) =>
    await this.Invoke(() => WebView.ExecuteScriptAsync(script));

  //
  // WinForms Form Visibility - Hide the form until the WebView has loaded.
  //

  /// <summary>
  /// Determines whether the Form is allowed to be displayed.
  /// </summary>
  public bool AllowShowDisplay { get; private set; } = false;

  /// <summary>
  /// Reveals the Form when the WebView has loaded.
  /// </summary>
  private void HostForm_Show(object? sender, EventArgs e)
  {
    if (!this.Visible)
    {
      AllowShowDisplay = true;
      this.Visible |= true;
    }
  }

  /// <summary>
  /// Controls whether the Form is visible.
  /// </summary>
  protected override void SetVisibleCore(bool value)
  {
    base.SetVisibleCore(AllowShowDisplay ? value : AllowShowDisplay);
  }
}
