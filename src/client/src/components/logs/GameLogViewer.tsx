import React, { useState, useEffect, useMemo, useRef, useCallback } from "react"
import { ArrowDown, Check, Copy, Filter, Maximize2, Minimize2 } from "lucide-react"
import type { GameLogDTO, GameLogType, GameStateData } from "@/types/api"
import { TYPE_CONFIG, ALL_TYPES, TYPE_ORDER, renderData, formatDataAsText } from "@/utils/game-log-rendering"

interface GameLogViewerProps {
  logs: GameLogDTO[]
  loading?: boolean
  emptyMessage?: string
  noMatchMessage?: string
  timePrecision?: "seconds" | "milliseconds"
  maxHeightClassName?: string
  expanded?: boolean
  onExpandedChange?: (expanded: boolean) => void
}

type LogEntry = {
  id: number
  timestamp: string
  gameLogType: GameLogType
  data: string
  nonce: number
  key: string
  ts: Date
  deltaMs: number | null
}

function compareLogEntries(a: { nonce: number; gameLogType: string; ts: Date }, b: { nonce: number; gameLogType: string; ts: Date }): number {
  if (a.nonce !== 0 && b.nonce !== 0 && a.nonce === b.nonce) {
    const tsDiff = a.ts.getTime() - b.ts.getTime()
    if (tsDiff !== 0) return tsDiff
    const ta = TYPE_ORDER[a.gameLogType] ?? 6
    const tb = TYPE_ORDER[b.gameLogType] ?? 6
    return ta - tb
  }

  return a.ts.getTime() - b.ts.getTime()
}

function formatTime(d: Date, precision: "seconds" | "milliseconds"): string {
  const h = d.getHours().toString().padStart(2, "0")
  const m = d.getMinutes().toString().padStart(2, "0")
  const s = d.getSeconds().toString().padStart(2, "0")
  if (precision === "seconds") return `${h}:${m}:${s}`

  const ms = d.getMilliseconds().toString().padStart(3, "0")
  return `${h}:${m}:${s}.${ms}`
}

function formatDelta(ms: number | null): string {
  if (ms === null) return ""
  const abs = Math.abs(ms)
  const sign = ms < 0 ? "-" : "+"
  if (abs < 1000) return `${sign}${abs}ms`
  if (abs < 60000) return `${sign}${(abs / 1000).toFixed(1)}s`
  return `${sign}${Math.floor(abs / 60000)}m${Math.floor((abs % 60000) / 1000)}s`
}

function TypeFilterBar({ enabled, onToggle }: { enabled: Set<GameLogType>; onToggle: (type: GameLogType) => void }) {
  return (
    <div className="flex items-center gap-1.5 flex-wrap">
      <Filter className="h-3.5 w-3.5 text-muted-foreground mr-1" />
      {ALL_TYPES.map((type) => {
        const cfg = TYPE_CONFIG[type]
        const active = enabled.has(type)
        return (
          <button
            key={type}
            onClick={() => onToggle(type)}
            className={`px-2 py-0.5 rounded text-xs font-mono transition-all ${
              active
                ? `${cfg.bg} ${cfg.color} ring-1 ring-current/30`
                : "bg-muted/50 text-muted-foreground/50 hover:text-muted-foreground"
            }`}
          >
            {cfg.short}
          </button>
        )
      })}
    </div>
  )
}

function CopyLogButton({ entries, timePrecision }: { entries: LogEntry[]; timePrecision: "seconds" | "milliseconds" }) {
  const [copied, setCopied] = useState(false)

  const handleCopy = useCallback(() => {
    const lines: string[] = []
    let prevNonce = -1

    for (const entry of entries) {
      if (entry.nonce !== 0 && entry.nonce !== prevNonce && prevNonce !== -1) {
        lines.push("")
      }
      prevNonce = entry.nonce

      const time = formatTime(entry.ts, timePrecision)
      const cfg = TYPE_CONFIG[entry.gameLogType] ?? TYPE_CONFIG.LogMessage
      const tag = cfg.short.padEnd(6)
      const body = formatDataAsText(entry.gameLogType, entry.data)
      const bodyLines = body.split("\n")
      lines.push(`${time}  ${tag}  ${bodyLines[0]}`)
      for (let i = 1; i < bodyLines.length; i++) {
        lines.push(`${"".padEnd(time.length + 2)}${"".padEnd(8)}${bodyLines[i]}`)
      }
    }

    navigator.clipboard.writeText(lines.join("\n")).then(() => {
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    })
  }, [entries, timePrecision])

  return (
    <button
      onClick={handleCopy}
      className="ml-auto flex items-center gap-1 px-2 py-0.5 rounded text-xs text-muted-foreground hover:text-foreground hover:bg-muted/50 transition-colors shrink-0"
      title="Copy log to clipboard"
    >
      {copied ? <Check className="h-3 w-3" /> : <Copy className="h-3 w-3" />}
      {copied ? "Copied" : "Copy"}
    </button>
  )
}

