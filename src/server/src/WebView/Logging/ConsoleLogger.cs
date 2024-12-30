/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;

using Microsoft.Extensions.Logging;


namespace Tracker.WebView.Logging;

public class ConsoleLogger(string name, HostForm hostForm)
    : ConsoleFormatter, ILogger
{
  private static string GetCategoryColor(LogLevel logLevel)
  {
    string color = "#000000";

    if (logLevel == LogLevel.Information)
      color = "#089fa2"; // teal
    else if (logLevel == LogLevel.Warning)
      color = "#ff8c00"; // orange
    else if (logLevel == LogLevel.Error ||
        logLevel == LogLevel.Critical)
      color = "#ff0000"; // red

    return color;
  }

  public bool IsEnabled(LogLevel logLevel) => true;

#pragma warning disable CS8633
  public IDisposable BeginScope<TState>(TState state) => null!;
#pragma warning restore CS8633

  public void Log<TState>(
    LogLevel logLevel,
    EventId eventId,
    TState state,
    Exception? exception,
    Func<TState, Exception, string> formatter)
  {
    if (!IsEnabled(logLevel)) return;

    string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
    string header = $"[{Thread.CurrentThread.Name ?? "Unknown"}]";
    string label = $"{name}[{eventId.Id}]";
    string message = formatter(state, exception!)
      // Escape any slashes in the message
      .Replace("\\", "\\\\");

    string args = FormatArgs(
      $"%c{timestamp} %c{header} %c{label} \\n{message}",
      /* timestamp   */  "margin-bottom: 0.25em; color: #691569",
      /* header      */ $"color: {GetCategoryColor(logLevel)}",
      /* label + msg */  "color: #000000");

#pragma warning disable CS4014
    hostForm.Exec($"{DeRefConsole(logLevel, args)}");
#pragma warning restore CS4014
  }
}
