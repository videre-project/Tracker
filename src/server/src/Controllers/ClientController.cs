/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using MTGOSDK.Core.Remoting;

using Tracker.Controllers.Base;
using Tracker.Services.MTGO;


namespace Tracker.Controllers;

/// <summary>
/// Client connection state management API
/// </summary>
[ApiController]
[Route("api/[controller]/[action]")]
public class ClientController : APIController
{
  private readonly IClientAPIProvider _clientProvider;

  public ClientController(IClientAPIProvider clientProvider)
  {
    _clientProvider = clientProvider;
  }

  //
  // Client State Interfaces
  //

  public interface IClientState
  {
    bool IsConnected { get; }
    bool IsInitialized { get; }
    ushort? ProcessId { get; }
    string Status { get; }
    long? MemoryUsage { get; }
    long? WorkingSet { get; }
    long? VirtualMemory { get; }
  }

  //
  // Client State Endpoints
  //

  /// <summary>
  /// Get current client connection state
  /// </summary>
  /// <returns>Client state information</returns>
  [HttpGet] // GET /api/client/getstate
  [ProducesResponseType(typeof(IClientState), StatusCodes.Status200OK)]
  public IActionResult GetState()
  {
    var isReady = _clientProvider.IsReady;
    var memory = GetMemoryUsage(isReady);

    return Ok(new
    {
      IsConnected = isReady,
      IsInitialized = isReady,
      ProcessId = _clientProvider.Pid,
      Status = isReady ? "ready" : "disconnected",
      MemoryUsage = memory?.PrivateMemory,
      WorkingSet = memory?.WorkingSet,
      VirtualMemory = memory?.VirtualMemory
    });
  }

  /// <summary>
  /// Stream real-time client connection state changes
  /// </summary>
  /// <returns>Server-sent events stream of state changes as NDJSON</returns>
  [HttpGet] // GET /api/client/watchstate
  [ProducesResponseType(typeof(IClientState), StatusCodes.Status200OK)]
  [Produces("application/x-ndjson")]
  public async Task<IActionResult> WatchState()
  {
    // Set up streaming headers
    Response.Headers.Append("Content-Type", "application/x-ndjson");
    Response.Headers.Append("Cache-Control", "no-cache");

    try
    {
      // Send initial state
      await SendCurrentState();

      // Use a channel to signal state changes from the event handler
      var channel = System.Threading.Channels.Channel.CreateUnbounded<bool>();

      void OnStateChanged(object? sender, EventArgs e)
      {
        channel.Writer.TryWrite(true);
      }

      // Subscribe to state change events
      _clientProvider.ClientStateChanged += OnStateChanged;

      try
      {
        // Wait for state changes or cancellation
        while (!HttpContext.RequestAborted.IsCancellationRequested)
        {
          // Wait for event or timeout (for periodic updates)
          try 
          {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
            cts.CancelAfter(TimeSpan.FromSeconds(1));
            
            await channel.Reader.WaitToReadAsync(cts.Token);
            // Consume all available items
            while (channel.Reader.TryRead(out _)) { }
          }
          catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
          {
            // Timeout occurred, send update anyway
          }

          await SendCurrentState();
        }
      }
      catch (OperationCanceledException)
      {
        // Client disconnected or request was cancelled
      }
      finally
      {
        // Unsubscribe from events
        _clientProvider.ClientStateChanged -= OnStateChanged;
        channel.Writer.Complete();
      }

      return new EmptyResult();
    }
    catch (TaskCanceledException)
    {
      return new EmptyResult();
    }
  }

  //
  // Helper Methods
  //

  private async Task SendCurrentState()
  {
    var isReady = _clientProvider.IsReady;
    var memory = GetMemoryUsage(isReady);

    await StreamResponse(new[]
    {
      new
      {
        IsConnected = isReady,
        IsInitialized = isReady,
        ProcessId = _clientProvider.Pid,
        Status = isReady ? "ready" : "disconnected",
        MemoryUsage = memory?.PrivateMemory,
        WorkingSet = memory?.WorkingSet,
        VirtualMemory = memory?.VirtualMemory
      }
    });
  }

  private (long? PrivateMemory, long? WorkingSet, long? VirtualMemory)? GetMemoryUsage(bool isReady)
  {
    if (!isReady) return null;

    try
    {
      var process = RemoteClient.ClientProcess;
      if (process != null && !process.HasExited)
      {
        process.Refresh();
        return (process.PrivateMemorySize64, process.WorkingSet64, process.VirtualMemorySize64);
      }
    }
    catch
    {
      // Ignore race conditions with process exit
    }
    return null;
  }
}
