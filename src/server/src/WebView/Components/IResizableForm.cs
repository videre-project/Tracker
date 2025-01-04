/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Drawing;


namespace Tracker.WebView.Components;

public interface IResizableForm
{
  Size? RestoreSize { get; set; }
}
