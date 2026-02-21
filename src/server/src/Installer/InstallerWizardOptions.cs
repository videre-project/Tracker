/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.Installer;

/// <summary>
/// User-selected options from the installer wizard.
/// </summary>
public sealed record InstallerWizardOptions(
  bool UsePortableMode,
  string InstallationRootDirectory,
  bool CreateDesktopShortcut,
  bool CreateStartMenuShortcut
);
