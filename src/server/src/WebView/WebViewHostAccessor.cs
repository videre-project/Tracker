/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


namespace Tracker.WebView;

public sealed class WebViewHostAccessor(HostForm? hostForm)
{
  public HostForm? HostForm { get; } = hostForm;
}
