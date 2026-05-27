import React, { useState, useEffect, useRef, useCallback, useMemo } from "react"
import { useParams, useNavigate } from "react-router-dom"
import { getApiUrl } from "@/utils/api-config"
import { useClientState } from "@/hooks/use-client-state"
import { useNDJSONStream } from "@/hooks/use-ndjson-stream"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { ArrowLeft, ArrowDown, Filter, Copy, Check } from "lucide-react"
import type { GameLogDTO, GameLogType, GameStateData } from "@/types/api"
import { TYPE_CONFIG, ALL_TYPES, TYPE_ORDER, renderData, formatDataAsText } from "@/utils/game-log-rendering"
import { useMatchDetails } from "./match-details"

// -- Types --

interface LogEntry {
  id: number
  gameId: number
  timestamp: string
  gameLogType: GameLogType
  data: string
  nonce: number
  /** Monotonic index for keying */
  seq: number
  /** Parsed timestamp as Date */
  ts: Date
  /** Delta from previous entry in ms */
  deltaMs: number | null
}

// -- Constants --

/**
 * Sort comparator for log entries that respects nonce grouping.
 * - Primary key: timestamp (chronological order across ticks)
 * - Within the same nonce AND same timestamp: type priority as tiebreaker
 *   so GameState headers precede zone/card/player changes from the same
 *   snapshot tick.
 */
function compareLogEntries(
  a: { nonce: number; gameLogType: string; ts: Date },
  b: { nonce: number; gameLogType: string; ts: Date },
): number {
  // Same nonce — timestamp first, type priority only as tiebreaker.
  // This ensures "entering state" events (zones from the snapshot at the
  // state's timestamp) sort before "during state" events (player actions
  // with later timestamps).
  if (a.nonce !== 0 && b.nonce !== 0 && a.nonce === b.nonce) {
    const tsDiff = a.ts.getTime() - b.ts.getTime()
    if (tsDiff !== 0) return tsDiff
    const ta = TYPE_ORDER[a.gameLogType] ?? 6
    const tb = TYPE_ORDER[b.gameLogType] ?? 6
    return ta - tb
  }

  return a.ts.getTime() - b.ts.getTime()
}

// -- Helpers --

