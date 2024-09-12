/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;


namespace Tracker.WebView.Extensions;

/// <summary>
/// WebView2 Console API Extensions
/// </summary>
/// <remarks>
/// For more information, refer to the MDN documentation:
/// <br/>
/// https://developer.mozilla.org/en-US/docs/Web/API/Console_API
/// </remarks>
public static class ConsoleAPIExtensions
{
  /// <summary>
  /// Registers log events from the WebView2 DevTools to the given logger.
  /// </summary>
  /// <param name="hostForm">The <see cref="HostForm"/> instance.</param>
  /// <param name="logger">The <see cref="ILogger"/> instance.</param>
  public static async Task RegisterLogger(this HostForm hostForm, ILogger logger)
  {
    await hostForm.CDPMethod("Runtime.enable", "{}");
    hostForm.RegisterConsoleAPI(logger);
    await hostForm.CDPMethod("Log.enable", "{}");
    hostForm.RegisterLogAPI(logger);
  }

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
      var obj = JsonSerializer.Deserialize<dynamic>(e.ParameterObjectAsJson);
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
      var obj = JsonSerializer.Deserialize<dynamic>(e.ParameterObjectAsJson);
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
        case "info":
          logger.LogInformation(eventId, message);
          break;
        case "warning":
          logger.LogWarning(eventId, message);
          break;
        case "error":
          logger.LogError(eventId, message);
          break;
        case "verbose":
        default:
          logger.LogDebug(eventId, message);
          break;
      }
    };
  }
}
