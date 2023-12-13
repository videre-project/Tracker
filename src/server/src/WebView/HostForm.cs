/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.Logging;
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
  /// The logger used to log events from the WebView2 DevTools.
  /// </summary>
  public ILogger Logger { get; set; } = null!;

  public HostForm()
  {
    InitializeComponent();

    // Register WebView event handlers.
    WebView.CoreWebView2InitializationCompleted += async (s, e) =>
    {
      await this.RegisterLogger(Logger);
    };
    WebView.NavigationCompleted += (s, e) =>
    {
      // Hides the form until the WebView has loaded.
      if (!this.Visible)
      {
        AllowShowDisplay = true;
        this.Visible |= true;
      }
    };

    // Initialize the WebView2 environment.
    WebView.CreateEnvironment();
  }

  /// <summary>
  /// Executes a script in the WebView2 control.
  /// </summary>
  /// <param name="script">The JavaScript to execute.</param>
  /// <returns>The result of the script execution.</returns>
  public async Task<string> Exec(string script) =>
    await WebView.ExecuteScriptAsync(script);

  //
  // WinForms Form Visibility - Hide the form until the WebView has loaded.
  //

  /// <summary>
  /// Determines whether the Form is allowed to be displayed.
  /// </summary>
  public bool AllowShowDisplay { get; private set; } = false;

  /// <summary>
  /// Controls whether the Form is visible.
  /// </summary>
  protected override void SetVisibleCore(bool value)
  {
    base.SetVisibleCore(AllowShowDisplay ? value : AllowShowDisplay);
  }
}
