/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Globalization;
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

  private static string ColorANSI(string message, string hexColor)
  {
    // Remove the '#' if it exists
    hexColor = hexColor.Replace("#", "");

    // Parse the hex color string
    int r = int.Parse(hexColor.Substring(0, 2), NumberStyles.HexNumber);
    int g = int.Parse(hexColor.Substring(2, 2), NumberStyles.HexNumber);
    int b = int.Parse(hexColor.Substring(4, 2), NumberStyles.HexNumber);

    // Convert RGB to ANSI 256 color
    int grayPossible = (r == g && g == b) ? 1 : 0;
    int colorCode;

    if (grayPossible == 1)
    {
      if (r < 8)
        colorCode = 16;
      else if (r > 248)
        colorCode = 231;
      else
        colorCode = (int)Math.Round(((double)(r - 8) / 247) * 24) + 232;
    }
    else
    {
      colorCode = 16
                + (36 * (int)Math.Round(r / 255.0 * 5))
                + (6 * (int)Math.Round(g / 255.0 * 5))
                + (int)Math.Round(b / 255.0 * 5);
    }

    return $"\u001b[38;5;{colorCode}m{message}\u001b[0m";
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

#if DEBUG
    Console.WriteLine(string.Format(
      CultureInfo.InvariantCulture,
      "{0} {1} {2}\n{3}",
      ColorANSI(timestamp, "#691569"),
      ColorANSI(header, GetCategoryColor(logLevel).Replace("000000", "ffffff")),
      label,
      message));
#endif
  }
}
