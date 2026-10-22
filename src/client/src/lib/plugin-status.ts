/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import type { PluginInstallation } from "@/types/api"

export function pluginStatus(plugin: PluginInstallation, hostEnabled = true) {
  if (plugin.pendingRemoval) return "Removal pending"
  if (plugin.pendingUpdate) return plugin.loaded ? "Update pending" : "Install pending"
  if (plugin.error) return "Failed"
  if (!hostEnabled) return "Bypassed"
  if (plugin.enabled && !plugin.loaded) return "Enable after restart"
  if (!plugin.enabled && plugin.loaded) return "Disable after restart"
  return plugin.loaded ? "Running" : "Disabled"
}
