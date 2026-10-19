/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MTGOTracker.Controllers.Base;
using MTGOTracker.Services;

namespace MTGOTracker.Controllers;

[ApiController]
[SettingsWrite]
[Route("api/settings/updates")]
public sealed class UpdatesController(UpdateService updates) : ControllerBase
{
  [HttpGet]
  public UpdateService.UpdateStatus Get() => updates.Status;

  [HttpPost("check")]
  public Task<ActionResult<UpdateService.UpdateStatus>> Check(CancellationToken cancellationToken) =>
    Run(() => updates.CheckAsync(cancellationToken));

  [HttpPost("download")]
  public Task<ActionResult<UpdateService.UpdateStatus>> Download(CancellationToken cancellationToken) =>
    Run(() => updates.DownloadAsync(cancellationToken));

  [HttpPost("apply")]
  public Task<ActionResult<UpdateService.UpdateStatus>> Apply(CancellationToken cancellationToken) =>
    Run(async () =>
    {
      await updates.ApplyAsync(cancellationToken);
      // The installer is ready and waiting for this process, even if the caller disconnects.
      ResponseHandoff.CompleteAfterResponse(HttpContext, updates.CompleteHandoff);
    });

  [HttpPut("schedule")]
  public Task<ActionResult<UpdateService.UpdateStatus>> Schedule([FromBody] bool scheduled, CancellationToken cancellationToken) =>
    Run(() => updates.ScheduleAsync(scheduled, cancellationToken));

  private async Task<ActionResult<UpdateService.UpdateStatus>> Run(Func<Task> action)
  {
    try { await action(); return updates.Status; }
    catch (InvalidOperationException exception) { return Conflict(exception.Message); }
    catch (InvalidDataException exception) { return BadRequest(exception.Message); }
    catch (TimeoutException exception) { return Problem(exception.Message, statusCode: 504); }
    catch (HttpRequestException exception) { return Problem(exception.Message, statusCode: 502); }
    catch (UnauthorizedAccessException exception) { return Problem(exception.Message); }
    catch (IOException exception) { return Problem(exception.Message); }
  }
}
