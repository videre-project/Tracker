import { useMemo, useEffect, useLayoutEffect, useRef, useState, useCallback } from "react"
import { format as fnsFormat } from "date-fns"
import { Maximize2, Minimize2 } from "lucide-react"
import { cn } from "@/lib/utils"
import { Button } from "@/components/ui/button"
import type { ActiveGame } from "@/hooks/use-events"

// --- Constants ---

const PX_PER_HOUR = 80
const LANE_HEIGHT = 32
const LANE_GAP = 2
const ROW_PAD = 4
const MIN_BAR_WIDTH = 60
const MAX_SCROLL_HEIGHT = 300

// --- Format colors ---

const FORMAT_COLORS: [string, string][] = [
  ["modern",         "bg-red-700"    ],
  ["legacy",         "bg-blue-700"   ],
  ["duel commander", "bg-green-700"  ],
  ["standard",       "bg-purple-700" ],
  ["vintage",        "bg-amber-700"  ],
  ["pauper",         "bg-teal-700"   ],
  ["pioneer",        "bg-pink-700"   ],
  ["premodern",      "bg-red-900"    ],
]

const DEFAULT_BG = "bg-orange-700"

function getFormatBg(format: string): string {
  const lower = format.toLowerCase()
  for (const [key, bg] of FORMAT_COLORS) {
    if (lower.includes(key)) return bg
  }
  return DEFAULT_BG
}

// --- Timeline range ---

function getTimelineRange(events: ActiveGame[]) {
  let earliest = Infinity
  let latest = -Infinity
  for (const e of events) {
    if (e._rawStartTime) earliest = Math.min(earliest, new Date(e._rawStartTime).getTime())
    if (e._rawEndTime) latest = Math.max(latest, new Date(e._rawEndTime).getTime())
  }
  if (earliest === Infinity) {
    const now = new Date()
    const start = new Date(now.getFullYear(), now.getMonth(), now.getDate())
    const end = new Date(start.getTime() + 24 * 60 * 60 * 1000)
    return { start, end }
  }
  const start = new Date(earliest)
  start.setMinutes(0, 0, 0)
  const end = new Date(latest)
  if (end.getMinutes() > 0 || end.getSeconds() > 0) {
    end.setHours(end.getHours() + 1, 0, 0, 0)
  }
  return { start, end }
}

function generateHourSlots(start: Date, end: Date): Date[] {
  const hours: Date[] = []
  const cur = new Date(start)
  while (cur.getTime() < end.getTime()) {
    hours.push(new Date(cur))
    cur.setHours(cur.getHours() + 1)
  }
  return hours
}

// --- Day spans for header ---

interface DaySpan {
  label: string
  hourCount: number
}

function computeDaySpans(hourSlots: Date[]): DaySpan[] {
  if (hourSlots.length === 0) return []
  const spans: DaySpan[] = []
  let curDay = hourSlots[0]
  let count = 0

  for (const slot of hourSlots) {
    if (slot.getDate() !== curDay.getDate() || slot.getMonth() !== curDay.getMonth()) {
      spans.push({ label: fnsFormat(curDay, "EEEE, MMMM d, yyyy"), hourCount: count })
      curDay = slot
      count = 1
    } else {
      count++
    }
  }
  spans.push({ label: fnsFormat(curDay, "EEEE, MMMM d, yyyy"), hourCount: count })
  return spans
}

// --- Positioning ---

function eventToPosition(event: ActiveGame, timelineStart: Date) {
  const start = new Date(event._rawStartTime!)
  const end = new Date(event._rawEndTime!)
  const startMs = Math.max(start.getTime() - timelineStart.getTime(), 0)
  const endMs = end.getTime() - timelineStart.getTime()
  const left = (startMs / (1000 * 60 * 60)) * PX_PER_HOUR
  const width = Math.max(((endMs - startMs) / (1000 * 60 * 60)) * PX_PER_HOUR, MIN_BAR_WIDTH)
  return { left, width }
}

function getCurrentTimeOffset(start: Date, end: Date): number | null {
  const now = Date.now()
  if (now < start.getTime() || now > end.getTime()) return null
  return ((now - start.getTime()) / (1000 * 60 * 60)) * PX_PER_HOUR
}

function formatDuration(s: string, e: string): string {
  const min = Math.round((new Date(e).getTime() - new Date(s).getTime()) / 60000)
  const h = Math.floor(min / 60)
  const m = min % 60
  return m === 0 ? `(${h}h)` : `(${h}h ${m}m)`
}

function formatTimeShort(d: string): string {
  return new Date(d).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" })
}

// --- Lane assignment (per-format) ---

interface LanedEvent { event: ActiveGame; lane: number }

