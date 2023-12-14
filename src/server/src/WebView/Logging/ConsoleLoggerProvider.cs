/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;


namespace Tracker.WebView.Logging;

public class ConsoleLoggerProvider(HostForm hostForm) : ILoggerProvider
{
  private readonly ConcurrentDictionary<string, ConsoleLogger> _loggers = new();

  public ILogger CreateLogger(string categoryName) =>
    _loggers.GetOrAdd(categoryName, name => new ConsoleLogger(name, hostForm));

  public void Dispose()
  {
    _loggers.Clear();
    GC.SuppressFinalize(this);
  }
}
