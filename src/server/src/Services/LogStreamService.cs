/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using MTGOSDK.Core.Logging;
using MTGOSDK.Core.Remoting;


[assembly: MetadataUpdateHandler(typeof(Tracker.Services.LogStreamHotReloadHandler))]

namespace Tracker.Services;

/// <summary>
/// Flushes the log buffer on hot reload so stale entries don't persist.
/// </summary>
internal static class LogStreamHotReloadHandler
{
  static void ClearCache(Type[]? _) => LogStreamService.Clear();
  static void UpdateApplication(Type[]? _) => LogStreamService.Clear();
}

/// <summary>
/// Static in-memory log buffer that captures SDK/Tracker and Diver log entries
/// for the diagnostics page. Modeled after <see cref="RequestMetricsService"/>.
/// </summary>
public static class LogStreamService
{
  public record LogEntry(
    long Seq,               // Monotonic sequence number (stable cursor)
    DateTime Timestamp,
    string Source,          // "SDK", "Tracker", "Diver"
    string Level,           // "Debug", "Information", "Warning", "Error", etc.
    string Logger,          // Category name
    string Message,
    int? MessageId = null); // IPC correlation ID (from TCP frame MessageId)

  private const int MaxHistory = 1000;
  private static readonly LinkedList<LogEntry> s_history = new();
  private static readonly ReaderWriterLockSlim s_lock = new();
  private static long s_seq;
  private static long s_diverLogPosition;

  /// <summary>
  /// Record a log entry. Called from ConsoleLogger and Diver log parser.
  /// </summary>
  public static void Record(DateTime timestamp, string source, string level, string logger, string message, int? messageId = null)
  {
    s_lock.EnterWriteLock();
    try
    {
      if (s_history.Count >= MaxHistory)
        s_history.RemoveFirst();

      var seq = Interlocked.Increment(ref s_seq);
      var entry = new LogEntry(seq, timestamp, source, level, logger, message, messageId);

      // Insert in timestamp order (backdated entries like Diver log history)
      var node = s_history.Last;
      while (node != null && node.Value.Timestamp > timestamp)
        node = node.Previous;

      if (node == null)
        s_history.AddFirst(entry);
      else
        s_history.AddAfter(node, entry);
    }
    finally
    {
      s_lock.ExitWriteLock();
    }
  }

  /// <summary>
  /// Returns all buffered entries with Seq greater than <paramref name="afterSeq"/>.
  /// </summary>
  public static List<LogEntry> GetAfter(long afterSeq)
  {
    s_lock.EnterReadLock();
    try
    {
      // Entries are sorted by timestamp but seq is monotonic,
      // so we need to scan for entries with seq > afterSeq
      return s_history.Where(e => e.Seq > afterSeq).ToList();
    }
    finally
    {
      s_lock.ExitReadLock();
    }
  }

  /// <summary>
  /// Flush the log buffer (called on hot reload).
  /// </summary>
  public static void Clear()
  {
    s_lock.EnterWriteLock();
    try
    {
      s_history.Clear();
    }
    finally
    {
      s_lock.ExitWriteLock();
    }
  }

  /// <summary>
  /// Returns a snapshot of all buffered log entries (sorted by timestamp).
  /// </summary>
  public static List<LogEntry> GetSnapshot()
  {
    s_lock.EnterReadLock();
    try
    {
      return s_history.ToList();
    }
    finally
    {
      s_lock.ExitReadLock();
    }
  }

  //
  // Diver log file tailing
  //

  // Matches: "timestamp [Level] [Category] [#42] message" or "timestamp [Level] [Category] message"
  private static readonly Regex s_diverLogRegex =
    new(@"^(\S+)\s+\[(\w+)\]\s+\[([^\]]+)\]\s+(?:\[#(\d+)\]\s+)?(.*)$", RegexOptions.Compiled);
  private static CancellationTokenSource? s_diverTailCts;

  /// <summary>
  /// Start background polling of the Diver log file.
  /// Cancels any previous tail so it works correctly across reconnects.
  /// </summary>
  public static void StartDiverLogTail()
  {
    // Cancel previous tail (old process / old log file)
    s_diverTailCts?.Cancel();
    s_diverTailCts = new CancellationTokenSource();
    var cts = s_diverTailCts;

    // Reset file position so we read from the start of the new log
    s_diverLogPosition = 0;

    Task.Run(async () =>
    {
      string? logPath = null;
      for (int i = 0; i < 20 && !cts.IsCancellationRequested; i++)
      {
        try
        {
          if (RemoteClient.Port is ushort port)
          {
            logPath = Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
              "MTGOSDK", "Logs", $"Diver-{port}.log");
            if (File.Exists(logPath)) break;
          }
        }
        catch { }
        await Task.Delay(500, cts.Token).ConfigureAwait(false);
      }

      if (logPath == null || !File.Exists(logPath)) return;

      Log.Debug("Tailing Diver log: {Path}", logPath);

      while (!cts.IsCancellationRequested)
      {
        try
        {
          ReadNewDiverLines(logPath);
        }
        catch { }
        try { await Task.Delay(500, cts.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) { break; }
      }
    });
  }

  private static void ReadNewDiverLines(string logPath)
  {
    using var stream = new FileStream(logPath, FileMode.Open,
      FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    stream.Seek(s_diverLogPosition, SeekOrigin.Begin);
    using var reader = new StreamReader(stream);

    string? line;
    while ((line = reader.ReadLine()) != null)
    {
      var match = s_diverLogRegex.Match(line);
      if (!match.Success) continue;

      if (DateTime.TryParse(match.Groups[1].Value, out var ts))
      {
        int? mid = match.Groups[4].Success && int.TryParse(match.Groups[4].Value, out var id)
          ? id : null;
        Record(ts, "Diver", match.Groups[2].Value,
          match.Groups[3].Value, match.Groups[5].Value, mid);
      }
    }

    s_diverLogPosition = stream.Position;
  }
}
