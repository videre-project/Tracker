/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using Microsoft.AspNetCore.Mvc;

namespace Example;

[ApiController]
[Route("session")]
public sealed class SessionController(SessionObserver observer) : ControllerBase
{
  // Tracker prefixes this with /api/plugins/session-observer.
  [HttpGet]
  public SessionSnapshot Get() => observer.Snapshot;
}
