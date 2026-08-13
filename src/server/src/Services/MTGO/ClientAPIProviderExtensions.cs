/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;

using MTGOSDK.Core.Logging;


namespace Tracker.Services.MTGO;

public static class ClientAPIProviderExtensions
{
  public static bool TryGetCurrentUser(
    this IClientAPIProvider clientProvider,
    out UserIdentity? identity,
    bool requireReady = true)
  {
    identity = null;

    if (requireReady && !clientProvider.IsReady)
    {
      return false;
    }

    // The provider owns the authoritative identity once the client loop has
    // observed it. Prefer that value so callers do not need to re-read a
    // remote MTGO object at every API boundary (and so reconnect transitions
    // cannot race an otherwise valid cached identity).
    if (clientProvider.CurrentUser is { IsValid: true } currentIdentity)
    {
      identity = currentIdentity;
      return true;
    }

    if (clientProvider.Client == null)
    {
      return false;
    }

    try
    {
      var currentUser = clientProvider.Client.CurrentUser;
      int id = currentUser?.Id ?? 0;
      string username = currentUser?.Name ?? string.Empty;
      return UserIdentity.TryCreate(id, username, out identity);
    }
    catch (Exception ex) when (IsTransientCurrentUserReadFailure(ex))
    {
      Log.Trace(
        "Current MTGO user identity is not readable yet: {Message}",
        ex.Message);
      return false;
    }
  }

  public static bool TryGetCurrentUsername(
    this IClientAPIProvider clientProvider,
    out string username,
    bool requireReady = true)
  {
    username = string.Empty;

    if (requireReady && !clientProvider.IsReady)
    {
      return false;
    }

    if (clientProvider.CurrentUser is { IsValid: true } currentIdentity)
    {
      username = currentIdentity.Username;
      return true;
    }

    if (clientProvider.Client == null)
    {
      return false;
    }

    if (!clientProvider.TryGetCurrentUser(
          out UserIdentity? identity,
          requireReady))
      return false;

    username = identity!.Username;
    return true;
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
