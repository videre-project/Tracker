/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import React, { useState, useEffect, useMemo, useRef } from "react"
import { UnifiedLog } from "@/components/diagnostics/unified-log"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import {
  Area,
  AreaChart,
  ResponsiveContainer,
  YAxis,
  Tooltip,
  CartesianGrid,
} from "recharts"
import { Cpu, Zap, Server } from "lucide-react"
import { useDiagnostics } from "@/hooks/use-diagnostics"

//
// Helpers
//

function fmt(n: number | undefined, decimals = 1): string {
  if (n == null) return "—"
  // Keep displayed values compact: >1000 → no decimals, >10 → 1 decimal
  if (Math.abs(n) >= 1000) return Math.round(n).toLocaleString()
  if (Math.abs(n) >= 100) return n.toFixed(0)
  return n.toFixed(decimals)
}

function statusColor(value: number, warn = 10, crit = 50): string {
  if (value > crit) return "#ef4444"
  if (value > warn) return "#eab308"
  return "#10b981"
}

function poolBar(active: number, max: number) {
  const pct = max > 0 ? Math.min(100, (active / max) * 100) : 0
  const ratio = max > 0 ? active / max : 0
  const color = ratio >= 0.8 ? "#ef4444" : ratio >= 0.5 ? "#eab308" : "#10b981"
  return (
    <div className="h-1.5 w-full rounded-full bg-muted">
      <div
        className="h-1.5 rounded-full transition-all"
        style={{ width: `${pct}%`, backgroundColor: color }}
      />
    </div>
  )
}

//
// Sparkline
//

function Sparkline({
  data,
  color,
  height = 36,
  domain,
  unit,
}: {
  data: { t: number; v: number }[]
  color: string
  height?: number
  domain?: [number | string, number | string]
  unit?: string
}) {
  const id = useMemo(
    () => `grad-${Math.random().toString(36).slice(2, 8)}`,
    []
  )
  return (
    <div
      className="-mx-[5px] [&_.recharts-surface]:outline-none [&_.recharts-wrapper]:outline-none"
      style={{ height }}
    >
      <ResponsiveContainer width="100%" height="100%">
        <AreaChart data={data} margin={{ top: 2, right: 0, left: 0, bottom: 0 }}>
          <defs>
            <linearGradient id={id} x1="0" y1="0" x2="0" y2="1">
              <stop offset="5%" stopColor={color} stopOpacity={0.3} />
              <stop offset="95%" stopColor={color} stopOpacity={0} />
            </linearGradient>
          </defs>
          <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" opacity={0.3} vertical={false} />
          <YAxis hide domain={domain ?? ["dataMin", "dataMax"]} />
          <Tooltip
            content={({ active, payload }) => {
              if (!active || !payload?.length) return null
              const value = payload[0].value as number
              return (
                <div className="rounded-lg border bg-background p-1.5 shadow-sm text-[11px] font-mono">
                  {fmt(value, 2)}{unit ?? ""}
                </div>
              )
            }}
            cursor={{
              stroke: "hsl(var(--muted-foreground))",
              strokeWidth: 1,
              strokeDasharray: "4 4",
            }}
          />
          <Area
            type="monotone"
            dataKey="v"
            stroke={color}
            strokeWidth={2}
            fillOpacity={1}
            fill={`url(#${id})`}
            isAnimationActive={false}
          />
        </AreaChart>
      </ResponsiveContainer>
    </div>
  )
}

//
// Stat row
//

function Stat({
  label,
  value,
  color,
}: {
  label: string
  value: string | number
  color?: string
}) {
  return (
    <div className="flex items-baseline justify-between">
      <span className="text-[11px] text-muted-foreground">{label}</span>
      <span
        className="text-[11px] font-mono"
        style={color ? { color } : undefined}
      >
        {value}
      </span>
    </div>
  )
}

//
// Unified Log Viewer (SDK + Tracker + Diver)
//

