/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System.Collections.Generic;

using Microsoft.AspNetCore.Mvc;

using MTGOTracker.Services;


namespace MTGOTracker.Controllers;

[ApiController]
[Route("api/plugins")]
public sealed class PluginsController(PluginManager plugins) : ControllerBase
{
  [HttpGet]
  public IReadOnlyList<PluginManager.LoadedPlugin> List() => plugins.LoadedPlugins;

  [HttpGet("status")]
  public ActionResult<PluginHostStatus> Status() => new PluginHostStatus(plugins.HostEnabled, plugins.Failures);
}

/// <summary>
/// Whether this process allows installed plugins to run.
/// </summary>
public sealed record PluginHostStatus(bool Enabled, IReadOnlyList<PluginManager.PluginFailure> Failures);