function GameStateHeader({ entry, timePrecision }: { entry: LogEntry; timePrecision: "seconds" | "milliseconds" }) {
  try {
    const d: GameStateData = JSON.parse(entry.data)
    return (
      <tr className="bg-muted/30 border-t border-sidebar-border/60">
        <td colSpan={4} className="px-3 py-1.5">
          <div className="flex items-center gap-3 text-[11px]">
            <span className="text-muted-foreground font-mono">{formatTime(entry.ts, timePrecision)}</span>
            <span className="font-semibold text-blue-400">Turn {d.turn}</span>
            <span className="text-blue-300/80">{d.phase}</span>
            {d.previousTurn != null && (
              <span className="text-muted-foreground/50 text-[10px]">
                (from Turn {d.previousTurn} {d.previousPhase})
              </span>
            )}
          </div>
        </td>
      </tr>
    )
  } catch {
    return null
  }
}

function NonceSeparator() {
  return (
    <tr aria-hidden="true">
      <td colSpan={4} className="py-0">
        <div className="h-px bg-sidebar-border/60" />
      </td>
    </tr>
  )
}

function LogRow({
  entry,
  expanded,
  onToggle,
  timePrecision,
}: {
  entry: LogEntry
  expanded: boolean
  onToggle: () => void
  timePrecision: "seconds" | "milliseconds"
}) {
  const cfg = TYPE_CONFIG[entry.gameLogType] ?? TYPE_CONFIG.LogMessage

  return (
    <tr className="hover:bg-muted/50 transition-colors cursor-pointer group" onClick={onToggle}>
      <td className="w-[110px] shrink-0 px-3 py-1.5 text-muted-foreground align-top select-none font-mono text-[11px] whitespace-nowrap">
        {formatTime(entry.ts, timePrecision)}
      </td>
      <td className="w-[70px] shrink-0 px-2 py-1.5 text-muted-foreground/60 align-top select-none font-mono text-[10px] whitespace-nowrap text-right">
        {formatDelta(entry.deltaMs)}
      </td>
      <td className="w-[70px] shrink-0 px-2 py-1.5 align-top">
        <span className={`inline-block px-1.5 py-0.5 rounded text-[10px] font-mono font-semibold ${cfg.color} ${cfg.bg}`}>
          {cfg.short}
        </span>
      </td>
      <td className="px-3 py-1.5 break-words text-[12px] leading-relaxed align-top">
        {renderData(entry.gameLogType, entry.data)}
        {expanded && (
          <pre className="mt-1.5 p-2 rounded bg-muted/50 text-muted-foreground text-[10px] leading-snug overflow-x-auto max-w-full whitespace-pre-wrap border border-sidebar-border/60">
            {(() => {
              try {
                return JSON.stringify(JSON.parse(entry.data), null, 2)
              } catch {
                return entry.data
              }
            })()}
          </pre>
        )}
      </td>
    </tr>
  )
}

