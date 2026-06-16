/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Http;


namespace Tracker.Services;

/// <summary>
/// Per-endpoint request metrics for the Tracker's own API.
/// </summary>
public static class RequestMetrics
{
  private sealed class Metrics
  {
    public long Count;
    public long Active;
    public long TotalTicks;
    public long LastTicks;
  }

  private static readonly ConcurrentDictionary<string, Metrics> s_endpoints = new();

  /// <summary>
  /// Returns a snapshot of per-endpoint statistics, excluding diagnostics
  /// polling to reduce noise.
  /// </summary>
  public static Dictionary<string, (long Count, long Active, double AvgMs, double LastMs)> GetSnapshot()
  {
    var result = new Dictionary<string, (long, long, double, double)>();
    double freq = Stopwatch.Frequency;

    foreach (var kvp in s_endpoints)
    {
      // Filter out our own diagnostics polling
      if (kvp.Key.Contains("Diagnostics", StringComparison.OrdinalIgnoreCase))
        continue;

      long count = Interlocked.Read(ref kvp.Value.Count);
      long active = Interlocked.Read(ref kvp.Value.Active);
      if (count == 0 && active == 0) continue;

      result[kvp.Key] = (
        count,
        active,
        count > 0
          ? Math.Round((Interlocked.Read(ref kvp.Value.TotalTicks) / (double)count)
            / freq * 1000.0, 2)
          : 0,
        Math.Round(Interlocked.Read(ref kvp.Value.LastTicks) / freq * 1000.0, 2)
      );
    }
    return result;
  }

  internal static void Start(string endpoint)
  {
    var m = s_endpoints.GetOrAdd(endpoint, _ => new Metrics());
    Interlocked.Increment(ref m.Active);
  }

  internal static void Record(string endpoint, long elapsedTicks)
  {
    var m = s_endpoints.GetOrAdd(endpoint, _ => new Metrics());
    Interlocked.Increment(ref m.Count);
    Interlocked.Add(ref m.TotalTicks, elapsedTicks);
    Interlocked.Exchange(ref m.LastTicks, elapsedTicks);
  }

  internal static void Stop(string endpoint)
  {
    if (!s_endpoints.TryGetValue(endpoint, out var m)) return;

    long active;
    do
    {
      active = Interlocked.Read(ref m.Active);
      if (active <= 0) return;
    }
    while (Interlocked.CompareExchange(ref m.Active, active - 1, active) != active);
  }
}

/// <summary>
/// ASP.NET middleware that records per-endpoint request timing.
/// </summary>
public class RequestMetricsMiddleware
{
  private readonly RequestDelegate _next;

  public RequestMetricsMiddleware(RequestDelegate next) => _next = next;

  public async Task InvokeAsync(HttpContext context)
  {
    var sw = Stopwatch.StartNew();
    var endpoint = context.GetEndpoint();
    var route = (endpoint as Microsoft.AspNetCore.Routing.RouteEndpoint)
      ?.RoutePattern.RawText;

    if (route != null)
    {
      RequestMetrics.Start(route);
    }

    try
    {
      await _next(context);
    }
    finally
    {
      sw.Stop();

      // Use the endpoint route pattern (e.g. "api/Client/GetState")
      // rather than the raw URL path to avoid unbounded cardinality
      if (route != null)
      {
        RequestMetrics.Record(route, sw.ElapsedTicks);
        RequestMetrics.Stop(route);
      }
    }
  }
}
