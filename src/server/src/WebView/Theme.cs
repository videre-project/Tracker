/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Drawing;
using System.Windows.Forms;


namespace Tracker.WebView;

public static class Theme
{
  private static ApplicationOptions s_options = null!;

  public static bool IsDarkMode => s_options.IsDarkMode;

  public static bool UseCustomTitleBar => s_options.UseCustomTitleBar;

  public static Color Background_Dark =
    Color.FromArgb(255, 36, 36, 36); // #242424
  public static Color Background_Light =
    Color.FromArgb(255, 80, 80, 80); // #505050

  public static void Initialize(ApplicationOptions options)
  {
    s_options = options;
  }

  public static void SetTheme(this Control control)
  {
    control.BackColor = s_options.IsDarkMode
      ? Theme.Background_Dark
      : Theme.Background_Light;
  }
}
