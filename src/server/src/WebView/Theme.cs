/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Drawing;
using System.Windows.Forms;

using Microsoft.Win32;


namespace MTGOTracker.WebView;

public static class Theme
{
  private static ApplicationSettings s_settings = new();

  public static bool IsDarkMode => s_settings.Theme == "dark" ||
    (s_settings.Theme == "system" && Registry.GetValue(
      @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
      "AppsUseLightTheme", 1) is int value && value == 0);

  public static bool UseCustomTitleBar => s_settings.UseCustomTitleBar;

  public static Color Background_Dark =
    Color.FromArgb(255, 36, 36, 36); // #242424
  public static Color Background_Light =
    Color.FromArgb(255, 80, 80, 80); // #505050

  public static void Initialize(ApplicationSettings settings)
  {
    s_settings = settings;
  }

  public static void SetTheme(this Control control)
  {
    control.BackColor = IsDarkMode
      ? Theme.Background_Dark
      : Theme.Background_Light;
  }
}
