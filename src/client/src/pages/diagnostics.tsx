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
  Treemap,
} from "recharts"
import { Cpu, Zap, Server, Settings2, Filter, Search, Camera, Trash2, Maximize2, Minimize2 } from "lucide-react"
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
// Heap Analysis (3-level drill-down with longitudinal growth tracking)
//

interface TypeStatsEntry {
  typeName: string
  count: number
  totalSize: number
  gen0Count: number
  gen1Count: number
  gen2Count: number
  lohCount: number
  gen0Size: number
  gen1Size: number
  gen2Size: number
  lohSize: number
}

interface HeapSnapshot {
  types: TypeStatsEntry[]
  totalHeapSize: number
  totalObjectCount: number
}

interface StaticHolderEntry {
  holderType: string
  fieldName: string
  rootAddress: number
  rootTypeName: string
  retainedBytes: number
  objectCount: number
  dominantChildType: string
  dominantChildSize: number
}

interface StaticHoldersSnapshot {
  holders: StaticHolderEntry[]
  totalStaticRoots: number
  totalRetainedBytes: number
}

interface TimestampedSnapshot {
  timestamp: number
  data: HeapSnapshot
  holders: StaticHoldersSnapshot | null
}

interface TypeInstanceEntry {
  address: number
  size: number
  generation: number
}

interface RetainPathEntry {
  address: number
  typeName: string
  size: number
  fieldName: string | null
}

