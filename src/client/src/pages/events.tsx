"use client"

import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import { ColumnDef } from "@tanstack/react-table"
import { DataTable } from "@/components/ui/data-table"
import { EventsTableSkeleton } from "@/components/events-table-skeleton"
import { EventsTimeline } from "@/components/events-timeline"
import { EventDetailPanel } from "@/components/event-detail-panel"
import { useEvents, ActiveGame } from "@/hooks/use-events"
import { getApiUrl } from "@/utils/api-config"
import { cn } from "@/lib/utils"

function formatTime(dateString?: string) {
  if (!dateString) return "–"
  return new Date(dateString).toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
}

function formatSchedule(start?: string, end?: string) {
  if (!start) return "–"
  const startDate = new Date(start)
  const date = startDate.toLocaleDateString(undefined, {
    month: 'numeric',
    day: 'numeric',
    year: 'numeric',
  })
  const startTime = formatTime(start)
  const endTime = formatTime(end)
  return `${date}, ${startTime} – ${endTime}`
}

// --- Format color dots (matches timeline) ---

const FORMAT_DOT_COLORS: [string, string][] = [
  ["modern",         "bg-red-500"    ],
  ["legacy",         "bg-blue-500"   ],
  ["duel commander", "bg-green-500"  ],
  ["standard",       "bg-purple-500" ],
  ["vintage",        "bg-amber-500"  ],
  ["pauper",         "bg-teal-500"   ],
  ["pioneer",        "bg-pink-500"   ],
  ["premodern",      "bg-red-400"    ],
]

function getFormatDot(format: string): string {
  const lower = format.toLowerCase()
  for (const [key, dot] of FORMAT_DOT_COLORS) {
    if (lower.includes(key)) return dot
  }
  return "bg-orange-500"
}

const entryFeeCache = new Map<string, string>()
const entryFeeRequests = new Map<string, Promise<string>>()
const TIMELINE_COMPLETED_EVENT_WINDOW_MS = 12 * 60 * 60 * 1000

function getEventStartTime(event: ActiveGame) {
  return event._rawStartTime ? new Date(event._rawStartTime).getTime() : 0
}

function getEventEndTime(event: ActiveGame) {
  return event._rawEndTime ? new Date(event._rawEndTime).getTime() : 0
}

function isRecentCompletedEvent(event: ActiveGame) {
  if (event.status !== "completed") return true
  const completedCutoff = Date.now() - TIMELINE_COMPLETED_EVENT_WINDOW_MS
  return getEventEndTime(event) >= completedCutoff
}

function useEntryFee(eventId: string, enabled: boolean) {
  const [fee, setFee] = useState(() => enabled ? entryFeeCache.get(eventId) : undefined)

  useEffect(() => {
    if (!enabled) {
      setFee(undefined)
      return
    }

    const cached = entryFeeCache.get(eventId)
    if (cached != null) {
      setFee(cached)
      return
    }

    let cancelled = false
    const request = entryFeeRequests.get(eventId) ??
      fetch(getApiUrl(`/api/Events/GetEntryFee/${eventId}`))
        .then(r => r.ok ? r.text() : "-")
        .catch(() => "-")
        .then(value => {
          entryFeeCache.set(eventId, value)
          entryFeeRequests.delete(eventId)
          return value
        })

    entryFeeRequests.set(eventId, request)
    request.then(value => {
      if (!cancelled) setFee(value)
    })

    return () => {
      cancelled = true
    }
  }, [eventId, enabled])

  return fee
}

function EntryFeeCell({ event, enabled }: { event: ActiveGame; enabled: boolean }) {
  const shouldFetch = enabled && event.status !== "completed"
  const fee = useEntryFee(event.id, shouldFetch)
  if (event.status === "completed") return <span className="text-muted-foreground">-</span>
  return <span className="text-muted-foreground">{fee ?? "..."}</span>
}

// --- Page ---

