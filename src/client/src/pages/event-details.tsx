/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import { useParams, useNavigate } from "react-router-dom"
import {
  EventDetailsLayout,
  type StandingEntry,
  type TournamentEvent,
} from "@videreproject/ui"
import type { ActiveGame } from "@/hooks/use-events"
import { useEvents } from "@/hooks/events-context"
import { useClientState } from "@/hooks/use-client-state"
import { useNDJSONStream } from "@/hooks/use-ndjson-stream"
import { getApiUrl } from "@/utils/api-config"
import type { ITournamentStateUpdate } from "@/types/api"

function useCountdown(targetTime?: string) {
  const [timeLeft, setTimeLeft] = useState<string | null>(null)

  useEffect(() => {
    if (!targetTime) {
      setTimeLeft(null)
      return
    }

    const calculateTimeLeft = () => {
      const ms = Math.max(0, new Date(targetTime).getTime() - Date.now())
      if (ms === 0) return null

      const totalSeconds = Math.floor(ms / 1000)
      const hours = Math.floor(totalSeconds / 3600)
      const minutes = Math.floor((totalSeconds % 3600) / 60)
      const seconds = totalSeconds % 60

      if (hours > 0) return `${hours}h ${minutes}m ${seconds}s`
      if (minutes > 0) return `${minutes}m ${seconds}s`
      return `${seconds}s`
    }

    setTimeLeft(calculateTimeLeft())
    const interval = setInterval(() => {
      setTimeLeft(calculateTimeLeft())
    }, 1000)

    return () => clearInterval(interval)
  }, [targetTime])

  return timeLeft
}

function isValidRoundEndTime(roundEndTime?: string): roundEndTime is string {
  return Boolean(roundEndTime && !roundEndTime.startsWith("0001-01-01"))
}

function getNewestRoundEndTime(current?: string, incoming?: string) {
  if (!isValidRoundEndTime(current)) return isValidRoundEndTime(incoming) ? incoming : undefined
  if (!isValidRoundEndTime(incoming)) return current

  return new Date(incoming).getTime() >= new Date(current).getTime() ? incoming : current
}

function hasRoundCountdown(state?: ActiveGame["state"]) {
  return (
    state === "RoundInProgress" ||
    state === "BetweenRounds" ||
    state === "WaitingForFirstRoundToStart" ||
    state === "Deckbuilding" ||
    state === "DeckbuildingDeckSubmitted"
  )
}

function getRoundEndTimeForState(
  state?: ActiveGame["state"],
  current?: string,
  incoming?: string,
) {
  return hasRoundCountdown(state) ? getNewestRoundEndTime(current, incoming) : undefined
}

function mergeRoundNumber(incoming?: number, existing?: number): number | undefined {
  if (incoming == null) return existing
  if (existing == null) return incoming
  return Math.max(incoming, existing)
}

function mergeRoundScopedMatchPlayers(
  incoming: string[] | null | undefined,
  existing: string[] | undefined,
  state: ActiveGame["state"] | undefined,
  roundChanged: boolean,
) {
  if (state !== "RoundInProgress" || roundChanged) return []
  return incoming ?? existing
}

