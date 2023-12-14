/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;


namespace Tracker.WebView.Extensions;

public static class WebViewExtensions
{
  /// <summary>
  /// Explicitly creates the WebView2 environment.
  /// </summary>
  /// <param name="webView">The WebView2 control.</param>
  /// <param name="options">The environment options.</param>
  public static void CreateEnvironment(this WebView2 webView,
    string userDataFolder = default!,
    CoreWebView2EnvironmentOptions options = default!)
  {
    var envOptions = CoreWebView2Environment.CreateAsync(
      browserExecutableFolder: null, // Use the installed WebView2 version.
      userDataFolder,
      options
    );
    webView.EnsureCoreWebView2Async(envOptions.GetAwaiter().GetResult());
  }
}
