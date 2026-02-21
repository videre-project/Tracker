/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.Installer;

/// <summary>
/// Progress state displayed in the installer wizard.
/// </summary>
public readonly record struct InstallerProgressUpdate(
  string Message,
  int Percent
);
