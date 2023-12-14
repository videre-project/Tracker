/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;
using Microsoft.Extensions.Logging;


namespace Tracker.WebView.Logging;

public class ConsoleLogger(string name, HostForm hostForm)
    : ConsoleFormatter, ILogger
{
	private static readonly object s_lock = new();

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

    lock (s_lock)
    {
      string header = FormatCSS($"[{Thread.CurrentThread.Name ?? "Unknown"}]",
        "margin-bottom: 0.25em",
        "color: #089fa2"
      );
      string label = $"{name}[{eventId.Id}]";
      string message = formatter(state, exception!);

      string args = FormatArgs(header, label, $"\\n{message}");
#pragma warning disable CS4014
      hostForm.Exec($"{GetConsoleMethod(logLevel)}({args})");
#pragma warning restore CS4014
    }
  }
}
