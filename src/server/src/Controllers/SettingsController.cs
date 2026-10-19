/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using MTGOTracker.Services;
using MTGOTracker.Controllers.Base;

namespace MTGOTracker.Controllers;

[ApiController]
[SettingsWrite]
[Route("api/settings")]
public sealed class SettingsController(SettingsService settings, PluginManager plugins, IHostApplicationLifetime lifetime, ApplicationOptions options) : ControllerBase
{
  private static int s_restarting;
  public sealed record SettingsResponse(ApplicationSettings Application, bool RestartRequired, string? Error);

  public sealed record HostInstance(int ProcessId);

  [HttpGet("instance")]
  public HostInstance Instance() => new(Environment.ProcessId);

  [HttpGet]
  public SettingsResponse Get() => new(settings.Current, settings.RestartRequired || plugins.RestartRequired, settings.Error);

  public sealed record StorageLocation(string Id, string Name, string Description, string Path);

  [HttpGet("storage")]
  public StorageLocation[] GetStorage() =>
  [
    new("data", "Application data", "Recordings, collection history, and preferences.", options.UserDataFolder),
    new("plugins", "Plugin packages", "Installed plugin assemblies and their dependencies.", plugins.DirectoryPath),
    new("plugin-data", "Plugin data", "Files saved by plugins, retained after uninstalling.", Path.Combine(options.UserDataFolder, "PluginData"))
  ];

  [HttpPost("storage/{id}/open")]
  public IActionResult OpenFolder(string id)
  {
    var location = GetStorage().FirstOrDefault(location => location.Id == id);
    if (location == null) return NotFound();
    try
    {
      Directory.CreateDirectory(location.Path);
      using var process = Process.Start(new ProcessStartInfo(location.Path) { UseShellExecute = true });
      return NoContent();
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
    {
      return Problem(exception.Message);
    }
  }

  [HttpPost("restart")]
  public IActionResult Restart()
  {
    if (Interlocked.Exchange(ref s_restarting, 1) != 0) return Accepted();
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
        start.ArgumentList.Add(argument);
      start.ArgumentList.Add($"--restart-after={Environment.ProcessId}");
      using var next = Process.Start(start)
        ?? throw new InvalidOperationException("Tracker could not be restarted.");
      ResponseHandoff.CompleteAfterResponse(HttpContext, lifetime.StopApplication);
      return Accepted();
    }
    catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
    {
      Interlocked.Exchange(ref s_restarting, 0);
      return Problem(exception.Message);
    }
  }

  [HttpPut]
  public ActionResult<SettingsResponse> Save(ApplicationSettings value)
  {
    try
    {
      settings.Save(value);
      return Get();
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidDataException)
    {
      return BadRequest(exception.Message);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
      return Problem(title: "Could not save application settings", detail: exception.Message);
    }
  }
}
