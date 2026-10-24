/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using MTGOTracker.Controllers.Base;

namespace MTGOTracker.Controllers;

[ApiController]
[SettingsWrite]
[Route("api/settings")]
public sealed class SettingsController(IHostApplicationLifetime lifetime) : ControllerBase
{
  private static int s_restarting;

  public sealed record HostInstance(int ProcessId);

  [HttpGet("instance")]
  public HostInstance Instance() => new(Environment.ProcessId);

  [HttpPost("restart")]
  public IActionResult Restart()
  {
    if (Interlocked.Exchange(ref s_restarting, 1) != 0)
    {
      return Accepted();
    }
    try
    {
      string executable = Environment.ProcessPath
        ?? throw new InvalidOperationException("The current executable cannot be located.");
      var start = new ProcessStartInfo(executable)
      {
        UseShellExecute = false,
        WorkingDirectory = Environment.CurrentDirectory
      };
      bool dotnet = Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
      foreach (string argument in Environment.GetCommandLineArgs().Skip(dotnet ? 0 : 1)
        .Where(argument => !argument.StartsWith("--restart-after=", StringComparison.Ordinal)))
      {
        start.ArgumentList.Add(argument);
      }
      start.ArgumentList.Add($"--restart-after={Environment.ProcessId}");
      using var next = Process.Start(start)
        ?? throw new InvalidOperationException("Tracker could not be restarted.");
      Response.OnCompleted(() => { lifetime.StopApplication(); return Task.CompletedTask; });
      return Accepted();
    }
    catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
    {
      Interlocked.Exchange(ref s_restarting, 0);
      return Problem(exception.Message);
    }
  }

}