function assignLanes(events: ActiveGame[]): { lanes: LanedEvent[]; laneCount: number } {
  const sorted = [...events].sort((a, b) =>
    new Date(a._rawStartTime!).getTime() - new Date(b._rawStartTime!).getTime()
  )
  const laneEnds: number[] = []
  const lanes: LanedEvent[] = []
  for (const event of sorted) {
    const start = new Date(event._rawStartTime!).getTime()
    let assigned = -1
    for (let i = 0; i < laneEnds.length; i++) {
      if (start >= laneEnds[i]) { assigned = i; break }
    }
    if (assigned === -1) { assigned = laneEnds.length; laneEnds.push(0) }
    laneEnds[assigned] = new Date(event._rawEndTime!).getTime()
    lanes.push({ event, lane: assigned })
  }
  return { lanes, laneCount: Math.max(laneEnds.length, 1) }
}

// --- Format grouping ---

interface FormatGroup {
  format: string
  lanes: LanedEvent[]
  laneCount: number
}

function groupByFormat(events: ActiveGame[]): FormatGroup[] {
  const map = new Map<string, ActiveGame[]>()
  for (const e of events) {
    if (!e._rawStartTime || !e._rawEndTime) continue
    const list = map.get(e.format)
    if (list) list.push(e)
    else map.set(e.format, [e])
  }
  return Array.from(map.entries())
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([format, evts]) => ({ format, ...assignLanes(evts) }))
}

function formatRowHeight(laneCount: number) {
  return laneCount * LANE_HEIGHT + Math.max(0, laneCount - 1) * LANE_GAP + ROW_PAD * 2
}

// --- Hour label ---

function hourLabel(hour: number): string {
  if (hour === 0) return "12 AM"
  if (hour === 12) return "12 PM"
  const h = hour % 12
  return `${h} ${hour < 12 ? "AM" : "PM"}`
}

// --- Component ---

interface EventsTimelineProps {
  events: ActiveGame[]
  focusedEventId?: string | null
  activeEventIds?: Set<string>
  onEventClick?: (event: ActiveGame) => void
}

