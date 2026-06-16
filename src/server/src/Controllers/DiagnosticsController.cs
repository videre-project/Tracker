/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting;

using Tracker.Controllers.Base;
using Tracker.Services;


namespace Tracker.Controllers;

/// <summary>
/// SDK and Diver diagnostics streaming API.
/// </summary>
[ApiController]
[Route("api/[controller]/[action]")]
public class DiagnosticsController : APIController
{
  /// <summary>
  /// Stream real-time SDK and Diver diagnostics as NDJSON (polled every 500ms).
  /// </summary>
  [HttpGet] // GET /api/diagnostics/watchmetrics
  [Produces("application/x-ndjson")]
  public async Task<IActionResult> WatchMetrics()
  {
    DisableBuffering();
    SetNdjsonContentType();
    Response.Headers.Append("Cache-Control", "no-cache");

    try
    {
      while (!HttpContext.RequestAborted.IsCancellationRequested)
      {
        object? sdkMetrics = null;
        object? diverMetrics = null;

        // Only query metrics when the RemoteClient is alive
        if (RemoteClient.IsInitialized && !RemoteClient.IsDisposed)
        {
          try { sdkMetrics = RemoteClient.GetSdkDiagnostics(); } catch { }
          try { diverMetrics = RemoteClient.GetDiverDiagnostics(); } catch { }
        }

        var snapshot = new
        {
          Timestamp = DateTime.UtcNow,
          Sdk = sdkMetrics,
          Diver = diverMetrics,
          Tracker = BuildTrackerEndpoints(),
        };

        await StreamResponse(new[] { snapshot });
        await Task.Delay(500, HttpContext.RequestAborted);
      }
    }
    catch (OperationCanceledException)
    {
      // Client disconnected
    }

    return new EmptyResult();
  }

  /// <summary>
  /// Get a single diagnostics snapshot.
  /// </summary>
  [HttpGet] // GET /api/diagnostics/getmetrics
  [ProducesResponseType(StatusCodes.Status200OK)]
  public IActionResult GetMetrics()
  {
    object? sdkMetrics = null;
    object? diverMetrics = null;

    if (RemoteClient.IsInitialized && !RemoteClient.IsDisposed)
    {
      try { sdkMetrics = RemoteClient.GetSdkDiagnostics(); } catch { }
      try { diverMetrics = RemoteClient.GetDiverDiagnostics(); } catch { }
    }

    return Ok(new
    {
      Timestamp = DateTime.UtcNow,
      Sdk = sdkMetrics,
      Diver = diverMetrics,
      Tracker = BuildTrackerEndpoints(),
    });
  }


  /// <summary>
  /// Stream unified SDK/Tracker + Diver logs as NDJSON.
  /// </summary>
  [HttpGet] // GET /api/diagnostics/watchlogs
  [Produces("application/x-ndjson")]
  public async Task<IActionResult> WatchLogs()
  {
    DisableBuffering();
    SetNdjsonContentType();
    Response.Headers.Append("Cache-Control", "no-cache");

    // Ensure Diver log tailing is running (idempotent)
    LogStreamService.StartDiverLogTail();

    try
    {
      // Send initial buffered logs
      var initial = LogStreamService.GetSnapshot();
      long cursor = 0;
      foreach (var entry in initial)
      {
        await StreamResponse(new[] { FormatLogEntry(entry) });
        if (entry.Seq > cursor) cursor = entry.Seq;
      }

      // Stream only new entries (by sequence number, immune to sorted insertion)
      while (!HttpContext.RequestAborted.IsCancellationRequested)
      {
        var newer = LogStreamService.GetAfter(cursor);
        foreach (var entry in newer)
        {
          await StreamResponse(new[] { FormatLogEntry(entry) });
          if (entry.Seq > cursor) cursor = entry.Seq;
        }

        await Task.Delay(250, HttpContext.RequestAborted);
      }
    }
    catch (OperationCanceledException) { }

    return new EmptyResult();
  }

  private static object FormatLogEntry(LogStreamService.LogEntry e) => new
  {
    e.Timestamp,
    e.Source,
    e.Level,
    e.Logger,
    e.Message,
    e.MessageId,
  };

  private static object BuildTrackerEndpoints()
  {
    var snapshot = RequestMetrics.GetSnapshot();
    var endpoints = new Dictionary<string, object>();
    foreach (var kvp in snapshot)
    {
      var (count, active, avgMs, lastMs) = kvp.Value;
      endpoints[kvp.Key] = new
      {
        Count = count,
        Active = active,
        AvgMs = avgMs,
        LastMs = lastMs
      };
    }

    return new
    {
      Endpoints = endpoints,
      Streams = APIController.GetStreamMetricsSnapshot(),
    };
  }

  /// <summary>
  /// Streams the Diver log file as NDJSON lines, tailing new content as it's written.
  /// Sends the last N lines initially, then streams new lines as they appear.
  /// </summary>
  [HttpGet] // GET /api/diagnostics/watchdiverlog?tail=50
  public async Task<IActionResult> WatchDiverLog([FromQuery] int tail = 50)
  {
    // Resolve the Diver log path: %LOCALAPPDATA%\MTGOSDK\Logs\Diver-{port}.log
    var process = RemoteClient.ClientProcess;
    if (process == null || process.HasExited)
    {
      Log.Warning("[DiagnosticsController] WatchDiverLog: MTGO client not connected");
      return StatusCode(503, "MTGO client not connected");
    }

    int port = process.Id + 1024;
    string logDir = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "MTGOSDK",
      "Logs");
    string logPath = Path.Combine(logDir, $"Diver-{port}.log");

    Log.Debug("[DiagnosticsController] WatchDiverLog: Looking for {Path}", logPath);

    if (!System.IO.File.Exists(logPath))
    {
      Log.Warning("[DiagnosticsController] WatchDiverLog: File not found at {Path}", logPath);
      return NotFound($"Diver log not found: {logPath}");
    }

    DisableBuffering();
    SetNdjsonContentType();
    Response.Headers.Append("Cache-Control", "no-cache");

    try
    {
      using var stream = new FileStream(logPath, FileMode.Open,
        FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
      using var reader = new StreamReader(stream);

      // Read initial tail lines by seeking to the end and scanning backward
      var initialLines = ReadTailLines(stream, reader, tail);
      foreach (var line in initialLines)
      {
        await StreamResponse(new[] { new { Line = line } });
      }

      // Tail new lines as they're written
      while (!HttpContext.RequestAborted.IsCancellationRequested)
      {
        string? line = await reader.ReadLineAsync();
        if (line != null)
        {
          await StreamResponse(new[] { new { Line = line } });
        }
        else
        {
          // No new content — wait before polling again
          await Task.Delay(250, HttpContext.RequestAborted);
        }
      }
    }
    catch (OperationCanceledException) { }

    return new EmptyResult();
  }

  /// <summary>
  /// Reads the last N lines from a file stream.
  /// </summary>
  private static List<string> ReadTailLines(FileStream stream, StreamReader reader, int count)
  {
    // Read all lines (log files are typically small) and return the tail
    var lines = new List<string>();
    string? line;
    while ((line = reader.ReadLine()) != null)
    {
      lines.Add(line);
    }

    int skip = Math.Max(0, lines.Count - count);
    return lines.GetRange(skip, lines.Count - skip);
  }
}
