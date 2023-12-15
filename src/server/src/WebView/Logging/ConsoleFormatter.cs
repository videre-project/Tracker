/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using Microsoft.Extensions.Logging;


namespace Tracker.WebView.Logging;

public class ConsoleFormatter
{
  public string FormatArgs(params string[] args) =>
    $"'{string.Join("', '", args)}'";

  public string FormatCSS(string message, params string[] styles) =>
    $"%c{message}', '{string.Join(";", styles)}', '";

  public string GetConsoleMethod(LogLevel logLevel) =>
    logLevel switch
    {
      LogLevel.None         => "console.log",
      LogLevel.Trace        => "console.log",
      LogLevel.Debug        => "console.debug",
      LogLevel.Information  => "console.info",
      LogLevel.Warning      => "console.warn",
      LogLevel.Error        => "console.error",
      LogLevel.Critical     => "console.error",
      _ =>
        throw new ArgumentOutOfRangeException(nameof(logLevel), logLevel, null),
    };

  /// <summary>
  /// Anonymizes the console method call's source context.
  /// </summary>
  /// <param name="logLevel">The log level.</param>
  /// <param name="args">The arguments to pass to the console method.</param>
  /// <returns>A string representing the console method call.</returns>
  public string DeRefConsole(LogLevel logLevel, string args) =>
    $"setTimeout({GetConsoleMethod(logLevel)}.bind(console, {args}))";
}
