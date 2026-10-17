/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using Microsoft.AspNetCore.Mvc;

using Tracker.Services;


namespace Tracker.Controllers;

[ApiController]
[Route("api/plugins")]
public sealed class PluginsController(PluginService.PluginCatalog catalog) : ControllerBase
{
  [HttpGet]
  public IActionResult List() => Ok(catalog.Plugins);

  [HttpGet("status")]
  public ActionResult<PluginHostStatus> Status() => new PluginHostStatus(catalog.Enabled, catalog.Failures);
}

/// <summary>Whether this process allows installed plugins to run.</summary>
public sealed record PluginHostStatus(bool Enabled, System.Collections.Generic.IReadOnlyList<PluginService.PluginFailure> Failures);
