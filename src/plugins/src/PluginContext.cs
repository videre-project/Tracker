/* @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
*/

using Microsoft.Extensions.Configuration;


namespace MTGOTracker.Plugins;

/// <summary>
/// Configuration and storage belonging to one installed plugin.
/// </summary>
/// <param name="Id">The plugin ID from its installation manifest.</param>
/// <param name="InstallationDirectory">The directory containing the plugin.</param>
/// <param name="DataDirectory">The plugin's persistent data directory.</param>
/// <param name="Configuration">
/// Effective plugin configuration: schema defaults, then saved settings, then
/// the host's Plugins:{Id} overrides, including environment and command-line values.
/// </param>
public sealed record PluginContext(
  string Id,
  string InstallationDirectory,
  string DataDirectory,
  IConfiguration Configuration);