function useStandings(
  eventId: string | null,
  enabled: boolean,
  liveUpdates: boolean,
  manualRefreshRevision: number,
) {
  const [standings, setStandings] = useState<StandingEntry[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [lastUpdatedAt, setLastUpdatedAt] = useState<Date | null>(null)
  const { isReady: clientReady } = useClientState()

  const mapRef = useRef(new Map<string, StandingEntry>())
  const isDirtyRef = useRef(false)
  const snapshotRequestRef = useRef(0)
  const eventIdRef = useRef(eventId)
  eventIdRef.current = eventId

  const flushStandings = useCallback(() => {
    const sorted = Array.from(mapRef.current.values()).sort((a, b) => a.rank - b.rank)
    setStandings(sorted)
    setLastUpdatedAt(new Date())
    setLoading(false)
    setError(null)
  }, [])

  useNDJSONStream<StandingEntry>({
    url: getApiUrl(`/api/Events/WatchStandings/${eventId}`),
    enabled: enabled && liveUpdates && clientReady && !!eventId,
    onMessage: entry => {
      mapRef.current.set(entry.player, entry)
      isDirtyRef.current = true
    },
    onError: e => {
      setError(e.message)
      setLoading(false)
    },
    autoReconnect: liveUpdates,
    reconnectDelay: 2000,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })

  const refreshSnapshot = useCallback(async () => {
    if (!enabled || !eventId || !clientReady) return

    const requestId = ++snapshotRequestRef.current
    const requestEventId = eventId
    try {
      const response = await fetch(getApiUrl(`/api/Events/GetStandings/${eventId}`))
      if (!response.ok) {
        throw new Error(
          `Failed to refresh standings: ${response.status} ${response.statusText}`,
        )
      }

      const entries = (await response.json()) as StandingEntry[]
      if (requestId !== snapshotRequestRef.current || requestEventId !== eventIdRef.current) {
        return
      }

      mapRef.current.clear()
      for (const entry of entries) {
        mapRef.current.set(entry.player, entry)
      }
      isDirtyRef.current = false
      flushStandings()
    } catch (e) {
      if (requestId === snapshotRequestRef.current && requestEventId === eventIdRef.current) {
        setError(e instanceof Error ? e.message : String(e))
      }
    }
  }, [clientReady, enabled, eventId, flushStandings])

  useEffect(() => {
    if (!liveUpdates) {
      void refreshSnapshot()
    }
  }, [liveUpdates, refreshSnapshot])

  useEffect(() => {
    if (!liveUpdates || manualRefreshRevision <= 0) return
    void refreshSnapshot()
  }, [liveUpdates, manualRefreshRevision, refreshSnapshot])

  useEffect(() => {
    if (!enabled || !eventId || !liveUpdates) return
    const interval = setInterval(() => {
      if (isDirtyRef.current) {
        isDirtyRef.current = false
        flushStandings()
      }
    }, 200)
    return () => clearInterval(interval)
  }, [enabled, eventId, flushStandings, liveUpdates])

  useEffect(() => {
    mapRef.current.clear()
    isDirtyRef.current = false
    setStandings([])
    setError(null)
    setLastUpdatedAt(null)
    if (eventId && enabled) setLoading(true)
  }, [eventId, enabled])

  return { standings, loading, error, lastUpdatedAt }
}

function getPhase(event: ActiveGame): "pre" | "active" | "finished" {
  if (event.status === "completed") return "finished"
  if (event.status === "active") return "active"
  return "pre"
}

export default function EventDetails() {
  const { eventId } = useParams<{ eventId: string }>()
  const navigate = useNavigate()
  const { activeGames, upcomingGames, completedGames, loading: eventsLoading } = useEvents()
  const { isReady: clientReady } = useClientState()
  const [liveEvent, setLiveEvent] = useState<ActiveGame | null>(null)
  const [standingsRefreshRevision, setStandingsRefreshRevision] = useState(0)
  const lastPendingStandingsRefreshKeyRef = useRef<string | null>(null)
  const latestRoundNumberRef = useRef<number | undefined>(undefined)

  const event = useMemo(() => {
    return (
      [...activeGames, ...upcomingGames, ...completedGames].find(e => e.id === eventId) ?? null
    )
  }, [activeGames, upcomingGames, completedGames, eventId])

  const eventRevision = event
    ? [
        event.id,
        event.status,
        event.state ?? "",
        event.roundNumber ?? "",
        event.roundEndTime ?? "",
        event.inPlayoffs ?? "",
        event.timeRemaining ?? "",
        event.activePlayerNames?.join("|") ?? "",
        event.playerNamesWithMatchesInProgress?.join("|") ?? "",
        event.totalPlayers ?? "",
        event.totalRounds ?? "",
        event.minimumPlayers ?? "",
        event._rawEndTime ?? "",
      ].join("|")
    : ""

  useEffect(() => {
    if (!event) {
      setLiveEvent(null)
      return
    }

    setLiveEvent(prev => {
      if (!prev || prev.id !== event.id) {
        const roundNumber = mergeRoundNumber(event.roundNumber, latestRoundNumberRef.current)
        latestRoundNumberRef.current = roundNumber
        return { ...event, roundNumber }
      }

      const state = event.state ?? prev.state
      const roundNumber = mergeRoundNumber(
        event.roundNumber,
        mergeRoundNumber(prev.roundNumber, latestRoundNumberRef.current),
      )
      latestRoundNumberRef.current = roundNumber
      const eventIsOlderRound =
        event.roundNumber != null &&
        prev.roundNumber != null &&
        event.roundNumber < prev.roundNumber
      const currentRoundEndTime =
        roundNumber != null && roundNumber === prev.roundNumber ? prev.roundEndTime : undefined
      const roundChanged =
        roundNumber != null && prev.roundNumber != null && roundNumber !== prev.roundNumber

      return {
        ...event,
        state,
        roundNumber,
        roundEndTime: getRoundEndTimeForState(
          state,
          currentRoundEndTime,
          eventIsOlderRound ? undefined : event.roundEndTime,
        ),
        inPlayoffs: event.inPlayoffs ?? prev.inPlayoffs,
        activePlayerNames: event.activePlayerNames ?? prev.activePlayerNames,
        playerNamesWithMatchesInProgress: mergeRoundScopedMatchPlayers(
          eventIsOlderRound ? undefined : event.playerNamesWithMatchesInProgress,
          prev.playerNamesWithMatchesInProgress,
          state,
          roundChanged,
        ),
        timeRemaining: hasRoundCountdown(state)
          ? (event.timeRemaining ?? prev.timeRemaining)
          : undefined,
      }
    })
  }, [event, eventRevision])

  useEffect(() => {
    lastPendingStandingsRefreshKeyRef.current = null
    latestRoundNumberRef.current = event?.roundNumber
    setStandingsRefreshRevision(0)
  }, [eventId, event?.roundNumber])

  const displayEvent = liveEvent ?? event
  const phase = displayEvent ? getPhase(displayEvent) : "pre"
  const hasCountdown = hasRoundCountdown(displayEvent?.state)
  const roundCountdown = useCountdown(hasCountdown ? displayEvent?.roundEndTime : undefined)
  const timerText = hasCountdown ? (roundCountdown ?? displayEvent?.timeRemaining) : undefined

  const applyTournamentStateUpdate = useCallback(
    (update: ITournamentStateUpdate) => {
      if (String(update.id) !== eventId) return

      const updateRoundNumber = mergeRoundNumber(update.roundNumber, latestRoundNumberRef.current)
      latestRoundNumberRef.current = updateRoundNumber

      const pendingPlayerNames = update.playerNamesWithMatchesInProgress ?? []
      const pendingStandingsRefreshKey = [
        update.state ?? "",
        updateRoundNumber ?? "",
        pendingPlayerNames.join("|"),
      ].join("::")

      const hasPendingMatches = pendingPlayerNames.length > 0

      if (!hasPendingMatches) {
        lastPendingStandingsRefreshKeyRef.current = null
      } else if (lastPendingStandingsRefreshKeyRef.current !== pendingStandingsRefreshKey) {
        lastPendingStandingsRefreshKeyRef.current = pendingStandingsRefreshKey
        setStandingsRefreshRevision(revision => revision + 1)
      }

      setLiveEvent(prev => {
        if (!prev) return prev

        const nextState = update.state as ActiveGame["state"]
        const roundNumber = mergeRoundNumber(updateRoundNumber, prev.roundNumber)
        const updateIsOlderRound =
          update.roundNumber != null &&
          prev.roundNumber != null &&
          update.roundNumber < prev.roundNumber
        const hasSameRound = hasRoundCountdown(prev.state) && prev.roundNumber === roundNumber
        const roundEndTime = hasRoundCountdown(nextState)
          ? !updateIsOlderRound && isValidRoundEndTime(update.roundEndTime)
            ? update.roundEndTime
            : hasSameRound
              ? prev.roundEndTime
              : undefined
          : undefined
        const roundChanged =
          roundNumber != null && prev.roundNumber != null && roundNumber !== prev.roundNumber
        const next: ActiveGame = {
          ...prev,
          state: nextState,
          roundNumber,
          roundEndTime,
          inPlayoffs: update.inPlayoffs,
          activePlayerNames: update.activePlayerNames ?? prev.activePlayerNames,
          playerNamesWithMatchesInProgress: mergeRoundScopedMatchPlayers(
            updateIsOlderRound ? undefined : update.playerNamesWithMatchesInProgress,
            prev.playerNamesWithMatchesInProgress,
            nextState,
            roundChanged,
          ),
          timeRemaining: undefined,
        }

        if (roundEndTime) {
          const ms = Math.max(0, new Date(roundEndTime).getTime() - Date.now())
          const min = Math.floor(ms / 60000)
          const sec = Math.floor((ms % 60000) / 1000)
          next.timeRemaining = `${min}:${sec.toString().padStart(2, "0")}`
        }

        if (update.state === "Finished") {
          next.status = "completed"
        } else if (update.state === "WaitingToStart" || update.state === "NotSet") {
          next.status = "scheduled"
        } else {
          next.status = "active"
        }

        return next
      })
    },
    [eventId],
  )

  useNDJSONStream<ITournamentStateUpdate | ITournamentStateUpdate[]>({
    url: getApiUrl(`/api/Events/WatchTournamentUpdates/${eventId}`),
    enabled: !!eventId && clientReady && phase === "active",
    onMessage: message => {
      const updates = Array.isArray(message) ? message : [message]
      for (const update of updates) applyTournamentStateUpdate(update)
    },
    onError: e => {
      console.error("Tournament state update stream error:", e)
    },
    autoReconnect: true,
    reconnectDelay: 2000,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })

  const {
    standings,
    loading: standingsLoading,
    error: standingsError,
    lastUpdatedAt,
  } = useStandings(
    phase !== "pre" ? (eventId ?? null) : null,
    phase !== "pre",
    phase === "active",
    standingsRefreshRevision,
  )

  return (
    <EventDetailsLayout
      event={(displayEvent as TournamentEvent | null) ?? null}
      standings={standings}
      eventsLoading={eventsLoading}
      standingsLoading={standingsLoading}
      standingsError={standingsError}
      lastStandingsUpdatedAt={lastUpdatedAt}
      timerText={timerText ?? null}
      playerNamesWithMatchesInProgress={displayEvent?.playerNamesWithMatchesInProgress}
      onBack={() => navigate("/events")}
    />
  )
}