export function EventsTimeline({ events, focusedEventId, activeEventIds, onEventClick }: EventsTimelineProps) {
  const wrapperRef = useRef<HTMLDivElement>(null)
  const scrollRef = useRef<HTMLDivElement>(null)
  const dayLabelRefs = useRef<(HTMLDivElement | null)[]>([])
  const [timeOffset, setTimeOffset] = useState<number | null>(null)
  const [viewportWidth, setViewportWidth] = useState(0)
  const [labelWidths, setLabelWidths] = useState<number[]>([])
  const [isFullscreen, setIsFullscreen] = useState(false)
  const hasScrolled = useRef(false)

  // Hide siblings and lock scroll ancestor so timeline fills the viewport
  useEffect(() => {
    if (!isFullscreen) return
    const wrapper = wrapperRef.current
    const page = wrapper?.parentElement
    if (!wrapper || !page) return

    // Hide sibling elements (table, error, etc.)
    const siblings = Array.from(page.children).filter(el => el !== wrapper) as HTMLElement[]
    siblings.forEach(el => el.style.display = "none")

    // Make scroll ancestor a constrained flex column
    const scrollAncestor = wrapper.closest(".overflow-y-auto") as HTMLElement | null
    if (scrollAncestor) {
      scrollAncestor.style.overflow = "hidden"
      scrollAncestor.style.display = "flex"
      scrollAncestor.style.flexDirection = "column"
    }

    // Page container fills remaining space
    page.style.flex = "1"
    page.style.minHeight = "0"
    page.style.display = "flex"
    page.style.flexDirection = "column"

    return () => {
      siblings.forEach(el => el.style.display = "")
      page.style.flex = ""
      page.style.minHeight = ""
      page.style.display = ""
      page.style.flexDirection = ""
      if (scrollAncestor) {
        scrollAncestor.style.overflow = ""
        scrollAncestor.style.display = ""
        scrollAncestor.style.flexDirection = ""
      }
    }
  }, [isFullscreen])

  const range = useMemo(() => getTimelineRange(events), [events])
  const hourSlots = useMemo(() => generateHourSlots(range.start, range.end), [range])
  const daySpans = useMemo(() => computeDaySpans(hourSlots), [hourSlots])
  const timelineWidth = hourSlots.length * PX_PER_HOUR
  const formatGroups = useMemo(() => groupByFormat(events), [events])

  // Track scroll container width
  useEffect(() => {
    const el = scrollRef.current
    if (!el) return
    const ro = new ResizeObserver(() => setViewportWidth(el.clientWidth))
    setViewportWidth(el.clientWidth)
    ro.observe(el)
    return () => ro.disconnect()
  }, [])

  // Measure label text widths before first paint
  const setLabelRef = useCallback((el: HTMLDivElement | null, i: number) => {
    dayLabelRefs.current[i] = el
  }, [])

  useLayoutEffect(() => {
    setLabelWidths(dayLabelRefs.current.map(el => el?.offsetWidth ?? 0))
  }, [daySpans])

  // Current time marker
  useEffect(() => {
    const update = () => setTimeOffset(getCurrentTimeOffset(range.start, range.end))
    update()
    const id = setInterval(update, 60_000)
    return () => clearInterval(id)
  }, [range])

  // Initial positioning: jump (no animation) to the selected event so the
  // timeline matches the table's initial state, or to the current time /
  // earliest event when nothing is selected. Later focused-event scrolls
  // (hover/selection) remain smooth — see the effect below.
  useEffect(() => {
    if (!scrollRef.current || hasScrolled.current || formatGroups.length === 0) return
    const container = scrollRef.current

    let target: number
    const selected = focusedEventId
      ? events.find(e => e.id === focusedEventId)
      : null
    if (selected?._rawStartTime && selected?._rawEndTime) {
      const pos = eventToPosition(selected, range.start)
      target = pos.left + pos.width / 2
    } else if (timeOffset !== null) {
      target = timeOffset
    } else {
      let minLeft = timelineWidth
      for (const { lanes } of formatGroups) {
        for (const { event } of lanes) {
          const { left } = eventToPosition(event, range.start)
          if (left < minLeft) minLeft = left
        }
      }
      target = minLeft
    }
    container.scrollLeft = Math.max(0, target - container.clientWidth / 3)
    hasScrolled.current = true
  }, [formatGroups, timeOffset, range, timelineWidth, focusedEventId, events])

  // Scroll to focused event when hovering a table row (debounced to avoid animation queue)
  useEffect(() => {
    if (!focusedEventId || !scrollRef.current) return

    const timer = setTimeout(() => {
      const el = scrollRef.current
      if (!el) return
      const bar = el.querySelector(`[data-event-id="${focusedEventId}"]`) as HTMLElement | null
      if (!bar) return

      const event = events.find(e => e.id === focusedEventId)
      if (!event?._rawStartTime || !event._rawEndTime) return

      const pos = eventToPosition(event, range.start)
      const centerX = pos.left + pos.width / 2
      const targetLeft = Math.max(0, centerX - el.clientWidth / 2)

      const scrollRect = el.getBoundingClientRect()
      const barRect = bar.getBoundingClientRect()
      const barTopInScroll = barRect.top - scrollRect.top + el.scrollTop
      const barCenterY = barTopInScroll + barRect.height / 2
      const targetTop = Math.max(0, barCenterY - el.clientHeight / 2)

      el.scrollTo({ left: targetLeft, top: targetTop, behavior: "smooth" })
    }, 80)

    return () => clearTimeout(timer)
  }, [focusedEventId, events, range])

  if (formatGroups.length === 0) {
    return (
      <div className="px-4 py-6 text-center text-sm text-muted-foreground">
        No scheduled events to display on timeline.
      </div>
    )
  }

  return (
    <div
      ref={wrapperRef}
      className={cn("relative isolate", isFullscreen && "flex-1 min-h-0 flex flex-col")}
    >
      {/* Left-edge fade for date text */}
      <div
        className="absolute top-0 left-0 z-30 pointer-events-none"
        style={{
          width: 60,
          height: 28,
          background: 'linear-gradient(to right, hsl(var(--background)) 0px, transparent)',
        }}
      />
      {/* Right-edge fade for date text */}
      <div
        className="absolute top-0 right-0 z-30 pointer-events-none"
        style={{
          width: 60,
          height: 28,
          background: 'linear-gradient(to left, hsl(var(--background)) 0px, transparent)',
        }}
      />
      <div
        ref={scrollRef}
        className={cn("overflow-auto border-b border-border", isFullscreen && "flex-1 min-h-0")}
        style={isFullscreen ? undefined : { maxHeight: MAX_SCROLL_HEIGHT }}
      >
        <div style={{ width: timelineWidth }}>
          {/* Sticky two-tier header */}
        <div className="sticky top-0 z-20 bg-background">
          {/* Day spans row — labels sticky-center */}
          <div className="flex border-b border-border">
            {daySpans.map((day, i) => {
              const spanWidth = day.hourCount * PX_PER_HOUR
              const tw = labelWidths[i] ?? 0
              const stickyLeft = viewportWidth > 0 ? (viewportWidth - tw) / 2 : 0
              return (
                <div
                  key={i}
                  className={cn("px-3", i > 0 && "border-l border-border")}
                  style={{ width: spanWidth }}
                >
                  <div
                    ref={el => setLabelRef(el, i)}
                    className="sticky w-fit text-[13px] font-medium text-muted-foreground whitespace-nowrap pt-0.5 pb-2.5"
                    style={{ left: stickyLeft }}
                  >
                    {day.label}
                  </div>
                </div>
              )
            })}
          </div>
          {/* Hour labels row */}
          <div className="flex border-b border-border/60">
            {hourSlots.map((slot, i) => (
                <div
                  key={i}
                  className="relative text-center text-[11px] text-muted-foreground/80 py-1 select-none shrink-0 border-r border-border/50"
                  style={{ width: PX_PER_HOUR, minWidth: PX_PER_HOUR }}
                >
                  {hourLabel(slot.getHours())}
                  {/* 15-min tick marks */}
                  <div className="absolute bottom-0 left-0 right-0 h-[4px] pointer-events-none">
                    {[1, 2, 3].map((t) => (
                      <div
                        key={t}
                        className="absolute bottom-0 w-px h-full bg-border/30"
                        style={{ left: `${t * 25}%` }}
                      />
                    ))}
                  </div>
                </div>
            ))}
          </div>
        </div>

        {/* Format rows + time marker wrapper */}
        <div className="relative">
          {formatGroups.map(({ format, lanes, laneCount }) => {
            const height = formatRowHeight(laneCount)
            const bg = getFormatBg(format)
            return (
              <div
                key={format}
                className="relative border-b border-border/15"
                style={{ height }}
              >
                {/* Hour grid lines */}
                {hourSlots.map((slot, i) => {
                  const isDayBoundary = i > 0 && slot.getHours() === 0
                  return (
                    <div
                      key={i}
                      className={cn(
                        "absolute top-0 bottom-0",
                        isDayBoundary
                          ? "border-l border-border/40"
                          : "border-l border-border/10",
                      )}
                      style={{ left: i * PX_PER_HOUR }}
                    />
                  )
                })}

                {/* Event bars */}
                {lanes.map(({ event, lane }) => {
                  const pos = eventToPosition(event, range.start)
                  const dur = formatDuration(event._rawStartTime!, event._rawEndTime!)
                  const timeStr = `${formatTimeShort(event._rawStartTime!)} - ${formatTimeShort(event._rawEndTime!)} ${dur}`
                  const top = ROW_PAD + lane * (LANE_HEIGHT + LANE_GAP)
                  const isFocused = focusedEventId === event.id
                  const isActive = activeEventIds?.has(event.id) ?? false
                  const isDimmed = (focusedEventId != null && !isFocused) || (!focusedEventId && isActive)
                  return (
                    <div
                      key={event.id}
                      data-event-id={event.id}
                      className={cn(
                        "absolute rounded-sm px-1.5 flex flex-col justify-center overflow-hidden text-white shadow-sm transition-all duration-150",
                        onEventClick ? "cursor-pointer" : "cursor-default",
                        bg,
                        isFocused && "ring-2 ring-white/70 z-10",
                        isDimmed && "opacity-30",
                      )}
                      style={{ left: pos.left, width: pos.width, top, height: LANE_HEIGHT }}
                      onClick={() => onEventClick?.(event)}
                      title={`${event.name}\nFormat: ${event.format}\nTime: ${timeStr}\nPlayers: ${event.totalPlayers ?? "?"} / ${event.minimumPlayers ?? "?"}\nRounds: ${event.totalRounds ?? "?"}`}
                    >
                      <span className="text-[10px] font-medium truncate leading-tight">
                        {event.name}
                      </span>
                      <span className="text-[9px] opacity-80 truncate leading-tight">
                        {timeStr}
                      </span>
                    </div>
                  )
                })}
              </div>
            )
          })}

          {/* Current time marker — spans all rows */}
          {timeOffset !== null && (
            <div
              className="absolute top-0 bottom-0 w-0.5 bg-red-500 z-10 pointer-events-none"
              style={{ left: timeOffset }}
            />
          )}
        </div>
      </div>
      </div>
      {/* Fullscreen toggle */}
      <Button
        variant="secondary"
        size="sm"
        className="absolute bottom-2 right-4 h-8 w-8 p-0 rounded-full shadow-md opacity-50 hover:opacity-100 transition-opacity z-30 bg-background/80 backdrop-blur-sm"
        onClick={() => setIsFullscreen(v => !v)}
        title={isFullscreen ? "Exit fullscreen" : "Fullscreen"}
      >
        {isFullscreen ? <Minimize2 className="h-4 w-4" /> : <Maximize2 className="h-4 w-4" />}
      </Button>
    </div>
  )
}
