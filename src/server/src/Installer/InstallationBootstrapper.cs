/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;


namespace Tracker.Installer;

/// <summary>
/// Ensures the current version is installed under LocalAppData and relaunches from there when needed.
/// </summary>
public static class InstallationBootstrapper
{
  [DllImport("user32.dll")]
  private static extern bool SetForegroundWindow(IntPtr hWnd);

  [DllImport("user32.dll")]
  private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

  [DllImport("user32.dll")]
  private static extern bool IsIconic(IntPtr hWnd);

  [DllImport("user32.dll")]
  private static extern bool AllowSetForegroundWindow(int dwProcessId);

  private const int SW_RESTORE = 9;

  /// <summary>
  /// Ensures the app is installed in %LocalAppData%\{ProductName}\v{Version}.
  /// Returns true when the current process should exit after triggering a relaunch.
  /// </summary>
  public static bool EnsureInstalledAndRelaunchIfNeeded(string[]? args = null)
  {
#if DEBUG
    return false;
#else
    if (ShouldSkipBootstrap(args))
    {
      return false;
    }

    if (!OperatingSystem.IsWindows())
    {
      return false;
    }

    var processPath = Environment.ProcessPath;
    if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
    {
      return false;
    }

    var installRoot = GetInstallRoot();
    var versionFolderName = GetVersionFolderName();
    var versionInstallDirectory = Path.Combine(installRoot, versionFolderName);
    var selectedInstallRoot = installRoot;
    var selectedInstallDirectory = versionInstallDirectory;
    var createDesktopShortcut = true;
    var createStartMenuShortcut = true;
    var didInstallInWizard = false;
    var launchedDuringWizard = false;

    var isRunningFromVersionInstall = IsPathUnderDirectory(processPath, versionInstallDirectory);

    Directory.CreateDirectory(installRoot);

    // If this version is already installed elsewhere, start it and exit.
    if (Directory.Exists(versionInstallDirectory) && !isRunningFromVersionInstall)
    {
      var existingExe = Path.Combine(versionInstallDirectory, Path.GetFileName(processPath));
      if (File.Exists(existingExe))
      {
        CreateLocalShortcut(existingExe, installRoot);
        _ = StartInstalledExecutable(existingExe, versionInstallDirectory, waitForVisibleWindow: false);
        return true;
      }
    }

    // Already running from the proper install directory.
    if (isRunningFromVersionInstall)
    {
      return false;
    }

    if (!Directory.Exists(versionInstallDirectory))
    {
      var options = PromptForInstallationOptions(
        installRoot,
        (wizardOptions, progress) =>
        {
          var targetVersionDirectory = Path.Combine(wizardOptions.InstallationRootDirectory, versionFolderName);
          if (!InstallToDirectory(processPath, targetVersionDirectory, progress))
          {
            return false;
          }

          var targetExePath = Path.Combine(targetVersionDirectory, Path.GetFileName(processPath));
          progress.Report(new InstallerProgressUpdate("Creating shortcuts...", 99));

          CreateLocalShortcut(targetExePath, wizardOptions.InstallationRootDirectory);

          if (wizardOptions.CreateDesktopShortcut)
          {
            CreateDesktopShortcut(targetExePath);
          }

          if (wizardOptions.CreateStartMenuShortcut)
          {
            CreateStartMenuShortcut(targetExePath);
          }

          progress.Report(new InstallerProgressUpdate("Launching Tracker...", 100));
          launchedDuringWizard = StartInstalledExecutable(
            targetExePath,
            targetVersionDirectory,
            waitForVisibleWindow: true);

          progress.Report(new InstallerProgressUpdate("Installation complete.", 100));
          return true;
        });

      if (options == null)
      {
        // User canceled installation wizard.
        return true;
      }

      if (options.UsePortableMode)
      {
        EnablePortableMode();
        return false;
      }

      selectedInstallRoot = options.InstallationRootDirectory;
      selectedInstallDirectory = Path.Combine(selectedInstallRoot, versionFolderName);
      createDesktopShortcut = options.CreateDesktopShortcut;
      createStartMenuShortcut = options.CreateStartMenuShortcut;
      didInstallInWizard = true;

      if (launchedDuringWizard)
      {
        // Child process was already launched while the wizard was still visible.
        return true;
      }
    }

    var installedExe = Path.Combine(selectedInstallDirectory, Path.GetFileName(processPath));
    if (!File.Exists(installedExe))
    {
      return false;
    }

    if (!didInstallInWizard)
    {
      var shortcutRoot = selectedInstallRoot;
      CreateLocalShortcut(installedExe, shortcutRoot);

      if (createDesktopShortcut)
      {
        CreateDesktopShortcut(installedExe);
      }

      if (createStartMenuShortcut)
      {
        CreateStartMenuShortcut(installedExe);
      }
    }

    _ = StartInstalledExecutable(installedExe, selectedInstallDirectory, waitForVisibleWindow: false);

    return true;
#endif
  }

