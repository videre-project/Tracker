/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import React, { useCallback, useEffect, useMemo, useRef, useState } from "react"
import { Maximize2, Minimize2, Search, Settings2 } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Card } from "@/components/ui/card"
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { type LogEntry, useLogsStream } from "@/hooks/use-logs-stream"
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

export function UnifiedLog() {
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
