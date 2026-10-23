/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Tracker.Controllers.Base;

/// <summary>
/// Requires an explicit API request when changing settings or installing executable plugins.
/// </summary>
public sealed class SettingsWriteAttribute : ActionFilterAttribute
{
  public override void OnActionExecuting(ActionExecutingContext context)
  {
    var request = context.HttpContext.Request;
    if (!HttpMethods.IsGet(request.Method) && request.Headers["X-Tracker-Settings"] != "1")
    {
      context.Result = new ObjectResult("Settings changes require the X-Tracker-Settings header.")
      {
        StatusCode = StatusCodes.Status403Forbidden
      };
    }
  }
}