export default function DiagnosticsContent() {
  const { current, history } = useDiagnostics()

  const sdk = current?.sdk
  const diver = current?.diver
  const host = diver?.hostProcess
  const lastRate = history.length > 0 ? history[history.length - 1] : null

  // Accumulated endpoint activity — merges new deltas into running totals,
  // never removes entries so the table stays stable.
  const endpointAccum = useRef<Record<string, { count: number; avgMs: number }>>({})
  const [recentEndpoints, setRecentEndpoints] = useState<
    { endpoint: string; count: number; avgMs: number }[]
  >([])
  useEffect(() => {
    if (!lastRate || lastRate.topEndpoints.length === 0) return
    const acc = endpointAccum.current
    for (const ep of lastRate.topEndpoints) {
      const existing = acc[ep.endpoint]
      if (existing) {
        existing.count += ep.count
        existing.avgMs = ep.avgMs // latest cumulative avg
      } else {
        acc[ep.endpoint] = { count: ep.count, avgMs: ep.avgMs }
      }
    }
    setRecentEndpoints(
      Object.entries(acc)
        .sort((a, b) => b[1].count - a[1].count)
        .slice(0, 5)
        .map(([endpoint, m]) => ({ endpoint, ...m }))
    )
  }, [lastRate])

  const requestRateData = useMemo(
    () => history.map((h, i) => ({ t: i, v: h.requestsPerSec })),
    [history]
  )
  const callbackLatencyData = useMemo(
    () => history.map((h, i) => ({
      t: i,
      v: h.sdk?.peakCallbackLatencyMs ?? h.sdk?.avgCallbackLatencyMs ?? 0,
    })),
    [history]
  )
  const dispatcherData = useMemo(
    () => history.map((h, i) => ({
      t: i,
      v: h.diver?.hostProcess?.dispatcherResponsivenessMs ?? 0,
    })),
    [history]
  )

  // Combined SyncThread utilization: SDK vs Diver side-by-side
  const threadPoolData = useMemo(
    () => history.map((h, i) => ({
      t: i,
      sdk: h.sdk?.syncThreadActive ?? 0,
      diver: h.diver?.syncThreadActive ?? 0,
    })),
    [history]
  )

  // Queue depth trend: SDK vs Diver queued tasks
  const queueDepthData = useMemo(
    () => history.map((h, i) => ({
      t: i,
      sdk: h.sdk?.syncThreadQueued ?? 0,
      diver: h.diver?.syncThreadQueued ?? 0,
    })),
    [history]
  )

  const heapData = useMemo(
    () => history.map((h, i) => ({
      t: i,
      v: (h.diver?.hostProcess?.gcTotalMemory ?? 0) / 1024 / 1024,
    })),
    [history]
  )

  const cbQueueDelayData = useMemo(
    () => history.map((h, i) => ({
      t: i,
      v: h.diver?.lastCallbackQueueDelayMs ?? 0,
    })),
    [history]
  )

  const dispatcherMs = host?.dispatcherResponsivenessMs ?? 0
  const dispatcherColor = statusColor(dispatcherMs, 100, 2000)

  // Compute avg and peak dispatcher latency from history
  const dispatcherAvg = useMemo(() => {
    const vals = history.map(h => h.diver?.hostProcess?.dispatcherResponsivenessMs ?? 0).filter(v => v >= 0)
    return vals.length > 0 ? vals.reduce((a, b) => a + b, 0) / vals.length : 0
  }, [history])
  const dispatcherPeak = useMemo(() => {
    const vals = history.map(h => h.diver?.hostProcess?.dispatcherResponsivenessMs ?? 0).filter(v => v >= 0)
    return vals.length > 0 ? Math.max(...vals) : 0
  }, [history])

  const heapStats = useMemo(() => {
    const vals = heapData.map(h => h.v).filter(v => v > 0)
    if (vals.length === 0) return { min: 0, avg: 0, max: 0 }
    return {
      min: Math.min(...vals),
      avg: vals.reduce((a, b) => a + b, 0) / vals.length,
      max: Math.max(...vals),
    }
  }, [heapData])

  return (
    <div className="flex flex-1 flex-col gap-4 p-4 pt-2 relative">
        {/* Two-column layout: SDK left, Diver right */}
        <div className="grid gap-6 md:grid-cols-2">

        {/* ============================================================== */}
        {/* SDK / Tracker                                                   */}
        {/* ============================================================== */}
        <div className="flex flex-col gap-4">
          <div className="flex flex-row items-center gap-2 mb-2">
            <Server className="h-5 w-5 text-indigo-400" />
            <h3 className="text-lg font-medium text-foreground tracking-tight">
              Tracker Service
            </h3>
          </div>

          <div className="grid grid-cols-1 xl:grid-cols-2 gap-4 flex-1">

            {/* IPC Throughput */}
            <Card className="border-sidebar-border/60 flex flex-col">
              <CardHeader className="flex flex-row items-center justify-between space-y-0 p-4 pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">
                  IPC Throughput
                </CardTitle>
                <Zap className="h-4 w-4 text-muted-foreground" />
              </CardHeader>
              <CardContent className="p-4 pt-0 flex-1 flex flex-col">
                <div className="grid grid-cols-3 gap-2 tabular-nums">
                  <div>
                    <div className="text-2xl font-bold font-mono">
                      {fmt(lastRate?.requestsPerSec, 0)}
                    </div>
                    <div className="text-[10px] text-muted-foreground">req/sec</div>
                  </div>
                  <div className="text-center">
                    <div className="text-2xl font-bold font-mono">
                      {fmt(lastRate?.callbacksPerSec, 0)}
                    </div>
                    <div className="text-[10px] text-muted-foreground">cb/sec</div>
                  </div>
                  <div className="text-right">
                    <div className="text-lg font-bold font-mono">
                      {sdk?.inFlightRequests ?? "—"}
                    </div>
                    <div className="text-[10px] text-muted-foreground">in-flight</div>
                  </div>
                </div>
                <Sparkline data={requestRateData} color="#6366f1" height={32} unit=" req/s" />
                {/* Side-by-side SDK/Diver SyncThread Utilization with queue depth */}
                <div className="mt-3 pt-3 border-t border-border/30 space-y-3">
                  <div className="text-[11px] text-muted-foreground font-medium">
                    SyncThread & Queue Depth
                  </div>
                  <div className="grid grid-cols-2 gap-3">
                    {/* SDK Section */}
                    <div className="space-y-1">
                      <div className="flex items-baseline justify-between text-[11px] text-muted-foreground">
                        <span>SDK</span>
                        <span className="tabular-nums font-mono text-[11px]">
                          {sdk?.syncThreadActive ?? 0}/{sdk?.syncThreadMax ?? 0}
                          <span className="opacity-60"> + {sdk?.syncThreadQueued ?? 0}q</span>
                        </span>
                      </div>
                      <div className="-mx-[5px] [&_.recharts-surface]:outline-none [&_.recharts-wrapper]:outline-none" style={{ height: 44 }}>
                        <ResponsiveContainer width="100%" height="100%">
                          <AreaChart data={history.map((h, i) => ({
                            t: i,
                            active: h.sdk?.syncThreadActive ?? 0,
                            queued: h.sdk?.syncThreadQueued ?? 0,
                          }))} margin={{ top: 2, right: 0, left: 0, bottom: 0 }}>
                            <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" opacity={0.3} vertical={false} />
                            <YAxis hide domain={[0, sdk?.syncThreadMax ?? 16]} />
                            <Tooltip
                              content={({ active, payload }) => {
                                if (!active || !payload?.length) return null
                                const active_val = payload.find(p => p.dataKey === "active")?.value ?? 0
                                const queued_val = payload.find(p => p.dataKey === "queued")?.value ?? 0
                                return (
                                  <div className="rounded-lg border bg-background p-1.5 shadow-sm text-[11px] font-mono">
                                    Active: {active_val}, Queued: {queued_val}
                                  </div>
                                )
                              }}
                              cursor={{ stroke: "hsl(var(--muted-foreground))", strokeWidth: 1, strokeDasharray: "4 4" }}
                            />
                            <Area type="monotone" dataKey="active" stroke="#6366f1" strokeWidth={2} fill="url(#grad-sdk-active)" isAnimationActive={false} />
                            <Area type="monotone" dataKey="queued" stroke="#6366f1" strokeWidth={0} fill="url(#grad-sdk-queued)" isAnimationActive={false} />
                            <defs>
                              <linearGradient id="grad-sdk-active" x1="0" y1="0" x2="0" y2="1">
                                <stop offset="5%" stopColor="#6366f1" stopOpacity={0.4} />
                                <stop offset="95%" stopColor="#6366f1" stopOpacity={0} />
                              </linearGradient>
                              <linearGradient id="grad-sdk-queued" x1="0" y1="0" x2="0" y2="1">
                                <stop offset="5%" stopColor="#6366f1" stopOpacity={0.15} />
                                <stop offset="95%" stopColor="#6366f1" stopOpacity={0} />
                              </linearGradient>
                            </defs>
                          </AreaChart>
                        </ResponsiveContainer>
                      </div>
                    </div>

                    {/* Diver Section */}
                    <div className="space-y-1">
                      <div className="flex items-baseline justify-between text-[11px] text-muted-foreground">
                        <span>Diver</span>
                        <span className="tabular-nums font-mono text-[11px]">
                          {diver?.syncThreadActive ?? 0}/{diver?.syncThreadMax ?? 0}
                          <span className="opacity-60"> + {diver?.syncThreadQueued ?? 0}q</span>
                        </span>
                      </div>
                      <div className="-mx-[5px] [&_.recharts-surface]:outline-none [&_.recharts-wrapper]:outline-none" style={{ height: 44 }}>
                        <ResponsiveContainer width="100%" height="100%">
                          <AreaChart data={history.map((h, i) => ({
                            t: i,
                            active: h.diver?.syncThreadActive ?? 0,
                            queued: h.diver?.syncThreadQueued ?? 0,
                          }))} margin={{ top: 2, right: 0, left: 0, bottom: 0 }}>
                            <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" opacity={0.3} vertical={false} />
                            <YAxis hide domain={[0, diver?.syncThreadMax ?? 16]} />
                            <Tooltip
                              content={({ active, payload }) => {
                                if (!active || !payload?.length) return null
                                const active_val = payload.find(p => p.dataKey === "active")?.value ?? 0
                                const queued_val = payload.find(p => p.dataKey === "queued")?.value ?? 0
                                return (
                                  <div className="rounded-lg border bg-background p-1.5 shadow-sm text-[11px] font-mono">
                                    Active: {active_val}, Queued: {queued_val}
                                  </div>
                                )
                              }}
                              cursor={{ stroke: "hsl(var(--muted-foreground))", strokeWidth: 1, strokeDasharray: "4 4" }}
                            />
                            <Area type="monotone" dataKey="active" stroke="#f59e0b" strokeWidth={2} fill="url(#grad-diver-active)" isAnimationActive={false} />
                            <Area type="monotone" dataKey="queued" stroke="#f59e0b" strokeWidth={0} fill="url(#grad-diver-queued)" isAnimationActive={false} />
                            <defs>
                              <linearGradient id="grad-diver-active" x1="0" y1="0" x2="0" y2="1">
                                <stop offset="5%" stopColor="#f59e0b" stopOpacity={0.4} />
                                <stop offset="95%" stopColor="#f59e0b" stopOpacity={0} />
                              </linearGradient>
                              <linearGradient id="grad-diver-queued" x1="0" y1="0" x2="0" y2="1">
                                <stop offset="5%" stopColor="#f59e0b" stopOpacity={0.15} />
                                <stop offset="95%" stopColor="#f59e0b" stopOpacity={0} />
                              </linearGradient>
                            </defs>
                          </AreaChart>
                        </ResponsiveContainer>
                      </div>
                    </div>
                  </div>
                </div>
                <div className="mt-auto pt-3 border-t border-border/30 grid grid-cols-2 gap-x-4 gap-y-0.5">
                  <Stat label="Total req" value={sdk?.totalRequests?.toLocaleString() ?? "—"} />
                  <Stat label="Total cb" value={sdk?.callbacksReceived?.toLocaleString() ?? "—"} />
                </div>
              </CardContent>
            </Card>

            {/* Callback Latency */}
            <Card className="border-sidebar-border/60 flex flex-col">
              <CardHeader className="flex flex-row items-center justify-between space-y-0 p-4 pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">
                  Callback Latency
                </CardTitle>
              </CardHeader>
              <CardContent className="p-4 pt-0 flex-1 flex flex-col">
                <div className="grid grid-cols-3 gap-1 tabular-nums overflow-hidden">
                  <div className="min-w-0">
                    <div
                      className="text-xl font-bold font-mono truncate"
                      style={{ color: statusColor(sdk?.peakCallbackLatencyMs ?? 0) }}
                    >
                      {fmt(sdk?.peakCallbackLatencyMs)}ms
                    </div>
                    <div className="text-[10px] text-muted-foreground">peak (5s)</div>
                  </div>
                  <div className="text-center min-w-0">
                    <div className="text-base font-mono truncate">{fmt(sdk?.avgCallbackLatencyMs)}ms</div>
                    <div className="text-[10px] text-muted-foreground">avg</div>
                  </div>
                  <div className="text-right min-w-0">
                    <div className="text-base font-mono truncate">{fmt(sdk?.lastCallbackLatencyMs)}ms</div>
                    <div className="text-[10px] text-muted-foreground">last</div>
                  </div>
                </div>
                <Sparkline
                  data={callbackLatencyData}
                  color={statusColor(sdk?.peakCallbackLatencyMs ?? 0)}
                  height={32}
                  unit=" ms"
                />
                {/* Top active endpoints — persisted until new activity replaces them */}
                <div className="mt-auto pt-2 border-t border-border/30">
                  {recentEndpoints.length > 0 ? (
                    <table className="w-full text-[11px] tabular-nums">
                      <thead>
                        <tr className="text-muted-foreground">
                          <th className="text-left font-medium pb-0.5">Endpoint</th>
                          <th className="text-right font-medium pb-0.5 w-12">Req</th>
                          <th className="text-right font-medium pb-0.5 w-14">Avg</th>
                        </tr>
                      </thead>
                      <tbody>
                        {recentEndpoints.map(ep => (
                          <tr key={ep.endpoint}>
                            <td className="py-0.5 font-mono truncate max-w-[200px]" title={`/${ep.endpoint}`}>/{ep.endpoint}</td>
                            <td className="py-0.5 text-right font-mono text-muted-foreground w-12">{ep.count}</td>
                            <td className="py-0.5 text-right font-mono text-muted-foreground w-14">{fmt(ep.avgMs)}<span className="text-[10px] opacity-50 ml-0.5">ms</span></td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  ) : (
                    <div className="text-[11px] text-muted-foreground text-center py-2">
                      No recent callback activity
                    </div>
                  )}
                </div>
              </CardContent>
            </Card>
          </div>
        </div>

        {/* ============================================================== */}
        {/* MTGO / Diver                                                    */}
        {/* ============================================================== */}
        <div className="flex flex-col gap-4">
          <div className="flex flex-row items-center gap-2 mb-2">
            <Cpu className="h-5 w-5 text-sky-400" />
            <h3 className="text-lg font-medium text-foreground tracking-tight">
              MTGO Diver
            </h3>
            <div className="ml-auto flex items-center gap-2 text-sm text-muted-foreground">
              {current ? (
                <>
                  <div className="h-2 w-2 rounded-full bg-emerald-500 animate-[pulse_2s_cubic-bezier(0.4,0,0.6,1)_infinite]" />
                  <span>Connected · {new Date(current.timestamp).toLocaleTimeString()}</span>
                </>
              ) : (
                <>
                  <div className="h-2 w-2 rounded-full bg-muted-foreground/50" />
                  <span>Connecting to diagnostics feed...</span>
                </>
              )}
            </div>
          </div>

          {/* Diver: UI Thread (left) + Activity & Endpoints (right) */}
          <Card className="border-sidebar-border/60 flex-1">
            <CardHeader className="flex flex-row items-center justify-between space-y-0 p-4 pb-2">
              <CardTitle className="text-sm font-medium text-muted-foreground">
                Diver Status
              </CardTitle>
              <Cpu className="h-4 w-4 text-muted-foreground" />
            </CardHeader>
            <CardContent className="p-4 pt-0">
              <div className="grid grid-cols-1 xl:grid-cols-2 gap-4">
                {/* Left: Memory usage chart + generation breakdown */}
                <div className="space-y-3">
                  <div>
                    <div className="text-2xl font-bold font-mono">
                      {host ? `${(host.gcTotalMemory / 1024 / 1024).toFixed(0)} MB` : "—"}
                    </div>
                    <div className="text-[10px] text-muted-foreground">managed heap</div>
                    <Sparkline data={heapData} color="#06b6d4" height={56} unit=" MB" />
                    <div className="grid grid-cols-3 gap-1 tabular-nums mt-1 pt-1 border-t border-border/30">
                      <div>
                        <div className="text-[11px] font-mono">{heapStats.min > 0 ? `${heapStats.min.toFixed(0)} MB` : "—"}</div>
                        <div className="text-[10px] text-muted-foreground">min</div>
                      </div>
                      <div className="text-center">
                        <div className="text-[11px] font-mono">{heapStats.avg > 0 ? `${heapStats.avg.toFixed(0)} MB` : "—"}</div>
                        <div className="text-[10px] text-muted-foreground">avg</div>
                      </div>
                      <div className="text-right">
                        <div className="text-[11px] font-mono">{heapStats.max > 0 ? `${heapStats.max.toFixed(0)} MB` : "—"}</div>
                        <div className="text-[10px] text-muted-foreground">max</div>
                      </div>
                    </div>
                  </div>
                  {host && (host.gen0HeapSize > 0 || host.gcTotalMemory > 0) && (() => {
                    const mb = (b: number) => (b / 1024 / 1024).toFixed(1)
                    const gen0 = Math.max(0, host.gen0HeapSize)
                    const gen1 = Math.max(0, host.gen1HeapSize)
                    const gen2 = Math.max(0, host.gen2HeapSize)
                    const loh  = Math.max(0, host.lohSize)
                    const total = gen0 + gen1 + gen2 + loh || 1
                    const pct = (v: number) => `${Math.max(0.5, (v / total) * 100)}%`
                    return (
                      <div className="space-y-1.5">
                        {/* Stacked bar */}
                        <div className="h-2.5 w-full rounded-full bg-muted flex overflow-hidden">
                          <div style={{ width: pct(gen0), backgroundColor: "#10b981" }} title={`Gen0: ${mb(gen0)} MB`} />
                          <div style={{ width: pct(gen1), backgroundColor: "#6366f1" }} title={`Gen1: ${mb(gen1)} MB`} />
                          <div style={{ width: pct(gen2), backgroundColor: "#f59e0b" }} title={`Gen2: ${mb(gen2)} MB`} />
                          <div style={{ width: pct(loh),  backgroundColor: "#ef4444" }} title={`LOH: ${mb(loh)} MB`} />
                        </div>
                        <div className="flex flex-wrap gap-x-3 gap-y-0.5 text-[10px]">
                          <span><span className="inline-block w-2 h-2 rounded-sm mr-1" style={{ backgroundColor: "#10b981" }} />Gen0 {mb(gen0)}</span>
                          <span><span className="inline-block w-2 h-2 rounded-sm mr-1" style={{ backgroundColor: "#6366f1" }} />Gen1 {mb(gen1)}</span>
                          <span><span className="inline-block w-2 h-2 rounded-sm mr-1" style={{ backgroundColor: "#f59e0b" }} />Gen2 {mb(gen2)}</span>
                          <span><span className="inline-block w-2 h-2 rounded-sm mr-1" style={{ backgroundColor: "#ef4444" }} />LOH {mb(loh)}</span>
                        </div>
                        <Stat label="Pinned objects" value={host.pinnedObjects} />
                        <Stat label="GC collections" value={`${host.gcGen0Collections} / ${host.gcGen1Collections} / ${host.gcGen2Collections}`} />
                      </div>
                    )
                  })()}
                </div>

                {/* Right: UI Thread + Injection footprint */}
                <div className="xl:border-l xl:border-border/30 xl:pl-4 flex flex-col">
                  {/* UI Thread Responsiveness */}
                  <div className="space-y-2">
                    <div className="text-[10px] text-muted-foreground">
                      UI Thread Probe
                    </div>
                    <div className="grid grid-cols-3 gap-1 tabular-nums overflow-hidden">
                      <div className="min-w-0">
                        <div
                          className="text-xl font-bold font-mono truncate"
                          style={{ color: dispatcherColor }}
                        >
                          {dispatcherMs >= 2000
                            ? "BLOCKED"
                            : host ? `${fmt(dispatcherMs)}ms` : ""}
                        </div>
                        <div className="text-[10px] text-muted-foreground">last probe</div>
                      </div>
                      <div className="text-center min-w-0">
                        <div className="text-base font-mono truncate">{fmt(dispatcherAvg)}ms</div>
                        <div className="text-[10px] text-muted-foreground">avg</div>
                      </div>
                      <div className="text-right min-w-0">
                        <div
                          className="text-base font-mono truncate"
                          style={{ color: statusColor(dispatcherPeak, 100, 2000) }}
                        >
                          {fmt(dispatcherPeak)}ms
                        </div>
                        <div className="text-[10px] text-muted-foreground">peak</div>
                      </div>
                    </div>
                    <Sparkline data={dispatcherData} color={dispatcherColor} height={56} unit=" ms" />
                  </div>

                  {/* Hooks & Events — callback queue delay */}
                  {diver ? (
                    <div className="mt-3 pt-3 border-t border-border/30">
                      <div className="flex items-baseline justify-between text-[11px] text-muted-foreground mb-1">
                        <span>Callback Queue Delay</span>
                        <span className="tabular-nums">
                          {fmt(diver.lastCallbackQueueDelayMs)}<span className="text-[10px] opacity-50 ml-0.5">ms</span>
                        </span>
                      </div>
                      <Sparkline data={cbQueueDelayData} color="#8b5cf6" height={36} domain={[0, "dataMax"]} unit=" ms" />
                      <div className="flex items-baseline justify-between text-[11px] text-muted-foreground mt-1.5 tabular-nums">
                        <div className="flex gap-3">
                          <span>Hooks <span className="font-mono text-foreground">{diver.activeHooks}</span></span>
                          <span>Events <span className="font-mono text-foreground">{diver.activeEventSubscriptions}</span></span>
                        </div>
                        <span className="text-foreground">{fmt(lastRate?.diverCallbacksPerSec, 0)}<span className="text-[10px] opacity-50 ml-0.5">cb/s</span></span>
                      </div>
                    </div>
                  ) : (
                    <div className="mt-3 pt-3 border-t border-border/30 text-sm text-muted-foreground">
                      Diver not connected
                    </div>
                  )}
                </div>
              </div>
            </CardContent>
          </Card>

        </div>
      </div>

      {/* Unified Log — full width below both columns */}
      <UnifiedLog />
    </div>
  )
}
