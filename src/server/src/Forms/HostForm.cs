/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Net.Http.Headers;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;


namespace Tracker.Forms;

public partial class HostForm : Form
{
  /// <summary>
  /// Configures and initializes the WebView2 host.
  /// </summary>
  /// <returns>A new <see cref="HostForm"/> instance.</returns>
  public static HostForm CreateWebView2Host()
  {
    // Configure the WebView2 environment prior to creating any controls.
    var options = CoreWebView2Environment.CreateAsync(
      browserExecutableFolder: null, // Use the installed WebView2 version.
      userDataFolder: Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserData"),
      options: new CoreWebView2EnvironmentOptions()
    );

    return new HostForm(options.GetAwaiter().GetResult());
  }

  /// <summary>
  /// The WebView2 control used to host the web content.
  /// </summary>
  public WebView2 WebView => this.webView21;

  public HostForm(CoreWebView2Environment? webView2Env = null)
  {
    InitializeComponent();
    WebView.CoreWebView2InitializationCompleted += WebView_CoreWebView2InitializationCompleted;
    WebView.NavigationCompleted += WebView_NavigationCompleted;
    WebView.EnsureCoreWebView2Async(webView2Env);
  }

  private void WebView_CoreWebView2InitializationCompleted(
    object? sender,
    CoreWebView2InitializationCompletedEventArgs e)
  {
    WebView.Source = new Uri("https://localhost:7183/", UriKind.Absolute);
  }

  private void WebView_NavigationCompleted(
    object? sender,
    CoreWebView2NavigationCompletedEventArgs e)
  {
    if (!this.Visible)
    {
      AllowShowDisplay = true;
      this.Visible = true;
    }
  }

  //
  // WinForms Form Visibility - Hide the form until the WebView has loaded.
  //

  /// <summary>
  /// Determines whether the Form is allowed to be displayed.
  /// </summary>
  public bool AllowShowDisplay { get; private set; }

  /// <summary>
  /// Controls whether the Form is visible.
  /// </summary>
  protected override void SetVisibleCore(bool value)
  {            
    base.SetVisibleCore(AllowShowDisplay ? value : AllowShowDisplay);
  }
}