function formatTime(d: Date): string {
  const h = d.getHours().toString().padStart(2, "0")
  const m = d.getMinutes().toString().padStart(2, "0")
  const s = d.getSeconds().toString().padStart(2, "0")
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

// -- Components --

function TypeFilterBar({
  enabled,
  onToggle,
}: {
  enabled: Set<GameLogType>
  onToggle: (type: GameLogType) => void
}) {
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

function CopyLogButton({ entries }: { entries: LogEntry[] }) {
  const [copied, setCopied] = useState(false)

  const handleCopy = useCallback(() => {
    const lines: string[] = []
    let prevNonce = -1

    for (const entry of entries) {
      if (entry.nonce !== 0 && entry.nonce !== prevNonce && prevNonce !== -1) {
        lines.push("") // blank line between nonce groups
      }
      prevNonce = entry.nonce

      const time = formatTime(entry.ts)
      const cfg = TYPE_CONFIG[entry.gameLogType] ?? TYPE_CONFIG.LogMessage
      const tag = cfg.short.padEnd(6)
      const body = formatDataAsText(entry.gameLogType, entry.data)

      // Indent multi-line data under the first line
      const bodyLines = body.split("\n")
      lines.push(`${time}  ${tag}  ${bodyLines[0]}`)
      for (let i = 1; i < bodyLines.length; i++) {
        lines.push(`${"".padEnd(12)}${"".padEnd(8)}${bodyLines[i]}`)
      }
    }

    navigator.clipboard.writeText(lines.join("\n")).then(() => {
      setCopied(true)
      setTimeout(() => setCopied(false), 1500)
    })
  }, [entries])

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

function GameStateHeader({ entry }: { entry: LogEntry }) {
  try {
    const d: GameStateData = JSON.parse(entry.data)
    return (
      <tr className="bg-muted/30 border-t border-sidebar-border/60">
        <td colSpan={4} className="px-3 py-1.5">
          <div className="flex items-center gap-3 text-[11px]">
            <span className="text-muted-foreground font-mono">{formatTime(entry.ts)}</span>
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

function LogRow({ entry, expanded, onToggle }: { entry: LogEntry; expanded: boolean; onToggle: () => void }) {
  const cfg = TYPE_CONFIG[entry.gameLogType] ?? TYPE_CONFIG.LogMessage

  return (
    <tr
      className="hover:bg-muted/50 transition-colors cursor-pointer group"
      onClick={onToggle}
    >
      {/* Timestamp */}
      <td className="w-[110px] shrink-0 px-3 py-1.5 text-muted-foreground align-top select-none font-mono text-[11px] whitespace-nowrap">
        {formatTime(entry.ts)}
      </td>
      {/* Delta */}
      <td className="w-[70px] shrink-0 px-2 py-1.5 text-muted-foreground/60 align-top select-none font-mono text-[10px] whitespace-nowrap text-right">
        {formatDelta(entry.deltaMs)}
      </td>
      {/* Type badge */}
      <td className="w-[70px] shrink-0 px-2 py-1.5 align-top">
        <span className={`inline-block px-1.5 py-0.5 rounded text-[10px] font-mono font-semibold ${cfg.color} ${cfg.bg}`}>
          {cfg.short}
        </span>
      </td>
      {/* Data */}
      <td className="px-3 py-1.5 break-words text-[12px] leading-relaxed align-top">
        {renderData(entry.gameLogType, entry.data)}
        {expanded && (
          <pre className="mt-1.5 p-2 rounded bg-muted/50 text-muted-foreground text-[10px] leading-snug overflow-x-auto max-w-full whitespace-pre-wrap border border-sidebar-border/60">
            {(() => {
              try { return JSON.stringify(JSON.parse(entry.data), null, 2) } catch { return entry.data }
            })()}
          </pre>
        )}
      </td>
    </tr>
  )
}

// -- Page --

export default function GameWatch() {
  const { matchId } = useParams<{ matchId: string }>()
  const navigate = useNavigate()
  const parsedMatchId = matchId ? parseInt(matchId, 10) : null

  const { isReady: clientReady } = useClientState()

  // Historical backfill — fetches persisted logs on mount
  const { data: matchData, loading: historyLoading } = useMatchDetails(parsedMatchId)
  const historyMergedRef = useRef(false)

  // Log state
  const [entries, setEntries] = useState<LogEntry[]>([])
  const seqRef = useRef(0)
  const lastTsRef = useRef<Date | null>(null)

  // UI state
  const [enabledTypes, setEnabledTypes] = useState<Set<GameLogType>>(new Set(ALL_TYPES))
  const [autoScroll, setAutoScroll] = useState(true)
  const [expandedRows, setExpandedRows] = useState<Set<number>>(new Set())
  const scrollRef = useRef<HTMLDivElement>(null)
  const [connected, setConnected] = useState(false)
  const [eventCount, setEventCount] = useState(0)

  // Handle incoming SSE messages
  const onMessage = useCallback((dto: GameLogDTO) => {
    const ts = new Date(dto.timestamp ?? Date.now())
    const seq = seqRef.current++
    const deltaMs = lastTsRef.current ? ts.getTime() - lastTsRef.current.getTime() : null
    lastTsRef.current = ts

    const entry: LogEntry = {
      id: dto.id ?? 0,
      gameId: dto.gameId ?? 0,
      timestamp: dto.timestamp ?? new Date().toISOString(),
      gameLogType: (dto.gameLogType ?? "LogMessage") as GameLogType,
      data: dto.data ?? "",
      nonce: dto.nonce ?? 0,
      seq,
      ts,
      deltaMs,
    }
    setEntries(prev => {
      // Cap at 2000 entries for performance
      const next = [...prev, entry]
      next.sort(compareLogEntries)
      return next.length > 2000 ? next.slice(-1500) : next
    })
    setEventCount(c => c + 1)
  }, [])

  // Connect to SSE stream
  const streamUrl = parsedMatchId
    ? getApiUrl(`/api/games/match/${parsedMatchId}/watch`)
    : ""

  useNDJSONStream<GameLogDTO>({
    url: streamUrl,
    onMessage,
    onEnd: () => setConnected(false),
    onError: () => setConnected(false),
    enabled: clientReady && parsedMatchId != null,
    autoReconnect: true,
    reconnectDelay: 2000,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })

  // Track connection state — if we receive a message, we're connected
  useEffect(() => {
    if (eventCount > 0) setConnected(true)
  }, [eventCount])

  // Reset all state when matchId changes
  useEffect(() => {
    historyMergedRef.current = false
    setEntries([])
    seqRef.current = 0
    lastTsRef.current = null
    setEventCount(0)
  }, [parsedMatchId])

  // Merge historical logs when they arrive — prepend before any live entries
  useEffect(() => {
    if (!matchData?.games || historyMergedRef.current) return
    historyMergedRef.current = true

    setEntries(prevLive => {
      // Flatten all game logs — backend already sorted within each game by
      // nonce/type/timestamp, and games are sequential, so just concatenate.
      const allLogs: GameLogDTO[] = (matchData.games ?? [])
        .flatMap(g => g.logs ?? [])

      let seq = 0
      let prevTs: Date | null = null
      const historical: LogEntry[] = allLogs.map(dto => {
        const ts = new Date(dto.timestamp ?? Date.now())
        const deltaMs = prevTs ? ts.getTime() - prevTs.getTime() : null
        prevTs = ts
        return {
          id: dto.id ?? 0,
          gameId: dto.gameId ?? 0,
          timestamp: dto.timestamp ?? "",
          gameLogType: (dto.gameLogType ?? "LogMessage") as GameLogType,
          data: dto.data ?? "",
          nonce: dto.nonce ?? 0,
          seq: seq++,
          ts,
          deltaMs,
        }
      })

      // Re-sequence live entries to continue after historical
      const resequenced = prevLive.map((e, i) => ({
        ...e,
        seq: seq + i,
        deltaMs: i === 0 && prevTs
          ? e.ts.getTime() - prevTs.getTime()
          : e.deltaMs,
      }))

      // Update refs so subsequent onMessage calls produce correct seq/delta
      seqRef.current = seq + resequenced.length
      const last = resequenced.at(-1) ?? historical.at(-1)
      if (last) lastTsRef.current = last.ts

      const combined = [...historical, ...resequenced]
      return combined.length > 2000 ? combined.slice(-1500) : combined
    })
  }, [matchData])

  // Auto-scroll
  useEffect(() => {
    if (autoScroll && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight
    }
  }, [entries, autoScroll])

  // Detect manual scroll to pause auto-scroll
  const handleScroll = useCallback(() => {
    if (!scrollRef.current) return
    const { scrollTop, scrollHeight, clientHeight } = scrollRef.current
    const atBottom = scrollTop + clientHeight >= scrollHeight - 40
    setAutoScroll(atBottom)
  }, [])

  // Filter entries and recompute deltas based on display order
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

  const toggleType = useCallback((type: GameLogType) => {
    setEnabledTypes(prev => {
      const next = new Set(prev)
      if (next.has(type)) next.delete(type)
      else next.add(type)
      return next
    })
  }, [])

  const toggleExpand = useCallback((seq: number) => {
    setExpandedRows(prev => {
      const next = new Set(prev)
      if (next.has(seq)) next.delete(seq)
      else next.add(seq)
      return next
    })
  }, [])

  return (
    <div className="flex flex-col h-[calc(100vh-60px)]">
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-sidebar-border/60 bg-card/50 shrink-0">
        <div className="flex items-center gap-3">
          <Button variant="ghost" size="icon" onClick={() => navigate(-1)} className="shrink-0 h-8 w-8">
            <ArrowLeft className="h-4 w-4" />
          </Button>
          <div>
            <h1 className="text-sm font-semibold tracking-tight">
              Game Watch
              <span className="text-muted-foreground font-normal ml-2">Match #{parsedMatchId}</span>
            </h1>
          </div>
        </div>
        <div className="flex items-center gap-3">
          <Badge variant={connected ? "success" : "outline"} className="text-[10px] font-mono">
            {connected ? "LIVE" : "WAITING"}
          </Badge>
          <span className="text-[11px] text-muted-foreground font-mono">
            {entries.length} entries{eventCount > 0 && ` (${eventCount} live)`}
          </span>
        </div>
      </div>

      {/* Filter bar */}
      <div className="px-4 py-2 border-b border-sidebar-border/60 bg-muted/30 shrink-0 flex items-center gap-3">
        <TypeFilterBar enabled={enabledTypes} onToggle={toggleType} />
        <CopyLogButton entries={filtered} />
      </div>

      {/* Log table */}
      <div
        ref={scrollRef}
        onScroll={handleScroll}
        className="flex-1 overflow-y-auto overflow-x-hidden bg-background"
      >
        {filtered.length === 0 ? (
          <div className="flex items-center justify-center h-full text-muted-foreground text-sm">
            {entries.length === 0
              ? (historyLoading ? "Loading historical events..." : "Waiting for game events...")
              : "No events match the current filters."}
          </div>
        ) : (
          <table className="w-full font-mono">
            <thead className="sticky top-0 z-10">
              <tr className="bg-muted/50 border-b border-sidebar-border/60 text-[10px] text-muted-foreground font-semibold tracking-wider uppercase">
                <th className="w-[110px] px-3 py-1.5 text-left">Time</th>
                <th className="w-[70px] px-2 py-1.5 text-right">Delta</th>
                <th className="w-[70px] px-2 py-1.5 text-left">Type</th>
                <th className="px-3 py-1.5 text-left">Data</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((entry, idx) => {
                const prevNonce = idx > 0 ? filtered[idx - 1].nonce : entry.nonce
                const nonceChanged = idx > 0 && entry.nonce !== 0 && entry.nonce !== prevNonce

                // GameState entries render as section headers (already visually separated)
                if (entry.gameLogType === "GameState") {
                  return <GameStateHeader key={entry.seq} entry={entry} />
                }

                // Emit a separator bar when the nonce changes and there's no GameState header
                return (
                  <React.Fragment key={entry.seq}>
                    {nonceChanged && <NonceSeparator />}
                    <LogRow
                      entry={entry}
                      expanded={expandedRows.has(entry.seq)}
                      onToggle={() => toggleExpand(entry.seq)}
                    />
                  </React.Fragment>
                )
              })}
            </tbody>
          </table>
        )}
      </div>

      {/* Auto-scroll indicator */}
      {!autoScroll && (
        <button
          onClick={() => {
            setAutoScroll(true)
            if (scrollRef.current) {
              scrollRef.current.scrollTop = scrollRef.current.scrollHeight
            }
          }}
          className="absolute bottom-4 right-6 flex items-center gap-1.5 px-3 py-1.5 rounded-full bg-secondary border border-sidebar-border/60 text-secondary-foreground text-xs font-medium shadow-lg hover:bg-accent transition-colors z-20"
        >
          <ArrowDown className="h-3 w-3" />
          Resume auto-scroll
        </button>
      )}
    </div>
  )
}