  private static InstallerWizardOptions? PromptForInstallationOptions(
    string defaultInstallDirectory,
    Func<InstallerWizardOptions, IProgress<InstallerProgressUpdate>, bool> installAction)
  {
    var eulaText = GetEulaText();
    using var wizard = new InstallerWizardForm(defaultInstallDirectory, eulaText, installAction);
    return wizard.ShowDialog() == DialogResult.OK && wizard.InstallationSucceeded
      ? wizard.Options
      : null;
  }

  private static bool InstallToDirectory(
    string processPath,
    string targetInstallDirectory,
    IProgress<InstallerProgressUpdate> progress)
  {
    try
    {
      progress.Report(new InstallerProgressUpdate("Preparing installation directory...", 5));

      progress.Report(new InstallerProgressUpdate("Locating extracted runtime payload...", 8));
      var sourceDirectory = ResolveSourceDirectory(processPath);

      Directory.CreateDirectory(targetInstallDirectory);

      progress.Report(new InstallerProgressUpdate("Scanning files to install...", 12));

      var sourceFiles = new System.Collections.Generic.List<string>(capacity: 2048);
      foreach (var sourceFile in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
      {
        sourceFiles.Add(sourceFile);

        var fileCount = sourceFiles.Count;
        if (fileCount <= 20 || (fileCount % 250) == 0)
        {
          progress.Report(new InstallerProgressUpdate($"Scanning files ({fileCount})...", 12));
        }
      }

      progress.Report(new InstallerProgressUpdate($"Copying application files ({sourceFiles.Count})...", 15));

      var totalFiles = Math.Max(1, sourceFiles.Count);
      for (int i = 0; i < sourceFiles.Count; i++)
      {
        var sourceFile = sourceFiles[i];
        var relativePath = Path.GetRelativePath(sourceDirectory, sourceFile);
        var destinationPath = Path.Combine(targetInstallDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourceFile, destinationPath, overwrite: true);

        if ((i % 20) == 0 || i == sourceFiles.Count - 1)
        {
          var percent = 15 + (int)Math.Round(75.0 * (i + 1) / totalFiles);
          progress.Report(new InstallerProgressUpdate($"Copying files ({i + 1}/{totalFiles})...", percent));
        }
      }

      progress.Report(new InstallerProgressUpdate("Installing launcher...", 92));
      File.Copy(processPath, Path.Combine(targetInstallDirectory, Path.GetFileName(processPath)), overwrite: true);

      progress.Report(new InstallerProgressUpdate("Finalizing installation...", 98));
      return true;
    }
    catch
    {
      return false;
    }
  }

  private static bool ShouldSkipBootstrap(string[]? args)
  {
    var disableInstaller = Environment.GetEnvironmentVariable("TRACKER_DISABLE_INSTALLER")?.Trim();
    if (string.Equals(disableInstaller, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(disableInstaller, "true", StringComparison.OrdinalIgnoreCase) ||
        IsInstallerDisabledForCurrentVersion(disableInstaller))
    {
      return true;
    }

    // Swashbuckle CLI can load this assembly from a dotnet host context to generate OpenAPI docs.
    // In that path, installer behavior must be disabled.
    var processPath = Environment.ProcessPath ?? string.Empty;
    var processName = Path.GetFileNameWithoutExtension(processPath);
    if (string.Equals(processName, "dotnet", StringComparison.OrdinalIgnoreCase))
    {
      var commandLine = Environment.CommandLine;
      if (commandLine.Contains("swagger", StringComparison.OrdinalIgnoreCase)
          || commandLine.Contains("swashbuckle", StringComparison.OrdinalIgnoreCase)
          || commandLine.Contains("tofile", StringComparison.OrdinalIgnoreCase)
          || (args?.Any(arg => arg.Contains("swagger", StringComparison.OrdinalIgnoreCase)) ?? false))
      {
        return true;
      }
    }

    if (AppContext.BaseDirectory.Contains("swashbuckle.aspnetcore.cli", StringComparison.OrdinalIgnoreCase)
        || AppContext.BaseDirectory.Contains("dotnet-swagger", StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    return false;
  }

  private static void EnablePortableMode()
  {
    const string envName = "TRACKER_DISABLE_INSTALLER";
    var envValue = GetVersionFolderName();

    Environment.SetEnvironmentVariable(envName, envValue, EnvironmentVariableTarget.Process);

    try
    {
      Environment.SetEnvironmentVariable(envName, envValue, EnvironmentVariableTarget.User);
    }
    catch
    {
      // Ignore inability to persist user-level env var.
    }
  }

  private static bool IsInstallerDisabledForCurrentVersion(string? disableInstallerValue)
  {
    if (string.IsNullOrWhiteSpace(disableInstallerValue))
    {
      return false;
    }

    var current = NormalizeVersionToken(GetVersionFolderName());
    var configured = NormalizeVersionToken(disableInstallerValue);
    return string.Equals(configured, current, StringComparison.OrdinalIgnoreCase);
  }

  private static string NormalizeVersionToken(string value)
  {
    var normalized = value.Trim();
    if (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
    {
      normalized = normalized[1..];
    }

    return normalized;
  }

  private static string ResolveSourceDirectory(string processPath)
  {
    if (SingleFileExtractionLocator.TryGetActiveExtractionDirectory(out var extractionDirectory) &&
        !string.IsNullOrWhiteSpace(extractionDirectory) &&
        Directory.Exists(extractionDirectory))
    {
      return extractionDirectory;
    }

    // Fallback for non-single-file/development style execution.
    return AppContext.BaseDirectory;
  }

  private static string GetInstallRoot()
  {
    return Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      ProductInfo.Name);
  }

  private static string GetVersionFolderName()
  {
    var version = ProductInfo.Version?.Trim() ?? "0.0.0";
    return version.StartsWith("v", StringComparison.OrdinalIgnoreCase)
      ? version
      : $"v{version}";
  }

  private static bool IsPathUnderDirectory(string filePath, string directoryPath)
  {
    try
    {
      var fullFilePath = Path.GetFullPath(filePath)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
      var fullDirectoryPath = Path.GetFullPath(directoryPath)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        + Path.DirectorySeparatorChar;

      return fullFilePath.StartsWith(fullDirectoryPath, StringComparison.OrdinalIgnoreCase);
    }
    catch
    {
      return false;
    }
  }

  private static bool StartInstalledExecutable(
    string executablePath,
    string workingDirectory,
    bool waitForVisibleWindow)
  {
    try
    {
      var process = Process.Start(new ProcessStartInfo
      {
        FileName = executablePath,
        WorkingDirectory = workingDirectory,
        UseShellExecute = true,
      });

      if (process == null)
      {
        return false;
      }

      if (!waitForVisibleWindow)
      {
        // Fast handoff path for installer relaunch.
        return true;
      }

      _ = AllowSetForegroundWindow(process.Id);

      FocusProcessWindow(process, TimeSpan.FromSeconds(15));

      return true;
    }
    catch
    {
      return false;
    }
  }

  private static void FocusProcessWindow(Process process, TimeSpan timeout)
  {
    try
    {
      _ = AllowSetForegroundWindow(process.Id);

      var stopwatch = Stopwatch.StartNew();
      while (stopwatch.Elapsed < timeout && !process.HasExited)
      {
        process.Refresh();
        var mainWindowHandle = process.MainWindowHandle;
        if (mainWindowHandle != IntPtr.Zero)
        {
          if (IsIconic(mainWindowHandle))
          {
            _ = ShowWindowAsync(mainWindowHandle, SW_RESTORE);
          }

          _ = SetForegroundWindow(mainWindowHandle);
          return;
        }

        Thread.Sleep(50);
      }
    }
    catch
    {
      // Best effort focus handling.
    }
  }

  private static void CreateLocalShortcut(string targetExePath, string installRoot)
  {
    var shortcutPath = Path.Combine(installRoot, $"{ProductInfo.Name}.lnk");
    CreateShortcut(shortcutPath, targetExePath);
  }

  private static void CreateDesktopShortcut(string targetExePath)
  {
    var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    if (string.IsNullOrWhiteSpace(desktop))
    {
      return;
    }

    var shortcutPath = Path.Combine(desktop, $"{ProductInfo.Name}.lnk");
    CreateShortcut(shortcutPath, targetExePath);
  }

  private static void CreateStartMenuShortcut(string targetExePath)
  {
    var programsPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);

    if (string.IsNullOrWhiteSpace(programsPath))
    {
      return;
    }

    var shortcutPath = Path.Combine(programsPath, $"{ProductInfo.Name}.lnk");
    CreateShortcut(shortcutPath, targetExePath);
  }

  private static void CreateShortcut(string shortcutPath, string targetExePath)
  {
    if (string.IsNullOrWhiteSpace(shortcutPath)
        || string.IsNullOrWhiteSpace(targetExePath)
        || !File.Exists(targetExePath))
    {
      return;
    }

    if (TryCreateShortcutViaCom(shortcutPath, targetExePath))
    {
      return;
    }

    _ = TryCreateShortcutViaPowerShell(shortcutPath, targetExePath);
  }

  private static bool TryCreateShortcutViaCom(string shortcutPath, string targetExePath)
  {
    try
    {
      Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

      var shellType = Type.GetTypeFromProgID("WScript.Shell");
      if (shellType == null)
      {
        return false;
      }

      object? shell = Activator.CreateInstance(shellType);
      if (shell == null)
      {
        return false;
      }

      try
      {
        var createShortcut = shellType.GetMethod("CreateShortcut");
        var shortcut = createShortcut?.Invoke(shell, [shortcutPath]);
        if (shortcut == null)
        {
          return false;
        }

        try
        {
          var shortcutType = shortcut.GetType();
          shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [targetExePath]);
          shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, [Path.GetDirectoryName(targetExePath)!]);
          shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, [$"{targetExePath},0"]);
          shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, [$"Launch {ProductInfo.Name}"]);
          shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
        }
        finally
        {
          _ = System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
        }

        return true;
      }
      finally
      {
        _ = System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
      }
    }
    catch
    {
      return false;
    }
  }

  private static bool TryCreateShortcutViaPowerShell(string shortcutPath, string targetExePath)
  {
    try
    {
      var workingDirectory = Path.GetDirectoryName(targetExePath)!;
      var escapedShortcutPath = shortcutPath.Replace("'", "''", StringComparison.Ordinal);
      var escapedTargetExePath = targetExePath.Replace("'", "''", StringComparison.Ordinal);
      var escapedWorkingDirectory = workingDirectory.Replace("'", "''", StringComparison.Ordinal);
      var escapedDescription = $"Launch {ProductInfo.Name}".Replace("'", "''", StringComparison.Ordinal);

      var script =
        $"$s=New-Object -ComObject WScript.Shell;" +
        $"$k=$s.CreateShortcut('{escapedShortcutPath}');" +
        $"$k.TargetPath='{escapedTargetExePath}';" +
        $"$k.WorkingDirectory='{escapedWorkingDirectory}';" +
        $"$k.IconLocation='{escapedTargetExePath},0';" +
        $"$k.Description='{escapedDescription}';" +
        "$k.Save();";

      var process = Process.Start(new ProcessStartInfo
      {
        FileName = "powershell.exe",
        Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"",
        UseShellExecute = false,
        CreateNoWindow = true,
      });

      return process != null;
    }
    catch
    {
      return false;
    }
  }

  private static string GetEulaText()
  {
    try
    {
      var assembly = typeof(Program).Assembly;
      using var eulaStream = assembly.GetManifestResourceStream("Tracker.EULA.md")
        ?? assembly.GetManifestResourceStream("EULA.md")
        ?? assembly.GetManifestResourceNames()
          .Where(name => name.EndsWith("EULA.md", StringComparison.OrdinalIgnoreCase))
          .Select(assembly.GetManifestResourceStream)
          .FirstOrDefault(stream => stream != null);

      if (eulaStream != null)
      {
        using var reader = new StreamReader(eulaStream);
        var embeddedEula = reader.ReadToEnd();
        if (!string.IsNullOrWhiteSpace(embeddedEula))
        {
          return embeddedEula;
        }
      }
    }
    catch
    {
      // Fall back to file-based lookup.
    }

    var candidates = new[]
    {
      Path.Combine(AppContext.BaseDirectory, "EULA.md"),
      Path.Combine(AppContext.BaseDirectory, "LICENSE"),
      Path.Combine(AppContext.BaseDirectory, "..", "EULA.md"),
      Path.Combine(AppContext.BaseDirectory, "..", "LICENSE"),
      Path.Combine(AppContext.BaseDirectory, "..", "..", "EULA.md"),
      Path.Combine(AppContext.BaseDirectory, "..", "..", "LICENSE"),
      Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "EULA.md"),
      Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "LICENSE"),
    };

    foreach (var candidate in candidates)
    {
      try
      {
        var fullPath = Path.GetFullPath(candidate);
        if (File.Exists(fullPath))
        {
          return File.ReadAllText(fullPath);
        }
      }
      catch
      {
        // Ignore path and continue.
      }
    }

    return $"{ProductInfo.Name} - End User License Agreement{Environment.NewLine}{Environment.NewLine}By installing and using this software, you agree to the terms of the Apache-2.0 license.";
  }
}
