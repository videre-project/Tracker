/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core.Logging;


namespace Tracker.Services.MTGO;

public static class ClientAPIProviderExtensions
{
  public static bool TryGetCurrentUsername(
    this IClientAPIProvider clientProvider,
    out string username,
    bool requireReady = true)
  {
    username = string.Empty;

    if ((requireReady && !clientProvider.IsReady) ||
        clientProvider.Client == null)
    {
      return false;
    }

    try
    {
      username = clientProvider.Client.CurrentUser?.Name ?? string.Empty;
      return !string.IsNullOrWhiteSpace(username);
    }
    catch (Exception ex) when (IsTransientCurrentUserReadFailure(ex))
    {
      Log.Trace(
        "Current MTGO user is not readable yet: {Message}",
        ex.Message);
      return false;
    }
  }

  private static bool IsTransientCurrentUserReadFailure(Exception ex)
  {
    for (Exception? current = ex; current != null; current = current.InnerException)
    {
      if (current.Message.Contains("Couldn't find object in pinned pool", StringComparison.OrdinalIgnoreCase) ||
          current.Message.Contains("DynamicObject threw an exception", StringComparison.OrdinalIgnoreCase) ||
          current.Message.Contains("getter threw an exception", StringComparison.OrdinalIgnoreCase) ||
          current.Message.Contains("Cannot use RemoteObjectRef object after", StringComparison.OrdinalIgnoreCase))
      {
        return true;
      }
    }

    return false;
  }
}
