/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

#pragma warning disable CS8602, CA2254

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

using Newtonsoft.Json;


namespace Tracker.WebView.Extensions;

public static class DevToolsExtensions
{
  /// <summary>
  /// Registers log events from the WebView2 DevTools to the given logger.
  /// </summary>
  public static async Task RegisterLogger(this HostForm hostForm, ILogger logger)
  {
    await hostForm.CDPMethod("Runtime.enable", "{}");
    hostForm.RegisterConsoleAPI(logger);
    await hostForm.CDPMethod("Log.enable", "{}");
    hostForm.RegisterLogAPI(logger);
  }

  //
  // CoreWebView2 DevTools Protocol (CDP) Extensions
  //

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
  private static CoreWebView2DevToolsProtocolEventReceiver DevToolsEvent(
      this HostForm hostForm,
      string eventName) =>
    hostForm.WebView.CoreWebView2.GetDevToolsProtocolEventReceiver(eventName);

  /// <summary>
  /// Registers the <c>Runtime.consoleAPICalled</c> event to the given logger.
  /// </summary>
  private static void RegisterConsoleAPI(this HostForm hostForm, ILogger logger)
  {
    var eventReceiver = hostForm.DevToolsEvent("Runtime.consoleAPICalled");
    eventReceiver.DevToolsProtocolEventReceived += (s, e) =>
    {
      //
      // Extract the log entry from the event argument schema:
      // https://chromedevtools.github.io/devtools-protocol/tot/Runtime/#event-consoleAPICalled
      //
      var obj = JsonConvert.DeserializeObject<dynamic>(e.ParameterObjectAsJson);
      string message = obj["args"][0]["value"];

      var level = obj["type"];
      EventId eventId = new((int)obj["executionContextId"], "consoleAPICalled");
      switch(level)
      {
        case "log":
        case "debug":
        case "info":
          logger.LogInformation(message);
          break;
        case "error":
          logger.LogError(message);
          break;
        case "warning":
          logger.LogWarning(message);
          break;
        default:
          logger.LogDebug(message);
          break;
      }
    };
  }

  /// <summary>
  /// Registers the <c>Log.entryAdded</c> event to the given logger.
  /// </summary>
  private static void RegisterLogAPI(this HostForm hostForm, ILogger logger)
  {
    var eventReceiver = hostForm.DevToolsEvent("Log.entryAdded");
    eventReceiver.DevToolsProtocolEventReceived += (s, e) =>
    {
      //
      // Extract the log entry from the event argument schema:
      // https://chromedevtools.github.io/devtools-protocol/tot/Log/#type-LogEntry
      //
      var obj = JsonConvert.DeserializeObject<dynamic>(e.ParameterObjectAsJson);
      string source = obj["entry"]["source"];
      string level = obj["entry"]["level"];
      string message = $"[WebView2.{source}] {obj["entry"]["text"]}";

      string? url = obj["entry"]["url"];
      if (url is not null)
        message += $" (url: {url})";

      string? lineNumber = obj["entry"]["lineNumber"];
      if (lineNumber is not null)
        message += $" (line: {lineNumber})";

      EventId eventId = new EventId(0, "LogEntry");
      switch(level)
      {
        case "verbose":
        case "info":
          logger.LogInformation(eventId, message);
          break;
        case "warning":
          logger.LogWarning(eventId, message);
          break;
        case "error":
          logger.LogError(eventId, message);
          break;
        default:
          logger.LogDebug(eventId, message);
          break;
      }
    };
  }
}
