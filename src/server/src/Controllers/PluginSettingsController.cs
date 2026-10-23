/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tracker.Services;
using Tracker.Controllers.Base;

namespace Tracker.Controllers;

[ApiController]
[SettingsWrite]
[Route("api/settings/plugins")]
public sealed class PluginSettingsController(PluginManager plugins) : ControllerBase
{
  public sealed record EnablePluginRequest(bool Enabled);

  [HttpGet]
  [ProducesResponseType(typeof(IReadOnlyList<PluginManager.PluginInfo>), StatusCodes.Status200OK)]
  public IActionResult List() => Run(() => Ok(plugins.List()));

  public sealed record PluginSettingsStatus(
    bool HostEnabled, bool RestartRequired, string? SettingsError,
    IReadOnlyList<PluginService.PluginFailure> Failures);

  [HttpGet("status")]
  [ProducesResponseType(typeof(PluginSettingsStatus), StatusCodes.Status200OK)]
  public IActionResult Status([FromServices] PluginService.PluginCatalog catalog) => Run(() =>
    Ok(new PluginSettingsStatus(plugins.HostEnabled, plugins.RestartRequired, plugins.SettingsError, catalog.Failures)));

  [HttpPost]
  [RequestSizeLimit(256 * 1024 * 1024)]
  [RequestFormLimits(MultipartBodyLengthLimit = 256 * 1024 * 1024)]
  public IActionResult Install(IFormFile package) => Run(() =>
  {
    using var stream = package.OpenReadStream();
    plugins.Install(stream);
    return Ok(plugins.List());
  });

  [HttpPut("{id}/enabled")]
  public IActionResult Enable(string id, EnablePluginRequest request) => Run(() =>
  {
    plugins.SetEnabled(id, request.Enabled);
    return Ok(plugins.List());
  });

  [HttpDelete("{id}")]
  public IActionResult Remove(string id) => Run(() =>
  {
    plugins.Remove(id);
    return Ok(plugins.List());
  });

  [HttpPost("{id}/restore")]
  public IActionResult Restore(string id) => Run(() =>
  {
    plugins.CancelRemoval(id);
    return Ok(plugins.List());
  });

  [HttpGet("{id}/configuration")]
  [ProducesResponseType(typeof(PluginManager.PluginConfiguration), StatusCodes.Status200OK)]
  public IActionResult Configuration(string id) => Run(() => Ok(plugins.GetConfiguration(id)));

  [HttpPut("{id}/configuration")]
  [RequestSizeLimit(1024 * 1024)]
  public IActionResult Configure(string id, JsonObject values) => Run(() =>
  {
    plugins.SaveConfiguration(id, values);
    return Ok(plugins.GetConfiguration(id));
  });

  private IActionResult Run(Func<IActionResult> action)
  {
    try
    {
      return action();
    }
    catch (KeyNotFoundException exception)
    {
      return NotFound(exception.Message);
    }
    catch (Exception exception) when (exception is ArgumentException or InvalidDataException or JsonException)
    {
      return BadRequest(exception.Message);
    }
    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
    {
      return Problem(title: "Could not update plugin files or settings", detail: exception.Message);
    }
  }
}