export function GameLogViewer({
  logs,
  loading = false,
  emptyMessage = "No game logs recorded for this game.",
  noMatchMessage = "No events match the current filters.",
  timePrecision = "milliseconds",
  maxHeightClassName = "max-h-[600px]",
  expanded = false,
  onExpandedChange,
}: GameLogViewerProps) {
  const [enabledTypes, setEnabledTypes] = useState<Set<GameLogType>>(new Set(ALL_TYPES))
  const [expandedRows, setExpandedRows] = useState<Set<string>>(new Set())
  const [autoScroll, setAutoScroll] = useState(true)
  const scrollRef = useRef<HTMLDivElement>(null)

  const entries = useMemo<LogEntry[]>(() => {
    const result = logs.map((log, idx) => {
      const ts = new Date(log.timestamp ?? Date.now())
      return {
        id: log.id ?? 0,
        timestamp: log.timestamp ?? "",
        gameLogType: (log.gameLogType ?? "LogMessage") as GameLogType,
        data: log.data ?? "",
        nonce: log.nonce ?? 0,
        key: `${log.id ?? 0}-${log.timestamp ?? ""}-${idx}`,
        ts,
        deltaMs: null,
      }
    })

    result.sort(compareLogEntries)
    for (let i = 0; i < result.length; i++) {
      result[i] = {
        ...result[i],
        deltaMs: i > 0 ? result[i].ts.getTime() - result[i - 1].ts.getTime() : null,
      }
    }

    return result
  }, [logs])

  const filtered = useMemo(() => {
    const result = entries.filter(e => enabledTypes.has(e.gameLogType))
    for (let i = 0; i < result.length; i++) {
      result[i] = {
        ...result[i],
        deltaMs: i > 0 ? result[i].ts.getTime() - result[i - 1].ts.getTime() : null,
      }
    }
    return result
  }, [entries, enabledTypes])

  useEffect(() => {
    if (autoScroll && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight
    }
  }, [filtered, autoScroll])

  const toggleType = useCallback((type: GameLogType) => {
    setEnabledTypes(prev => {
      const next = new Set(prev)
      if (next.has(type)) next.delete(type)
      else next.add(type)
      return next
    })
  }, [])

  const toggleExpand = useCallback((rowKey: string) => {
    setExpandedRows(prev => {
      const next = new Set(prev)
      if (next.has(rowKey)) next.delete(rowKey)
      else next.add(rowKey)
      return next
    })
  }, [])

  const handleScroll = useCallback(() => {
    if (!scrollRef.current) return
    const { scrollTop, scrollHeight, clientHeight } = scrollRef.current
    const atBottom = scrollTop + clientHeight >= scrollHeight - 40
    setAutoScroll(atBottom)
  }, [])

  return (
    <div className="relative flex min-h-0 w-full flex-1 flex-col overflow-hidden rounded-md border border-sidebar-border/60 bg-card font-mono text-xs">
      <div className="flex shrink-0 items-center gap-3 border-b border-sidebar-border/60 bg-muted/30 px-3 py-2">
        <TypeFilterBar enabled={enabledTypes} onToggle={toggleType} />
        <CopyLogButton entries={filtered} timePrecision={timePrecision} />
      </div>

      <div
        ref={scrollRef}
        onScroll={handleScroll}
        className={`min-h-0 ${maxHeightClassName} overflow-y-auto overflow-x-hidden`}
      >
        {filtered.length === 0 ? (
          <div className="text-muted-foreground italic text-center py-6 px-4 bg-muted/20">
            {entries.length === 0 ? (loading ? "Loading game events..." : emptyMessage) : noMatchMessage}
          </div>
        ) : (
          <table className="w-full">
            <thead className="sticky top-0 z-10 bg-background">
              <tr className="bg-background border-b border-sidebar-border/60 text-[10px] text-muted-foreground font-semibold tracking-wider uppercase">
                <th className="w-[110px] px-3 py-1.5 text-left bg-background">Time</th>
                <th className="w-[70px] px-2 py-1.5 text-right bg-background">Delta</th>
                <th className="w-[70px] px-2 py-1.5 text-left bg-background">Type</th>
                <th className="px-3 py-1.5 text-left bg-background">Data</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((entry, idx) => {
                const prevNonce = idx > 0 ? filtered[idx - 1].nonce : entry.nonce
                const nonceChanged = idx > 0 && entry.nonce !== 0 && entry.nonce !== prevNonce

                if (entry.gameLogType === "GameState") {
                  return <GameStateHeader key={entry.key} entry={entry} timePrecision={timePrecision} />
                }

                return (
                  <React.Fragment key={entry.key}>
                    {nonceChanged && <NonceSeparator />}
                    <LogRow
                      entry={entry}
                      expanded={expandedRows.has(entry.key)}
                      onToggle={() => toggleExpand(entry.key)}
                      timePrecision={timePrecision}
                    />
                  </React.Fragment>
                )
              })}
            </tbody>
          </table>
        )}
      </div>

      {!autoScroll && filtered.length > 0 && (
        <button
          onClick={() => {
            setAutoScroll(true)
            if (scrollRef.current) {
              scrollRef.current.scrollTop = scrollRef.current.scrollHeight
            }
          }}
          className="absolute bottom-3 right-4 flex items-center gap-1.5 px-3 py-1.5 rounded-full bg-secondary border border-sidebar-border/60 text-secondary-foreground text-xs font-medium shadow-lg hover:bg-accent transition-colors z-20"
        >
          <ArrowDown className="h-3 w-3" />
          Resume auto-scroll
        </button>
      )}

      {onExpandedChange ? (
        <button
          type="button"
          onClick={() => onExpandedChange(!expanded)}
          className="absolute bottom-3 right-4 z-20 flex h-8 w-8 items-center justify-center rounded-full border border-sidebar-border/60 bg-background/80 text-muted-foreground shadow-md backdrop-blur-sm opacity-50 transition-opacity hover:bg-background hover:text-foreground hover:opacity-100"
          title={expanded ? "Restore details" : "Expand log"}
          aria-label={expanded ? "Restore game details" : "Expand game log"}
        >
          {expanded ? <Minimize2 className="h-4 w-4" /> : <Maximize2 className="h-4 w-4" />}
        </button>
      ) : null}
    </div>
  )
}
