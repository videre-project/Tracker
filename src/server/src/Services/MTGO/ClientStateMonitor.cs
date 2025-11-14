/** @file
  Copyright (c) 2024, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using MTGOSDK.Core.Logging;


namespace Tracker.Services.MTGO;

/// <summary>
/// Monitors MTGO client state and provides cancellation for client-dependent operations.
/// </summary>
/// <remarks>
/// This service is scoped per-request and automatically disposes when the request completes.
/// Subscribe to client state changes and cancel operations when the client disconnects.
/// </remarks>
public sealed class ClientStateMonitor : IDisposable
{
  private readonly IClientAPIProvider _clientProvider;
  private readonly CancellationTokenSource _cts = new();

  public ClientStateMonitor(IClientAPIProvider clientProvider)
  {
    _clientProvider = clientProvider;
    _clientProvider.ClientStateChanged += OnClientStateChanged;
  }

  /// <summary>
  /// Cancellation token that triggers when the MTGO client disconnects.
  /// </summary>
  public CancellationToken Token => _cts.Token;

  /// <summary>
  /// Gets whether the MTGO client is currently ready.
  /// </summary>
  public bool IsClientReady => _clientProvider.IsReady;

  private void OnClientStateChanged(object? sender, EventArgs e)
  {
    if (!_clientProvider.IsReady)
    {
      Log.Information("MTGO client disconnected, cancelling client-dependent operations");

      // Cancel without throwing
      try
      {
        _cts.Cancel();
      }
      catch (ObjectDisposedException)
      {
        // Already disposed, ignore
      }
    }
  }

  public void Dispose()
  {
    _clientProvider.ClientStateChanged -= OnClientStateChanged;
    _cts.Dispose();
  }
}
