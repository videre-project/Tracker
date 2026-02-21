/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.Linq;

using MTGOSDK.Win32.API;


namespace Tracker.Installer;

/// <summary>
/// Small wrapper around Restart Manager to retrieve lock owners for files.
/// </summary>
internal static class RestartManager
{
  private const int ERROR_MORE_DATA = 234;

  /// <summary>
  /// Returns process IDs currently locking any of the supplied files.
  /// </summary>
  public static HashSet<int> GetLockingProcessIds(IEnumerable<string> filePaths)
  {
    var files = filePaths
      .Where(path => !string.IsNullOrWhiteSpace(path))
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToArray();

    if (files.Length == 0)
    {
      return [];
    }

    uint sessionHandle = 0;
    var key = Guid.NewGuid().ToString("N");

    try
    {
      var startCode = Rstrtmgr.RmStartSession(
        out sessionHandle,
        dwSessionFlags: 0,
        strSessionKey: key);

      if (startCode != 0)
      {
        return [];
      }

      var registerCode = Rstrtmgr.RmRegisterResources(
        sessionHandle,
        nFiles: (uint)files.Length,
        rgsFileNames: files,
        nApplications: 0,
        rgApplications: [],
        nServices: 0,
        rgsServiceNames: []);

      if (registerCode != 0)
      {
        return [];
      }

      uint needed;
      uint count = 0;
      uint rebootReasons;

      var firstListCode = Rstrtmgr.RmGetList(
        sessionHandle,
        out needed,
        ref count,
        rgAffectedApps: [],
        out rebootReasons);

      if (firstListCode == 0 && needed == 0)
      {
        return [];
      }

      if (firstListCode != ERROR_MORE_DATA && firstListCode != 0)
      {
        return [];
      }

      count = needed;
      var apps = new Rstrtmgr.RM_PROCESS_INFO[count];

      var secondListCode = Rstrtmgr.RmGetList(
        sessionHandle,
        out needed,
        ref count,
        rgAffectedApps: apps,
        out rebootReasons);

      if (secondListCode != 0)
      {
        return [];
      }

      return apps
        .Take((int)count)
        .Select(app => app.Process.dwProcessId)
        .Where(pid => pid > 0)
        .ToHashSet();
    }
    catch
    {
      return [];
    }
    finally
    {
      if (sessionHandle != 0)
      {
        _ = Rstrtmgr.RmEndSession(sessionHandle);
      }
    }
  }
}