export default function Events() {
  const { activeGames, upcomingGames, completedGames, loading, error, hoveredEventId, setHoveredEventId, selectedEventId, setSelectedEventId } = useEvents()
  const tableAreaRef = useRef<HTMLDivElement>(null)
  const [areaHeight, setAreaHeight] = useState<number | undefined>()
  const [currentPageEventIds, setCurrentPageEventIds] = useState<Set<string>>(() => new Set())
  const [timelineScrollKey, setTimelineScrollKey] = useState(0)

  const events = useMemo(() => {
    return [...activeGames, ...upcomingGames, ...completedGames]
      .filter(isRecentCompletedEvent)
      .sort((a, b) => getEventStartTime(a) - getEventStartTime(b))
  }, [activeGames, upcomingGames, completedGames])

  const timelineEvents = useMemo(() => {
    const completedCutoff = Date.now() - TIMELINE_COMPLETED_EVENT_WINDOW_MS
    return events.filter(event => {
      if (event.status !== "completed") return true
      if (!event._rawEndTime) return false
      return new Date(event._rawEndTime).getTime() >= completedCutoff
    })
  }, [events])

  const playersDigitsWidth = useMemo(() => {
    return Math.max(1, ...events.map(event => String(event.totalPlayers ?? 0).length))
  }, [events])

  const selectedEvent = useMemo(() => {
    if (!selectedEventId) return null
    return events.find(e => e.id === selectedEventId) ?? null
  }, [selectedEventId, events])

  const activeEventIdSet = useMemo(() =>
    new Set(activeGames.map(e => e.id)),
  [activeGames])

  // Measure remaining height from table area to scroll container bottom
  useEffect(() => {
    const el = tableAreaRef.current
    if (!el) return
    const scrollAncestor = el.closest(".overflow-y-auto") as HTMLElement | null
    const update = () => {
      const bottom = scrollAncestor
        ? scrollAncestor.getBoundingClientRect().bottom
        : window.innerHeight
      setAreaHeight(bottom - el.getBoundingClientRect().top)
    }
    update()
    window.addEventListener("resize", update)
    const ro = new ResizeObserver(update)
    ro.observe(el)
    if (scrollAncestor) ro.observe(scrollAncestor)
    return () => { window.removeEventListener("resize", update); ro.disconnect() }
  }, [])

  const handleRowClick = useCallback((event: ActiveGame) => {
    setSelectedEventId(selectedEventId === event.id ? null : event.id)
  }, [selectedEventId, setSelectedEventId])

  const handleTimelineEventClick = useCallback((event: ActiveGame) => {
    setSelectedEventId(event.id)
    setTimelineScrollKey(key => key + 1)
  }, [setSelectedEventId])

  const handlePageRowsChange = useCallback((rows: ActiveGame[]) => {
    setCurrentPageEventIds((current) => {
      const next = new Set(rows.map(row => row.id))
      if (
        next.size === current.size &&
        Array.from(next).every(id => current.has(id))
      ) {
        return current
      }
      return next
    })
  }, [])

  const columns: ColumnDef<ActiveGame>[] = useMemo(() => [
    {
      accessorKey: "name",
      header: "Name",
    },
    {
      id: "schedule",
      header: "Schedule",
      size: 176,
      cell: ({ row }) => formatSchedule(row.original._rawStartTime, row.original._rawEndTime),
    },
    {
      accessorKey: "format",
      header: "Format",
      size: 112,
      cell: ({ row }) => (
        <span className="inline-flex items-center gap-1.5">
          <span className={cn("w-2 h-2 rounded-full shrink-0 translate-y-px", getFormatDot(row.original.format))} />
          {row.original.format}
        </span>
      ),
    },
    {
      id: "entryFee",
      header: "Entry Fee",
      size: 86,
      cell: ({ row }) => {
        return (
          <EntryFeeCell
            event={row.original}
            enabled={currentPageEventIds.has(row.original.id)}
          />
        )
      },
    },
    {
      accessorKey: "totalPlayers",
      header: "Players",
      size: 84,
      cell: ({ row }) => {
        const total = row.original.totalPlayers ?? 0
        const min = row.original.minimumPlayers ?? 0
        const pct = min > 0 ? Math.min(1, total / min) : 0
        const r = 6
        const circ = 2 * Math.PI * r
        const filled = circ * pct
        return (
          <span className="inline-flex items-center gap-1.5">
            <span className="tabular-nums text-right" style={{ width: `${playersDigitsWidth}ch` }}>{total}</span>
            <span className="text-muted-foreground">/ {min}</span>
            <svg width="16" height="16" className="shrink-0 -rotate-90">
              <circle cx="8" cy="8" r={r} fill="none" stroke="currentColor" strokeWidth="2" className="text-border" />
              <circle cx="8" cy="8" r={r} fill="none" stroke="currentColor" strokeWidth="2"
                strokeDasharray={`${filled} ${circ - filled}`}
                className={pct >= 1 ? "text-green-500" : pct >= 0.5 ? "text-blue-500" : "text-muted-foreground"}
              />
            </svg>
          </span>
        )
      }
    },
    {
      accessorKey: "totalRounds",
      header: "Rounds",
      size: 56,
    },
  ], [currentPageEventIds, playersDigitsWidth])

  return (
    <div className="-mt-10">
      <EventsTimeline events={timelineEvents} focusedEventId={hoveredEventId ?? selectedEvent?.id ?? null} activeEventIds={activeEventIdSet} onEventClick={handleTimelineEventClick} />

      <div ref={tableAreaRef} className="flex" style={areaHeight ? { height: areaHeight } : undefined}>
        {/* Table area */}
        <div className="flex min-h-0 flex-1 min-w-0 flex-col gap-4 overflow-hidden px-4 pt-2 pb-4">
          {error && (
            <div className="bg-destructive/15 text-destructive px-4 py-3 rounded-md text-sm font-medium">
              Error loading events: {error}
            </div>
          )}

          {loading && events.length === 0 ? (
            <div className="rounded-md">
              <table className="w-full">
                <thead>
                  <tr className="border-b border-sidebar-border/60 bg-muted/50">
                    {columns.map((col, i) => (
                      <th key={i} className="h-12 px-4 text-left align-middle font-medium text-muted-foreground">
                        {typeof col.header === 'string' ? col.header : ''}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  <EventsTableSkeleton rows={10} columns={columns.length} />
                </tbody>
              </table>
            </div>
          ) : (
            <DataTable
              columns={columns}
              data={events}
              autoResetPageIndex={false}
              containerClassName="flex min-h-0 flex-1 flex-col"
              tableContainerClassName="flex min-h-0 flex-1 flex-col overflow-visible"
              bodyWrapperClassName="min-h-0 flex-1 overflow-y-auto overflow-x-hidden"
              onRowHover={(event: ActiveGame) => setHoveredEventId(event.id)}
              onRowLeave={() => setHoveredEventId(null)}
              onRowClick={handleRowClick}
              onPageRowsChange={handlePageRowsChange}
              getRowClassName={(event: ActiveGame) => cn(
                event.id !== selectedEventId && event._rawStartTime && new Date(event._rawStartTime).getTime() < Date.now() && "opacity-60",
                selectedEvent?.id === event.id && "outline outline-1 outline-white/80 -outline-offset-1 bg-muted/50",
              )}
              activeRowId={selectedEvent?.id ?? null}
              activeRowScrollKey={timelineScrollKey}
              getRowId={(event: ActiveGame) => event.id}
            />
          )}
        </div>

        {/* Detail panel — appears on row click */}
        <EventDetailPanel
          event={selectedEvent}
          loadDetails={Boolean(selectedEvent)}
          onClose={() => setSelectedEventId(null)}
        />
      </div>
    </div>
  )
}
