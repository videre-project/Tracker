/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

using MTGOSDK.Core.Remoting;

namespace MTGOTracker.Installer;

/// <summary>
/// Installs a staged release in a separate process before handing over from the running host.
/// </summary>
internal static class UpdateInstaller
{
  internal sealed record UpdatePlan(
    string Version, string UserDataFolder, string InstallDirectory,
    int PreviousProcessId, string ReadyFile, string ErrorFile);

  internal static bool MTGORunning => RemoteClient.MTGOProcess() is not null;

  internal static Version ParseVersion(string value)
  {
    if (!Version.TryParse(value.TrimStart('v', 'V'), out var version) || version.Build < 0)
      throw new InvalidDataException($"Invalid release version: {value}");
    return new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
  }

  internal static bool TryRun(string[] arguments)
  {
    if (arguments.Length != 2 || arguments[0] != "--install-update") return false;
    UpdatePlan? plan = null;
    try
    {
      plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(arguments[1]))
        ?? throw new InvalidDataException("The update plan is empty.");
      if (ParseVersion(plan.Version) != ParseVersion(ProductInfo.Version))
        throw new InvalidDataException("The downloaded executable does not match the release version.");
      if (MTGORunning) throw new InvalidOperationException("Close MTGO before installing the update.");
      string executable = Environment.ProcessPath!;
      string destination = Path.Combine(plan.InstallDirectory, Path.GetFileName(executable));
      var progress = new Progress<InstallerProgressUpdate>();
      if (!InstallationBootstrapper.InstallToDirectory(executable, plan.InstallDirectory, progress))
        throw new IOException("The staged release could not be installed.");
      File.WriteAllText(plan.ReadyFile, plan.Version);
      try
      {
        using var previous = Process.GetProcessById(plan.PreviousProcessId);
        previous.WaitForExit();
      }
      catch (ArgumentException) { /* The previous host already exited. */ }
      if (MTGORunning)
        throw new InvalidOperationException("MTGO was reopened before the update finished. Close MTGO and start the installed release.");
      InstallationBootstrapper.RefreshExistingShortcuts(destination, plan.UserDataFolder);
      var start = new ProcessStartInfo(destination)
      {
        WorkingDirectory = plan.InstallDirectory,
        UseShellExecute = false
      };
      start.Environment["TRACKER_USER_DATA_FOLDER"] = plan.UserDataFolder;
      start.Environment["TRACKER_DISABLE_INSTALLER"] = "v" + ProductInfo.Version.TrimStart('v', 'V');
      using var next = Process.Start(start) ?? throw new IOException("The installed release could not start.");
    }
    catch (Exception exception)
    {
      if (plan is not null) File.WriteAllText(plan.ErrorFile, exception.Message);
      Environment.ExitCode = 1;
    }
    return true;
  }
}
