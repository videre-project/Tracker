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
  /// <param name="additionalBrowserArguments">Additional Chromium arguments.</param>
  public static void CreateEnvironment(this WebView2 webView,
    string userDataFolder = default!,
    string additionalBrowserArguments = default!)
  {
    var envOptions = CoreWebView2Environment.CreateAsync(
      browserExecutableFolder: null, // Use the installed WebView2 version.
      userDataFolder,
      new CoreWebView2EnvironmentOptions
      {
        AdditionalBrowserArguments = additionalBrowserArguments
      }
    );
    webView.EnsureCoreWebView2Async(envOptions.GetAwaiter().GetResult());
  }
}
