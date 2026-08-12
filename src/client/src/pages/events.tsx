/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

"use client"

import { useCallback, useEffect, useMemo, useState } from "react"
import { useNavigate } from "react-router-dom"
import {
  EventsLayout,
  type EventDetailExtras,
  type TournamentEvent,
} from "@videreproject/ui"
import type { ActiveGame } from "@/hooks/use-events"
import { useEvents } from "@/hooks/events-context"
import { getApiUrl } from "@/utils/api-config"

const entryFeeCache = new Map<string, string>()
const entryFeeRequests = new Map<string, Promise<string>>()
const prizesCache = new Map<string, Record<string, string> | null>()
const prizesRequests = new Map<string, Promise<Record<string, string> | null>>()
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

function fetchEntryFee(eventId: string): Promise<string> {
  const cached = entryFeeCache.get(eventId)
  if (cached != null) return Promise.resolve(cached)

  const existing = entryFeeRequests.get(eventId)
  if (existing) return existing

  const request = fetch(getApiUrl(`/api/Events/GetEntryFee/${eventId}`))
    .then(r => (r.ok ? r.text() : "-"))
    .catch(() => "-")
    .then(value => {
      entryFeeCache.set(eventId, value)
      entryFeeRequests.delete(eventId)
      return value
    })

  entryFeeRequests.set(eventId, request)
  return request
}

function fetchPrizes(eventId: string): Promise<Record<string, string> | null> {
  if (prizesCache.has(eventId)) {
    return Promise.resolve(prizesCache.get(eventId) ?? null)
  }

  const existing = prizesRequests.get(eventId)
  if (existing) return existing

  const request = fetch(getApiUrl(`/api/Events/GetPrizes/${eventId}`))
    .then(r => (r.ok ? r.json() : null))
    .catch(() => null)
    .then((value: Record<string, string> | null) => {
      prizesCache.set(eventId, value)
      prizesRequests.delete(eventId)
      return value
    })

  prizesRequests.set(eventId, request)
  return request
}

export default function Events() {
  const navigate = useNavigate()
  const {
    activeGames,
    upcomingGames,
    completedGames,
    loading,
    error,
    hoveredEventId,
    setHoveredEventId,
    selectedEventId,
    setSelectedEventId,
  } = useEvents()

  const [entryFees, setEntryFees] = useState<Record<string, string | undefined>>({})
  const [selectedEventDetails, setSelectedEventDetails] = useState<EventDetailExtras | undefined>()

  const events = useMemo(() => {
    const now = Date.now()
    return [...activeGames, ...upcomingGames, ...completedGames]
      .filter(isRecentCompletedEvent)
      .filter(event => {
        const isPast =
          event.status === "completed" ||
          (getEventStartTime(event) > 0 && getEventStartTime(event) < now)
        if (isPast && (event.minimumPlayers ?? 0) === 0) {
          return false
        }

        const cached = entryFees[event.id] ?? entryFeeCache.get(event.id)
        if (cached !== undefined && (!cached || cached === "-" || cached.trim() === "")) {
          return false
        }
        return true
      })
      .sort((a, b) => getEventStartTime(a) - getEventStartTime(b)) as TournamentEvent[]
  }, [activeGames, upcomingGames, completedGames, entryFees])

  const timelineEvents = useMemo(() => {
    const completedCutoff = Date.now() - TIMELINE_COMPLETED_EVENT_WINDOW_MS
    return events.filter(event => {
      if (event.status !== "completed") return true
      if (!event._rawEndTime) return false
      return new Date(event._rawEndTime).getTime() >= completedCutoff
    })
  }, [events])

  const activeEventIds = useMemo(
    () => new Set(activeGames.map(e => e.id)),
    [activeGames],
  )

  const handlePageRowsChange = useCallback((rows: TournamentEvent[]) => {
    const missing = rows.filter(row => !entryFeeCache.has(row.id))
    if (missing.length === 0) {
      // Sync known cache into state for visible rows
      setEntryFees(current => {
        let changed = false
        const next = { ...current }
        for (const row of rows) {
          const fee = entryFeeCache.get(row.id)
          if (fee !== undefined && next[row.id] !== fee) {
            next[row.id] = fee
            changed = true
          }
        }
        return changed ? next : current
      })
      return
    }

    void Promise.all(
      missing.map(async row => {
        const fee = await fetchEntryFee(row.id)
        return [row.id, fee] as const
      }),
    ).then(results => {
      setEntryFees(current => {
        const next = { ...current }
        for (const [id, fee] of results) {
          next[id] = fee
        }
        return next
      })
    })
  }, [])

  // Fetch detail extras for the selected event (panel)
  useEffect(() => {
    if (!selectedEventId) {
      setSelectedEventDetails(undefined)
      return
    }

    let cancelled = false
    setSelectedEventDetails({ loading: true })

    Promise.all([fetchEntryFee(selectedEventId), fetchPrizes(selectedEventId)]).then(
      ([fee, prizes]) => {
        if (cancelled) return
        setSelectedEventDetails({
          entryFee: fee,
          prizes,
          loading: false,
        })
        setEntryFees(current =>
          current[selectedEventId] === fee ? current : { ...current, [selectedEventId]: fee },
        )
      },
    )

    return () => {
      cancelled = true
    }
  }, [selectedEventId])

  return (
    <EventsLayout
      events={events}
      timelineEvents={timelineEvents}
      loading={loading}
      error={error}
      selectedEventId={selectedEventId}
      onSelectedEventIdChange={setSelectedEventId}
      hoveredEventId={hoveredEventId}
      onHoveredEventIdChange={setHoveredEventId}
      activeEventIds={activeEventIds}
      entryFees={entryFees}
      onPageRowsChange={handlePageRowsChange}
      selectedEventDetails={selectedEventDetails}
      onViewTournament={event => navigate(`/events/${event.id}`)}
    />
  )
}
