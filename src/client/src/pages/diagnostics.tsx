import React, { useState, useEffect, useMemo, useRef, useCallback } from "react"
import { useLogsStream, type LogEntry } from "@/hooks/use-logs-stream"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import {
  Area,
  AreaChart,
  ResponsiveContainer,
  YAxis,
  Tooltip,
  CartesianGrid,
} from "recharts"
import { Cpu, Zap, Server, Settings2, Search, Maximize2, Minimize2 } from "lucide-react"
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

const SOURCE_COLORS = {
  SDK: "#6366f1",
  Tracker: "#3b82f6",
  Diver: "#f59e0b",
} as const

const LEVEL_COLORS = {
  Debug: "#089fa2",
  Information: "#089fa2",
  Warning: "#ff8c00",
  Error: "#ff0000",
  Critical: "#ff0000",
  Trace: "#6b7280",
} as const

const LEVEL_ORDER = ["Trace", "Debug", "Information", "Warning", "Error", "Critical"]
const SOURCES = ["SDK", "Tracker", "Diver"] as const

function loadFilterState() {
  try {
    const raw = localStorage.getItem("diag-log-filters")
    if (raw) return JSON.parse(raw)
  } catch {}
  return null
}

type LogGroup =
  | { type: "single"; entry: LogEntry }
  | { type: "group"; messageId: number; entries: LogEntry[] }

function LogLine({ entry }: { entry: LogEntry }) {
  const ts = new Date(entry.timestamp)
  const srcColor = SOURCE_COLORS[entry.source as keyof typeof SOURCE_COLORS] ?? "#6b7280"
  const lvlColor = LEVEL_COLORS[entry.level as keyof typeof LEVEL_COLORS] ?? "#6b7280"
  return (
    <div className="whitespace-nowrap py-px">
      <span style={{ color: "#691569" }}>
        {ts.toLocaleTimeString()}.{String(ts.getMilliseconds()).padStart(3, "0")}
      </span>
      {" "}
      <span style={{ color: srcColor }}>[{entry.source}]</span>
      {" "}
      <span className="text-muted-foreground/60">[{entry.logger}]</span>
      {" "}
      <span style={{ color: lvlColor }}>{entry.message}</span>
    </div>
  )
}

function LogGroupRow({ group, expanded, onToggle }: {
  group: Extract<LogGroup, { type: "group" }>
  expanded: boolean
  onToggle: () => void
}) {
  const header = group.entries[0]
  const srcColor = SOURCE_COLORS[header.source as keyof typeof SOURCE_COLORS] ?? "#6b7280"
  return (
    <div>
      <div
        className="whitespace-nowrap py-px cursor-pointer hover:bg-muted/30 flex items-center gap-1"
        onClick={onToggle}
      >
        <span className="text-muted-foreground/40 select-none w-3 inline-block text-center">
          {expanded ? "\u25BE" : "\u25B8"}
        </span>
        <LogLine entry={header} />
        <span className="text-[10px] text-muted-foreground/50 tabular-nums ml-1">
          #{group.messageId} ({group.entries.length})
        </span>
      </div>
      {expanded && group.entries.length > 1 && (
        <div className="border-l-2 ml-1.5 pl-2" style={{ borderColor: srcColor + "40" }}>
          {group.entries.slice(1).map((entry, i) => (
            <LogLine key={i} entry={entry} />
          ))}
        </div>
      )}
    </div>
  )
}

