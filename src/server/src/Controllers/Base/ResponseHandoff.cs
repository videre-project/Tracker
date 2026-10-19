/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;


namespace MTGOTracker.Controllers.Base;

internal static class ResponseHandoff
{
  /// <summary>
  /// Completes a process handoff once, after the response or a caller disconnect.
  /// </summary>
  internal static void CompleteAfterResponse(HttpContext context, Action handoff)
  {
    int completed = 0;
    void Complete()
    {
      if (Interlocked.Exchange(ref completed, 1) == 0) handoff();
    }
    var disconnected = context.RequestAborted.Register(Complete);
    context.Response.OnCompleted(() =>
    {
      disconnected.Dispose();
      Complete();
      return Task.CompletedTask;
    });
  }
}