function fmtSize(bytes: number): string {
  if (bytes >= 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`
  if (bytes >= 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${bytes} B`
}

function fmtAddr(addr: number): string {
  return `0x${addr.toString(16).toUpperCase()}`
}

function computeGrowthRate(
  history: TimestampedSnapshot[],
  typeName: string,
  field: "count" | "totalSize"
): number {
  if (history.length < 2) return 0
  const first = history[0]
  const last = history[history.length - 1]
  const elapsedMin = (last.timestamp - first.timestamp) / 60000
  if (elapsedMin < 0.1) return 0

  const firstVal = first.data.types.find(t => t.typeName === typeName)?.[field] ?? 0
  const lastVal = last.data.types.find(t => t.typeName === typeName)?.[field] ?? 0
  return (lastVal - firstVal) / elapsedMin
}

type HeapView =
  | { level: "types" }
  | { level: "instances"; typeName: string }
  | { level: "retainChain"; typeName: string }

function shortTypeName(name: string): string {
  return name
    .replace(/System\./g, "")
    .replace(/Collections\.(Generic|Concurrent|Specialized)\./g, "")
    .replace(/WotC\.MtGO\.Client\.Model\./g, "")
    .replace(/[A-Za-z0-9_]+\./g, "")
}

function computeDelta(
  history: TimestampedSnapshot[],
  typeName: string,
): number {
  if (history.length < 2) return 0
  const firstSize = history[0].data.types.find(t => t.typeName === typeName)?.totalSize ?? 0
  const lastSize = history[history.length - 1].data.types.find(t => t.typeName === typeName)?.totalSize ?? 0
  return lastSize - firstSize
}

function growthColor(growth: number, maxGrowth: number, hasHistory: boolean): { fill: string; opacity: number } {
  if (!hasHistory) return { fill: "#64748b", opacity: 0.15 }
  if (growth > 0) {
    const intensity = Math.min(growth / Math.max(maxGrowth, 1), 1)
    return { fill: "#ef4444", opacity: 0.15 + intensity * 0.45 }
  }
  if (growth < 0) return { fill: "#10b981", opacity: 0.2 }
  return { fill: "#64748b", opacity: 0.1 }
}

function HeapTreemapCell(props: any) {
  const { x, y, width, height, depth, name, fullName, actualSize, growth, maxGrowth, hasHistory, onClick } = props
  if (!depth || !width || !height || width < 2 || height < 2) return null
  const { fill, opacity } = growthColor(growth ?? 0, maxGrowth ?? 1, hasHistory ?? false)
  const clipId = `heap-cell-${Math.round(x)}-${Math.round(y)}`
  return (
    <g
      onClick={(e) => { e.stopPropagation(); onClick?.(fullName) }}
      style={{ cursor: "pointer" }}
    >
      <defs>
        <clipPath id={clipId}>
          <rect x={x} y={y} width={width} height={height} rx={2} />
        </clipPath>
      </defs>
      <rect
        x={x} y={y} width={width} height={height}
        fill={fill} fillOpacity={opacity}
        stroke="hsl(var(--border))" strokeWidth={1} rx={2}
      />
      <g clipPath={`url(#${clipId})`} style={{ pointerEvents: "none" }}>
        {width > 55 && height > 24 && (
          <text x={x + 4} y={y + 13} fill="currentColor"
            fontSize={10} fontFamily="ui-monospace, monospace">
            {name}
          </text>
        )}
        {width > 55 && height > 38 && (
          <text x={x + 4} y={y + 25} fill="currentColor"
            fontSize={9} opacity={0.5} fontFamily="ui-monospace, monospace">
            {fmtSize(actualSize ?? 0)}
          </text>
        )}
        {width > 55 && height > 50 && hasHistory && growth !== 0 && (
          <text x={x + 4} y={y + 37} fontSize={9} fontFamily="ui-monospace, monospace"
            fill={growth > 0 ? "#ef4444" : "#10b981"}>
            {growth > 0 ? "\u25B2" : "\u25BC"} {fmtSize(Math.abs(growth))}/min
          </text>
        )}
      </g>
    </g>
  )
}

function HeapTooltip({ active, payload }: any) {
  if (!active || !payload?.[0]) return null
  const d = payload[0].payload
  const genRows: [string, number, number][] = [
    ["Gen0", d.gen0Count ?? 0, d.gen0Size ?? 0],
    ["Gen1", d.gen1Count ?? 0, d.gen1Size ?? 0],
    ["Gen2", d.gen2Count ?? 0, d.gen2Size ?? 0],
    ["LOH",  d.lohCount  ?? 0, d.lohSize  ?? 0],
  ]
  return (
    <div className="rounded-lg border bg-background p-3 shadow-lg text-[11px] max-w-[400px]">
      <div className="font-mono font-medium mb-1.5 break-all">{d.fullName}</div>
      <div className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-0.5 tabular-nums">
        <span className="text-muted-foreground">Count</span>
        <span className="text-right">{d.count?.toLocaleString()}</span>
        <span className="text-muted-foreground">Total Size</span>
        <span className="text-right">{fmtSize(d.actualSize ?? d.size)}</span>
      </div>
      <div className="border-t border-border/30 my-1.5" />
      <div className="grid grid-cols-[auto_1fr_1fr] gap-x-3 gap-y-0.5 tabular-nums">
        {genRows.map(([label, count, size]) => (
          <React.Fragment key={label}>
            <span className="text-muted-foreground">{label}</span>
            <span className="text-right">{count.toLocaleString()}</span>
            <span className="text-right opacity-60">{fmtSize(size)}</span>
          </React.Fragment>
        ))}
      </div>
      {(d.growth != null && d.growth !== 0 || d.delta != null && d.delta !== 0) && (
        <>
          <div className="border-t border-border/30 my-1.5" />
          <div className="grid grid-cols-[auto_1fr] gap-x-4 gap-y-0.5 tabular-nums">
            {d.growth != null && d.growth !== 0 && (
              <>
                <span className="text-muted-foreground">Growth</span>
                <span className="text-right" style={{ color: d.growth > 0 ? "#ef4444" : "#10b981" }}>
                  {d.growth > 0 ? "\u25B2" : "\u25BC"} {fmtSize(Math.abs(d.growth))}/min
                </span>
              </>
            )}
            {d.delta != null && d.delta !== 0 && (
              <>
                <span className="text-muted-foreground">Delta</span>
                <span className="text-right" style={{ color: d.delta > 0 ? "#ef4444" : "#10b981" }}>
                  {d.delta > 0 ? "+" : ""}{fmtSize(Math.abs(d.delta))} since first
                </span>
              </>
            )}
          </div>
        </>
      )}
    </div>
  )
}

type HolderRow = StaticHolderEntry & {
  baselineBytes: number
  deltaBytes: number
  hasBaseline: boolean
}

type HolderSortKey = "retained" | "count" | "delta"

function HoldersTable({
  holders,
  hasBaseline,
  onHolderClick,
}: {
  holders: HolderRow[]
  hasBaseline: boolean
  onHolderClick: (typeName: string) => void
}) {
  const [sortKey, setSortKey] = useState<HolderSortKey>("retained")
  const [sortDesc, setSortDesc] = useState(true)

  const sorted = useMemo(() => {
    const rows = [...holders]
    const dir = sortDesc ? -1 : 1
    rows.sort((a, b) => {
      let av: number, bv: number
      switch (sortKey) {
        case "count":    av = a.objectCount;  bv = b.objectCount;  break
        case "delta":    av = a.deltaBytes;   bv = b.deltaBytes;   break
        case "retained":
        default:         av = a.retainedBytes; bv = b.retainedBytes; break
      }
      if (av === bv) return 0
      return av < bv ? -dir : dir
    })
    return rows
  }, [holders, sortKey, sortDesc])

  const onSort = (key: HolderSortKey) => {
    if (key === sortKey) setSortDesc(d => !d)
    else { setSortKey(key); setSortDesc(true) }
  }

  const sortArrow = (key: HolderSortKey) =>
    sortKey === key ? (sortDesc ? " \u25BC" : " \u25B2") : ""

  if (holders.length === 0) {
    return (
      <div className="text-[11px] text-muted-foreground/50 py-6 text-center">
        No static holders found. Take a snapshot first.
      </div>
    )
  }
  return (
    <div className="max-h-[420px] overflow-auto border border-border/30 rounded">
      <table className="w-full text-[11px] tabular-nums">
        <thead className="bg-muted/50 sticky top-0 z-10">
          <tr className="border-b border-border/30 text-muted-foreground">
            <th className="text-left font-semibold h-6 py-1 px-2">Holder.Field</th>
            <th
              className="text-right font-semibold h-6 py-1 px-2 w-24 cursor-pointer hover:text-foreground select-none"
              onClick={() => onSort("retained")}
            >Retained{sortArrow("retained")}</th>
            <th
              className="text-right font-semibold h-6 py-1 px-2 w-20 cursor-pointer hover:text-foreground select-none"
              onClick={() => onSort("count")}
            >Count{sortArrow("count")}</th>
            <th className="text-left  font-semibold h-6 py-1 px-2">Dominant Child</th>
            {hasBaseline && (
              <th
                className="text-right font-semibold h-6 py-1 px-2 w-24 cursor-pointer hover:text-foreground select-none"
                onClick={() => onSort("delta")}
              >Δ{sortArrow("delta")}</th>
            )}
          </tr>
        </thead>
        <tbody>
          {sorted.map((h, i) => {
            const key = `${h.holderType}.${h.fieldName}`
            const isLeak = h.hasBaseline && h.deltaBytes > 1024 * 1024
            const isShrinking = h.hasBaseline && h.deltaBytes < -1024 * 1024
            return (
              <tr
                key={key + i}
                className="border-b border-border/10 hover:bg-muted/30 transition-colors cursor-pointer"
                onClick={() => onHolderClick(h.rootTypeName)}
              >
                <td className="py-1 px-2 font-mono">
                  <span className="text-muted-foreground">{shortTypeName(h.holderType)}</span>
                  <span className="text-indigo-400">.{h.fieldName}</span>
                </td>
                <td className="py-1 px-2 text-right font-mono">
                  {fmtSize(h.retainedBytes)}
                </td>
                <td className="py-1 px-2 text-right font-mono opacity-60">
                  {h.objectCount.toLocaleString()}
                </td>
                <td className="py-1 px-2 font-mono opacity-60 truncate max-w-[200px]" title={h.dominantChildType}>
                  {shortTypeName(h.dominantChildType ?? "?")}
                  <span className="ml-1 opacity-50">({fmtSize(h.dominantChildSize)})</span>
                </td>
                {hasBaseline && (
                  <td className="py-1 px-2 text-right font-mono" style={{
                    color: isLeak ? "#ef4444" : isShrinking ? "#10b981" : undefined,
                  }}>
                    {h.deltaBytes === 0
                      ? <span className="opacity-30">—</span>
                      : <>{h.deltaBytes > 0 ? "▲ +" : "▼ "}{fmtSize(Math.abs(h.deltaBytes))}</>}
                  </td>
                )}
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

function HeapAnalysis() {
  const [history, setHistory] = useState<TimestampedSnapshot[]>([])
  const [loading, setLoading] = useState(false)
  const [loadingDetail, setLoadingDetail] = useState<string | null>(null)
  const [view, setView] = useState<HeapView>({ level: "types" })
  const [instances, setInstances] = useState<TypeInstanceEntry[]>([])
  const [retainChain, setRetainChain] = useState<RetainPathEntry[]>([])
  const [retainSampleAddr, setRetainSampleAddr] = useState<number>(0)
  const [typeFilter, setTypeFilter] = useState("")
  const [viewMode, setViewMode] = useState<"size" | "growth" | "holders">("size")
  const [growingOnly, setGrowingOnly] = useState(false)

  const latest = history.length > 0 ? history[history.length - 1].data : null
  const hasHistory = history.length >= 2

  const takeSnapshot = async () => {
    setLoading(true)
    try {
      // Fire snapshot + holder analysis in parallel
      const [snapRes, holdersRes] = await Promise.all([
        fetch("/api/Diagnostics/HeapSnapshot?topN=200"),
        fetch("/api/Diagnostics/StaticHolders?topN=100"),
      ])
      if (!snapRes.ok) return
      const data: HeapSnapshot = await snapRes.json()
      const holders: StaticHoldersSnapshot | null = holdersRes.ok
        ? await holdersRes.json()
        : null
      setHistory(prev => {
        const next = [...prev, { timestamp: Date.now(), data, holders }]
        return next.length > 20 ? next.slice(-20) : next
      })
      setView({ level: "types" })
    } finally {
      setLoading(false)
    }
  }

  const loadInstances = async (typeName: string) => {
    setLoadingDetail("Loading instances...")
    try {
      const res = await fetch(`/api/Diagnostics/TypeInstances?typeName=${encodeURIComponent(typeName)}&maxCount=20`)
      if (!res.ok) return
      const data = await res.json()
      setInstances(data.instances ?? [])
      setView({ level: "instances", typeName })
    } finally {
      setLoadingDetail(null)
    }
  }

  const loadRetainChain = async (typeName: string) => {
    setLoadingDetail("Building retain chain (may take a while)...")
    setView({ level: "retainChain", typeName })
    try {
      const res = await fetch(`/api/Diagnostics/RetainChain?typeName=${encodeURIComponent(typeName)}&maxDepth=8`)
      if (!res.ok) {
        setRetainChain([])
        setRetainSampleAddr(0)
        return
      }
      const data = await res.json()
      setRetainChain(data.chain ?? [])
      setRetainSampleAddr(data.sampleAddress ?? 0)
    } finally {
      setLoadingDetail(null)
    }
  }

  const sortedTypes = useMemo(() => {
    if (!latest) return []
    const lowerFilter = typeFilter.toLowerCase()
    return [...latest.types]
      .filter(t => !lowerFilter || t.typeName.toLowerCase().includes(lowerFilter))
      .sort((a, b) => b.totalSize - a.totalSize)
  }, [latest, typeFilter])

  const treemapData = useMemo(() => {
    const mapped = sortedTypes.map(t => {
      const growth = computeGrowthRate(history, t.typeName, "totalSize")
      const delta = computeDelta(history, t.typeName)
      return {
        name: shortTypeName(t.typeName),
        fullName: t.typeName,
        size: viewMode === "growth" ? Math.max(growth, 0) || 0.001 : t.totalSize,
        actualSize: t.totalSize,
        count: t.count,
        gen0Count: t.gen0Count, gen0Size: t.gen0Size,
        gen1Count: t.gen1Count, gen1Size: t.gen1Size,
        gen2Count: t.gen2Count, gen2Size: t.gen2Size,
        lohCount: t.lohCount, lohSize: t.lohSize,
        growth,
        delta,
      }
    })
    if (viewMode === "growth") return mapped.filter(t => t.growth > 0)
    if (growingOnly) return mapped.filter(t => t.growth > 0)
    return mapped
  }, [sortedTypes, history, viewMode, growingOnly])

  const maxGrowth = useMemo(() =>
    Math.max(...treemapData.map(t => t.growth), 0) || 1
  , [treemapData])

  // Holders: compute delta between baseline (first snapshot) and latest
  const holdersData = useMemo(() => {
    const latestHolders = history.length > 0
      ? history[history.length - 1].holders
      : null
    if (!latestHolders) return []

    const baselineHolders = history.length >= 2 ? history[0].holders : null
    const baselineMap = new Map<string, StaticHolderEntry>()
    if (baselineHolders) {
      for (const h of baselineHolders.holders) {
        baselineMap.set(`${h.holderType}.${h.fieldName}`, h)
      }
    }

    const lowerFilter = typeFilter.toLowerCase()
    return latestHolders.holders
      .filter(h => {
        if (!lowerFilter) return true
        const key = `${h.holderType}.${h.fieldName}`.toLowerCase()
        return key.includes(lowerFilter) ||
               (h.dominantChildType?.toLowerCase().includes(lowerFilter) ?? false)
      })
      .map(h => {
        const baseline = baselineMap.get(`${h.holderType}.${h.fieldName}`)
        return {
          ...h,
          baselineBytes: baseline?.retainedBytes ?? 0,
          deltaBytes: baseline ? h.retainedBytes - baseline.retainedBytes : 0,
          hasBaseline: !!baseline,
        }
      })
  }, [history, typeFilter])

  const growingHolderCount = useMemo(() =>
    holdersData.filter(h => h.hasBaseline && h.deltaBytes > 1024 * 1024).length
  , [holdersData])

  // Breadcrumb
  const breadcrumb = (
    <div className="flex items-center gap-1 text-[11px] text-muted-foreground mb-2">
      <span
        className={view.level === "types" ? "text-foreground" : "cursor-pointer hover:text-foreground"}
        onClick={() => setView({ level: "types" })}
      >
        Types
      </span>
      {view.level !== "types" && (
        <>
          <span className="opacity-50">/</span>
          <span
            className={view.level === "instances" ? "text-foreground" : "cursor-pointer hover:text-foreground"}
            onClick={() => loadInstances(view.typeName)}
          >
            {view.typeName.split(".").pop()}
          </span>
        </>
      )}
      {view.level === "retainChain" && (
        <>
          <span className="opacity-50">/</span>
          <span className="text-foreground">Retain Chain</span>
        </>
      )}
    </div>
  )

  return (
    <Card className="border-sidebar-border/60">
      <CardHeader className="flex flex-row items-center justify-between space-y-0 p-4 pb-2">
        <CardTitle className="text-sm font-medium text-muted-foreground">
          Heap Analysis
        </CardTitle>
        <div className="flex items-center gap-2">
          {history.length > 0 && (
            <span className="text-[10px] text-muted-foreground/60 tabular-nums">
              {history.length} snapshot{history.length !== 1 ? "s" : ""}
              {latest && ` · ${fmtSize(latest.totalHeapSize)} · ${latest.totalObjectCount.toLocaleString()} objects`}
            </span>
          )}
          <Button variant="outline" size="sm" className="h-6 px-2.5 text-[11px] bg-muted/40 hover:bg-muted/80" onClick={takeSnapshot} disabled={loading}>
            <Camera className="w-3 h-3 mr-0.5 opacity-70" />
            {loading ? "Scanning..." : "Snapshot"}
          </Button>
          {history.length > 0 && (
            <Button variant="ghost" size="sm" className="h-6 w-6 p-0 text-muted-foreground hover:text-destructive transition-colors text-[11px]" onClick={() => setHistory([])} title="Clear history">
              <Trash2 className="w-3 h-3" />
            </Button>
          )}
        </div>
      </CardHeader>
      <CardContent className="p-4 pt-0">
        {loadingDetail && (
          <div className="text-[11px] text-muted-foreground text-center py-3 animate-pulse">
            {loadingDetail}
          </div>
        )}
        {!latest ? (
          <div className="text-[11px] text-muted-foreground text-center py-6">
            Click "Snapshot" to analyze the managed heap
          </div>
        ) : (
          <>
            {breadcrumb}

            {/* Level 1: Treemap or Holders */}
            {view.level === "types" && (
              <div className="space-y-3">
                {/* Toolbar */}
                <div className="flex flex-col sm:flex-row items-center justify-between gap-3 bg-muted/20 px-3 py-2 rounded-md border border-border/40">
                  <div className="flex items-center gap-3">
                    <div className="flex items-center rounded-md bg-muted/50 p-0.5 border border-border/40">
                      <Button
                        variant={viewMode === "size" ? "secondary" : "ghost"}
                        size="sm"
                        className="h-5 px-2 text-[11px]" onClick={() => setViewMode("size")}
                      >
                        Size
                      </Button>
                      {hasHistory && (
                        <Button
                          variant={viewMode === "growth" ? "secondary" : "ghost"}
                          size="sm"
                          className="h-5 px-2 text-[11px]" onClick={() => setViewMode("growth")}
                        >
                          Growth
                        </Button>
                      )}
                      <Button
                        variant={viewMode === "holders" ? "secondary" : "ghost"}
                        size="sm"
                        className="h-5 px-2 text-[11px]" onClick={() => setViewMode("holders")}
                      >
                        Holders
                      </Button>
                    </div>

                    <div className="w-px h-5 bg-border/40" />

                    <div className="relative w-48 sm:w-64">
                      <Search className="absolute left-2 top-1/2 -translate-y-1/2 h-3 w-3 text-muted-foreground/60" />
                      <Input
                        value={typeFilter}
                        onChange={e => setTypeFilter(e.target.value)}
                        placeholder={viewMode === "holders" ? "Filter holders..." : "Filter types..."}
                        className="h-6 pl-7 text-[10px] font-mono bg-background/50 focus-visible:ring-1 focus-visible:ring-ring border-border/60"
                      />
                    </div>
                  </div>

                  <div className="flex items-center justify-end gap-3 flex-1 sm:flex-initial">
                    {hasHistory && viewMode === "size" && (
                      <Button
                        variant={growingOnly ? "default" : "outline"}
                        size="sm"
                        className="h-6 px-3 text-[11px]"
                        onClick={() => setGrowingOnly(g => !g)}
                      >
                        Growing Only
                      </Button>
                    )}
                    <span className="text-[10px] text-muted-foreground/60 tabular-nums whitespace-nowrap">
                      {viewMode === "holders"
                        ? `${holdersData.length} holders${hasHistory && growingHolderCount > 0 ? ` · ${growingHolderCount} growing` : ""}`
                        : `${treemapData.length} types`}
                    </span>
                  </div>
                </div>

                {viewMode !== "holders" && (
                  <div style={{ height: 300 }}>
                    <ResponsiveContainer width="100%" height="100%">
                      <Treemap
                        data={treemapData}
                        dataKey="size"
                        aspectRatio={4/3}
                        isAnimationActive={false}
                        content={<HeapTreemapCell
                          maxGrowth={maxGrowth}
                          hasHistory={hasHistory}
                          onClick={(name: string) => {
                            const t = sortedTypes.find(t => t.typeName === name)
                            if (t) loadInstances(t.typeName)
                          }}
                        />}
                      >
                        <Tooltip content={<HeapTooltip />} />
                      </Treemap>
                    </ResponsiveContainer>
                  </div>
                )}
                {viewMode === "holders" && (
                  <HoldersTable
                    holders={holdersData}
                    hasBaseline={hasHistory}
                    onHolderClick={loadInstances}
                  />
                )}
              </div>
            )}

            {/* Level 2: Instance List */}
            {view.level === "instances" && (
              <div className="flex flex-col gap-3">
                <div className="flex items-center justify-between text-[11px] text-muted-foreground bg-muted/20 px-3 py-2 rounded-md border border-border/40">
                  <span>
                    {instances.length} instances of <span className="font-mono text-foreground font-medium">{view.typeName}</span>
                  </span>
                  <Button variant="secondary" size="sm" className="h-6 text-[10px] px-3 font-medium"
                    onClick={() => loadRetainChain(view.typeName)}>
                    Explore Retain Chain
                  </Button>
                </div>
                <div className="max-h-[300px] overflow-auto border border-border/30 rounded-md">
                  <table className="w-full text-[11px] tabular-nums">
                  <thead className="bg-muted/50 sticky top-0 z-10">
                    <tr className="border-b border-border/30 text-muted-foreground">
                      <th className="text-left font-semibold h-6 py-1 px-2">Address</th>
                      <th className="text-right font-semibold h-6 py-1 px-2 w-20">Size</th>
                      <th className="text-right font-semibold h-6 py-1 px-2 w-14">Gen</th>
                    </tr>
                  </thead>
                  <tbody>
                    {instances.map(inst => (
                      <tr
                        key={inst.address}
                        className="border-b border-border/10 hover:bg-muted/30 transition-colors"
                      >
                        <td className="py-1 px-2 font-mono">{fmtAddr(inst.address)}</td>
                        <td className="py-1 px-2 text-right font-mono">{fmtSize(inst.size)}</td>
                        <td className="py-1 px-2 text-right font-mono" style={{
                          color: inst.generation >= 2 ? "#eab308" : undefined
                        }}>
                          {inst.generation >= 0 ? `Gen${inst.generation}` : "?"}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              </div>
            )}

            {/* Level 3: Retain Chain */}
            {view.level === "retainChain" && (
              <div className="flex flex-col gap-3">
                <div className="text-[11px] text-muted-foreground bg-muted/20 px-3 py-2 rounded-md border border-border/40">
                  <span>
                    Retain chain for <span className="font-mono text-foreground font-medium">{view.typeName}</span>
                  </span>
                  {retainSampleAddr !== 0 && (
                    <span className="ml-1.5 opacity-60">sample {fmtAddr(retainSampleAddr)}</span>
                  )}
                </div>
                <div className="max-h-[300px] overflow-auto border border-border/30 rounded-md p-3">
                  {retainChain.length === 0 ? (
                    <div className="text-[11px] text-muted-foreground/50 py-4 text-center">
                      No retain chain found (object may have been collected)
                    </div>
                ) : (
                  <div className="font-mono text-[11px] space-y-px">
                    {retainChain.map((entry, i) => (
                      <div key={i} className="whitespace-nowrap" style={{ paddingLeft: i * 12 }}>
                        {i > 0 && <span className="text-muted-foreground/40 mr-1">{"\u2514"}</span>}
                        <span className="text-muted-foreground/60">{entry.typeName}</span>
                        {entry.fieldName && (
                          <span className="text-indigo-400">.{entry.fieldName}</span>
                        )}
                        <span className="text-muted-foreground/40 ml-1.5">
                          {fmtSize(entry.size)}
                        </span>
                      </div>
                    ))}
                  </div>
                )}
              </div>
              </div>
            )}
          </>
        )}
      </CardContent>
    </Card>
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

      {/* Heap Analysis — full width below both columns */}
      <HeapAnalysis />

      {/* Unified Log — full width below both columns */}
      <UnifiedLog />
    </div>
  )
}
