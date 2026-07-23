"use client"

import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import { ColumnDef } from "@tanstack/react-table"
import { DataTable } from "@/components/ui/data-table"
import { EventsTableSkeleton } from "@/components/events-table-skeleton"
import { EventsTimeline } from "@/components/events-timeline"
import { EventDetailPanel } from "@/components/event-detail-panel"
import { useEvents, ActiveGame } from "@/hooks/use-events"
import { getApiUrl } from "@/utils/api-config"
import { getFormatDotColor } from "@/utils/formats"
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

function useEntryFee(eventId: string, enabled: boolean, onFeeFetched?: () => void) {
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
      if (!cancelled) {
        setFee(value)
        onFeeFetched?.()
      }
    })

    return () => {
      cancelled = true
    }
  }, [eventId, enabled, onFeeFetched])

  return fee
}

function EntryFeeCell({ event, enabled, onFeeFetched }: { event: ActiveGame; enabled: boolean; onFeeFetched?: () => void }) {
  const fee = useEntryFee(event.id, enabled, onFeeFetched)
  return <span className="text-muted-foreground">{fee ?? "..."}</span>
}

// --- Page ---

export default function Events() {
  const { activeGames, upcomingGames, completedGames, loading, error, hoveredEventId, setHoveredEventId, selectedEventId, setSelectedEventId } = useEvents()
  const [currentPageEventIds, setCurrentPageEventIds] = useState<Set<string>>(() => new Set())
  const [timelineScrollKey, setTimelineScrollKey] = useState(0)

  const [feeCacheVersion, setFeeCacheVersion] = useState(0)

  const events = useMemo(() => {
    const now = Date.now()
    return [...activeGames, ...upcomingGames, ...completedGames]
      .filter(isRecentCompletedEvent)
      .filter(event => {
        const isPast = event.status === "completed" || (getEventStartTime(event) > 0 && getEventStartTime(event) < now)
        if (isPast && (event.minimumPlayers ?? 0) === 0) {
          return false
        }

        const cached = entryFeeCache.get(event.id)
        if (cached !== undefined && (!cached || cached === "-" || cached.trim() === "")) {
          return false
        }
        return true
      })
      .sort((a, b) => getEventStartTime(a) - getEventStartTime(b))
  }, [activeGames, upcomingGames, completedGames, feeCacheVersion])

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

  // Auto-select soonest upcoming event on initial load
  const hasAutoSelectedRef = useRef(false)
  useEffect(() => {
    if (hasAutoSelectedRef.current || selectedEventId != null || events.length === 0) return
    const now = Date.now()
    const soonestUpcoming =
      events.find(e => (e.status === "scheduled" || e.status === "active") && getEventStartTime(e) >= now) ??
      events.find(e => e.status === "scheduled" || e.status === "active") ??
      events[0]

    if (soonestUpcoming) {
      hasAutoSelectedRef.current = true
      setSelectedEventId(soonestUpcoming.id)
    }
  }, [events, selectedEventId, setSelectedEventId])

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

  const handleFeeFetched = useCallback(() => {
    setFeeCacheVersion(v => v + 1)
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
          <span className={cn("w-2 h-2 rounded-full shrink-0 translate-y-px", getFormatDotColor(row.original.format))} />
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
            onFeeFetched={handleFeeFetched}
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
  ], [currentPageEventIds, playersDigitsWidth, handleFeeFetched])

  return (
    <div className="-mt-10 flex flex-col h-[calc(100vh-1rem)] overflow-hidden">
      <EventsTimeline events={timelineEvents} focusedEventId={hoveredEventId ?? selectedEvent?.id ?? null} activeEventIds={activeEventIdSet} onEventClick={handleTimelineEventClick} />

      <div className="flex flex-1 min-h-0 min-w-0">
        {/* Table area */}
        <div className="flex min-h-0 flex-1 min-w-0 flex-col gap-4 overflow-hidden px-4 pt-2 pb-1.5">
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