function UnifiedLog() {
  const { logs } = useLogsStream()
  const containerRef = useRef<HTMLDivElement>(null)
  const cardRef = useRef<HTMLDivElement>(null)
  const [autoScroll, setAutoScroll] = useState(true)
  const [isFullscreen, setIsFullscreen] = useState(false)

    // Hide siblings and lock scroll ancestor when fullscreen
    useEffect(() => {
      if (!isFullscreen) return
      const card = cardRef.current
      const parent = card?.parentElement
      if (!card || !parent) return

      // Hide sibling components
      const siblings = Array.from(parent.children).filter(el => el !== card) as HTMLElement[]
      siblings.forEach(el => el.style.display = "none")

      // Turn scroll ancestor into a flex column so flex-1 works down the chain
      const scrollAncestor = card.closest(".overflow-y-auto") as HTMLElement | null
      if (scrollAncestor) {
        scrollAncestor.style.overflow = "hidden"
        scrollAncestor.style.display = "flex"
        scrollAncestor.style.flexDirection = "column"
      }

      // Page container fills remaining space
      parent.style.minHeight = "0"

      return () => {
        siblings.forEach(el => el.style.display = "")
        parent.style.minHeight = ""
        if (scrollAncestor) {
          scrollAncestor.style.overflow = ""
          scrollAncestor.style.display = ""
          scrollAncestor.style.flexDirection = ""
        }
      }
    }, [isFullscreen])
  const [logHeight, setLogHeight] = useState(300)

  // Filter state (persisted to localStorage)
  const saved = useRef(loadFilterState())
  const [sourcesEnabled, setSourcesEnabled] = useState<Record<string, boolean>>(
    saved.current?.sources ?? { SDK: true, Tracker: true, Diver: true }
  )
  const [minLevel, setMinLevel] = useState<string>(saved.current?.level ?? "Debug")
  const [searchText, setSearchText] = useState("")
  const [debouncedSearch, setDebouncedSearch] = useState("")
  const [groupMode, setGroupMode] = useState(saved.current?.group ?? false)
  const [expandedGroups, setExpandedGroups] = useState<Set<number>>(new Set())
  const searchTimer = useRef<ReturnType<typeof setTimeout>>()

  // Persist filter preferences
  useEffect(() => {
    localStorage.setItem("diag-log-filters", JSON.stringify({
      sources: sourcesEnabled, level: minLevel, group: groupMode
    }))
  }, [sourcesEnabled, minLevel, groupMode])

  // Debounce search
  const onSearchChange = useCallback((value: string) => {
    setSearchText(value)
    clearTimeout(searchTimer.current)
    searchTimer.current = setTimeout(() => setDebouncedSearch(value), 150)
  }, [])

  // Filter logs
  const filteredLogs = useMemo(() => {
    const minIdx = LEVEL_ORDER.indexOf(minLevel)
    const lowerSearch = debouncedSearch.toLowerCase()
    return logs.filter(entry => {
      if (!sourcesEnabled[entry.source]) return false
      if (LEVEL_ORDER.indexOf(entry.level) < minIdx) return false
      if (lowerSearch && !entry.message.toLowerCase().includes(lowerSearch)
          && !entry.logger.toLowerCase().includes(lowerSearch)) return false
      return true
    })
  }, [logs, sourcesEnabled, minLevel, debouncedSearch])

  // Group by messageId when correlation mode is on
  const groupedLogs = useMemo((): LogGroup[] => {
    if (!groupMode) return filteredLogs.map(e => ({ type: "single" as const, entry: e }))
    const groups = new Map<number, LogEntry[]>()
    const result: LogGroup[] = []
    for (const entry of filteredLogs) {
      if (entry.messageId != null) {
        if (!groups.has(entry.messageId)) {
          const entries: LogEntry[] = []
          groups.set(entry.messageId, entries)
          result.push({ type: "group", messageId: entry.messageId, entries })
        }
        groups.get(entry.messageId)!.push(entry)
      } else {
        result.push({ type: "single", entry })
      }
    }
    return result
  }, [filteredLogs, groupMode])

  // Measure remaining viewport below the card's top edge
  useEffect(() => {
    const measure = () => {
      if (!cardRef.current) return
      const rect = cardRef.current.getBoundingClientRect()
      const padding = 16 // bottom page padding (p-4)
      const border = 2  // card border top + bottom
      setLogHeight(Math.max(150, window.innerHeight - rect.top - padding - border))
    }
    measure()
    window.addEventListener("resize", measure)
    const observer = new ResizeObserver(measure)
    if (cardRef.current?.parentElement) observer.observe(cardRef.current.parentElement)
    return () => {
      window.removeEventListener("resize", measure)
      observer.disconnect()
    }
  }, [])

  useEffect(() => {
    if (autoScroll && containerRef.current) {
      containerRef.current.scrollTop = containerRef.current.scrollHeight
    }
  }, [groupedLogs, autoScroll])

  const handleScroll = () => {
    const el = containerRef.current
    if (!el) return
    setAutoScroll(el.scrollHeight - el.scrollTop - el.clientHeight < 30)
  }

  const toggleSource = (src: string) =>
    setSourcesEnabled(prev => ({ ...prev, [src]: !prev[src] }))

  const cycleLevel = () => {
    const idx = LEVEL_ORDER.indexOf(minLevel)
    setMinLevel(LEVEL_ORDER[(idx + 1) % LEVEL_ORDER.length])
  }

  const toggleGroup = (mid: number) =>
    setExpandedGroups(prev => {
      const next = new Set(prev)
      if (next.has(mid)) next.delete(mid); else next.add(mid)
      return next
    })

  return (
    <Card ref={cardRef} className={`border-sidebar-border/60 overflow-hidden flex flex-col relative ${isFullscreen ? "flex-1 min-h-0" : ""}`}
      style={isFullscreen ? undefined : { height: logHeight }}
    >
      {/* Filter toolbar */}
      <div className="flex items-center justify-between gap-3 px-3 py-2 border-b border-border/40 flex-shrink-0 bg-muted/20">
        <div className="flex items-center gap-2 flex-1">
          {/* Search */}
          <div className="relative w-64">
            <Search className="absolute left-2 top-1/2 -translate-y-1/2 h-3 w-3 text-muted-foreground/60" />
            <Input
              value={searchText}
              onChange={e => onSearchChange(e.target.value)}
              placeholder="Filter logs..."
              className="h-6 pl-7 text-[10px] font-mono bg-background/50 focus-visible:ring-1 focus-visible:ring-ring border-border/60"
            />
          </div>

          <div className="w-px h-5 bg-border/40 mx-1" />

          {/* Level Select */}
          <Select value={minLevel} onValueChange={setMinLevel}>
            <SelectTrigger className="h-6 w-[130px] text-[11px] font-mono bg-background/50 border-border/60">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {LEVEL_ORDER.map(lvl => (
                <SelectItem key={lvl} value={lvl} className="text-[11px] font-mono">
                  <div className="flex items-center gap-2">
                    <div
                      className="h-2 w-2 rounded-full"
                      style={{ backgroundColor: LEVEL_COLORS[lvl as keyof typeof LEVEL_COLORS] ?? "#6b7280" }}
                    />
                    {lvl}
                  </div>
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          {/* Configuration Dropdown */}
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="outline" size="sm" className="h-6 px-2.5 text-[11px] bg-background/50 border-border/60">
                <Settings2 className="w-3 h-3 mr-0.5 opacity-70" />
                Settings
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start" className="w-56 font-mono text-[11px]">
              <DropdownMenuLabel className="text-[11px] font-semibold text-muted-foreground py-1.5">
                Log Sources
              </DropdownMenuLabel>
              {SOURCES.map(src => (
                <DropdownMenuCheckboxItem
                  key={src}
                  className="text-[11px] py-1.5"
                  checked={sourcesEnabled[src]}
                  onCheckedChange={() => toggleSource(src)}
                >
                  <div className="flex items-center gap-2">
                    <div
                      className="h-2 w-2 rounded-full"
                      style={{ backgroundColor: SOURCE_COLORS[src as keyof typeof SOURCE_COLORS] }}
                    />
                    {src}
                  </div>
                </DropdownMenuCheckboxItem>
              ))}
              <DropdownMenuSeparator />
              <DropdownMenuCheckboxItem
                className="text-[11px] py-1.5"
                checked={groupMode}
                onCheckedChange={setGroupMode}
              >
                Group correlated entries
              </DropdownMenuCheckboxItem>
            </DropdownMenuContent>
          </DropdownMenu>
        </div>

        <span className="text-[10px] text-muted-foreground/60 tabular-nums font-mono whitespace-nowrap">
          {filteredLogs.length} / {logs.length} entries
        </span>
      </div>

      {/* Log content */}
      <div
        ref={containerRef}
        onScroll={handleScroll}
        className="overflow-auto font-mono text-[11px] leading-relaxed px-4 py-3 bg-black/20 flex-1 min-h-0"
        style={{ width: 0, minWidth: "100%" }}
      >
          {groupedLogs.length === 0 ? (
            <div className="text-muted-foreground text-center pt-8">
              {logs.length === 0 ? "Waiting for log entries..." : "No entries match filters"}
            </div>
          ) : (
            groupedLogs.map((item, i) =>
              item.type === "single" ? (
                <LogLine key={i} entry={item.entry} />
              ) : (
                <LogGroupRow
                  key={`g-${item.messageId}`}
                  group={item}
                  expanded={expandedGroups.has(item.messageId)}
                  onToggle={() => toggleGroup(item.messageId)}
                />
              )
            )
          )}
        </div>
        <Button variant="secondary" size="sm" className="absolute bottom-2 right-4 h-8 w-8 p-0 rounded-full shadow-md opacity-50 hover:opacity-100 transition-opacity z-10 bg-background/80 backdrop-blur-sm" onClick={() => setIsFullscreen(v => !v)} title={isFullscreen ? "Exit Fullscreen" : "Fullscreen"}>
          {isFullscreen ? <Minimize2 className="h-4 w-4" /> : <Maximize2 className="h-4 w-4" />}
        </Button>
      </Card>
  )
}

//
// Page
//

export default function Diagnostics() {
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
