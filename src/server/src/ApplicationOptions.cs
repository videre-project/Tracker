/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;


namespace Tracker;

public class ApplicationOptions
{
  /// <summary>
  /// The command-line arguments.
  /// </summary>
  public string[] Args { get; internal set; } = Array.Empty<string>();

  /// <summary>
  /// The port number to use for the Web API.
  /// </summary>
  public int Port { get; internal set; } = 7183;

  /// <summary>
  /// The base directory path for the application's static content.
  /// </summary>
  /// <remarks>
  /// Defaults to the install directory executing the application.
  /// </remarks>
  public string ContentRootPath { get; internal set; } =
    AppDomain.CurrentDomain.BaseDirectory;

  /// <summary>
  /// The base directory path for user application data.
  /// </summary>
  /// <remarks>
  /// Defaults to <c>%appdata%\..\Local\Videre Tracker</c>
  /// </remarks>
  public string UserDataFolder { get; internal set; } =
    Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      typeof(Program).Assembly.GetName().Name!
    );
}
