/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;


namespace Tracker.WebView.Extensions;

/// <summary>
/// Chrome DevTools Protocol (CDP) Extensions
/// </summary>
public static class DevToolsExtensions
{
  /// <summary>
  /// Executes a method from the WebView2 DevTools.
  /// </summary>
  /// <param name="method">The method to execute.</param>
  /// <param name="args">The arguments to pass to the method.</param>
  /// <returns>The result of the method execution.</returns>
  public static async Task<string> CDPMethod(
      this HostForm hostForm,
      string method,
      string args) =>
    await hostForm.WebView.CoreWebView2.CallDevToolsProtocolMethodAsync(method, args);

  /// <summary>
  /// Retrieves a DevTools Protocol event receiver by the given event name.
  /// </summary>
  public static CoreWebView2DevToolsProtocolEventReceiver DevToolsEvent(
      this HostForm hostForm,
      string eventName) =>
    hostForm.WebView.CoreWebView2.GetDevToolsProtocolEventReceiver(eventName);
}
