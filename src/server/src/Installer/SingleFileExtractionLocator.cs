/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;


namespace Tracker.Installer;

/// <summary>
/// Locates the active .NET single-file extraction folder for the current process.
/// </summary>
public static class SingleFileExtractionLocator
{
  private const int DEFAULT_SAMPLE_FILE_COUNT = 24;
    
  /// <summary>
  /// Represents a candidate .NET single-file extraction folder.
  /// </summary>
  /// <param name="Path">The full path to the extraction folder.</param>
  /// <param name="ModuleHits">Count of modules loaded by this process from this folder.</param>
  /// <param name="LockHits">Count of sampled files locked by this process.</param>
  /// <param name="IsRuntimeLayout">True when expected runtime artifacts were found.</param>
  /// <param name="LockingProcessIds">Distinct process IDs holding file locks in this folder sample.</param>
  public sealed record ExtractionCandidate(
    string Path,
    int ModuleHits,
    int LockHits,
    bool IsRuntimeLayout,
    IReadOnlyCollection<int> LockingProcessIds)
  {
    /// <summary>
    /// Weighted score for selecting the best extraction candidate.
    /// </summary>
    public int Score =>
      (ModuleHits * 100) +
      (LockHits * 20) +
      (IsRuntimeLayout ? 50 : 0);
  }

  /// <summary>
  /// Attempts to locate the extraction folder for the running process.
  /// </summary>
  /// <param name="extractionDirectory">The detected extraction directory when successful.</param>
  /// <param name="appName">
  /// Optional app folder name under %TEMP%\.net. Defaults to <see cref="ProductInfo.Name"/>.
  /// </param>
  /// <returns>True when a candidate extraction folder is found.</returns>
  public static bool TryGetActiveExtractionDirectory(
    out string? extractionDirectory,
    string? appName = null)
  {
    var candidate = GetBestCandidate(appName);
    extractionDirectory = candidate?.Path;
    return extractionDirectory != null;
  }

  /// <summary>
  /// Gets the highest-scoring extraction candidate for the running process.
  /// </summary>
  public static ExtractionCandidate? GetBestCandidate(string? appName = null)
  {
    var candidates = EnumerateCandidates(appName, DEFAULT_SAMPLE_FILE_COUNT).ToArray();

    return candidates
      .OrderByDescending(candidate => candidate.Score)
      .ThenByDescending(candidate => candidate.ModuleHits)
      .ThenByDescending(candidate => candidate.LockHits)
      .FirstOrDefault(candidate => candidate.IsRuntimeLayout);
  }

  /// <summary>
  /// Enumerates all candidate extraction folders discovered under %TEMP%\.net\{AppName}.
  /// </summary>
  public static IEnumerable<ExtractionCandidate> EnumerateCandidates(
    string? appName = null,
    int sampleFileCount = DEFAULT_SAMPLE_FILE_COUNT)
  {
    sampleFileCount = Math.Max(4, sampleFileCount);

    var rootPath = GetExtractionRootPath(appName);
    if (!Directory.Exists(rootPath))
    {
      yield break;
    }

    var currentProcess = Process.GetCurrentProcess();
    var currentPid = currentProcess.Id;

    var moduleCounts = GetModuleCounts(currentProcess, rootPath);

    foreach (var candidatePath in SafeEnumerateDirectories(rootPath))
    {
      var moduleHits = moduleCounts.TryGetValue(candidatePath, out var hitCount)
        ? hitCount
        : 0;

      var sampleFiles = GetSentinelFiles(candidatePath, sampleFileCount).ToArray();
      var lockingPids = RestartManager.GetLockingProcessIds(sampleFiles);
      var lockHits = lockingPids.Contains(currentPid) ? sampleFiles.Length : 0;

      yield return new ExtractionCandidate(
        Path: candidatePath,
        ModuleHits: moduleHits,
        LockHits: lockHits,
        IsRuntimeLayout: IsRuntimeLayout(candidatePath),
        LockingProcessIds: lockingPids);
    }
  }

  private static string GetExtractionRootPath(string? appName)
  {
    var bundleAppName = string.IsNullOrWhiteSpace(appName)
      ? ProductInfo.Name
      : appName;

    return Path.Combine(Path.GetTempPath(), ".net", bundleAppName);
  }

  private static Dictionary<string, int> GetModuleCounts(Process process, string rootPath)
  {
    var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    try
    {
      foreach (ProcessModule module in process.Modules)
      {
        var filePath = module.FileName;
        if (string.IsNullOrWhiteSpace(filePath))
        {
          continue;
        }

        if (!filePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        var relativePath = Path.GetRelativePath(rootPath, filePath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal))
        {
          continue;
        }

        var token = relativePath
          .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries)
          .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(token))
        {
          continue;
        }

        var candidatePath = Path.GetFullPath(Path.Combine(rootPath, token));
        counts[candidatePath] = counts.TryGetValue(candidatePath, out var existing)
          ? existing + 1
          : 1;
      }
    }
    catch
    {
      // Ignore module enumeration failures and continue with lock/runtimelayout scoring.
    }

    return counts;
  }

  private static IEnumerable<string> GetSentinelFiles(string candidatePath, int maxCount)
  {
    static bool IsSentinelExtension(string path)
    {
      var ext = Path.GetExtension(path);
      return ext.Equals(".dll", StringComparison.OrdinalIgnoreCase)
             || ext.Equals(".json", StringComparison.OrdinalIgnoreCase);
    }

    return SafeEnumerateFiles(candidatePath)
      .Where(IsSentinelExtension)
      .OrderBy(path => path.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
      .ThenBy(path => path.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
      .Take(maxCount);
  }

  private static bool IsRuntimeLayout(string candidatePath)
  {
    try
    {
      var entryAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
      var appDllName = string.IsNullOrWhiteSpace(entryAssemblyName)
        ? null
        : $"{entryAssemblyName}.dll";

      var hasDeps = Directory.EnumerateFiles(candidatePath, "*.deps.json", SearchOption.TopDirectoryOnly).Any();
      var hasRuntimeConfig = Directory.EnumerateFiles(candidatePath, "*.runtimeconfig.json", SearchOption.TopDirectoryOnly).Any();
      var managedAssemblies = Directory.EnumerateFiles(candidatePath, "*.dll", SearchOption.TopDirectoryOnly).Take(6).Count();
      var hasEntryAssembly = appDllName == null
        || File.Exists(Path.Combine(candidatePath, appDllName));

      return hasDeps && hasRuntimeConfig && hasEntryAssembly && managedAssemblies >= 3;
    }
    catch
    {
      return false;
    }
  }

  private static IEnumerable<string> SafeEnumerateDirectories(string rootPath)
  {
    try
    {
      return Directory.EnumerateDirectories(rootPath, "*", SearchOption.TopDirectoryOnly).ToArray();
    }
    catch
    {
      return [];
    }
  }

  private static IEnumerable<string> SafeEnumerateFiles(string candidatePath)
  {
    try
    {
      return Directory.EnumerateFiles(candidatePath, "*", SearchOption.TopDirectoryOnly).ToArray();
    }
    catch
    {
      return [];
    }
  }
}
