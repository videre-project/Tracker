/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

// Extend the Window interface for debugging
declare global {
  interface Window {
    __activeEvents?: ActiveGame[];
    __upcomingEvents?: ActiveGame[];
    __completedEvents?: ActiveGame[];
  }
}

import { useEffect, useState, useRef, useCallback, type ReactNode } from "react"
import { useClientState } from "./use-client-state"
import { useNDJSONStream } from "./use-ndjson-stream"
import { EventsContext } from "./events-context"
import { getApiUrl } from "../utils/api-config"
import { formatTimeShort, normalizeFormatName } from "../utils/event-format"
import type {
  IEventStructure,
  ITournament,
  ITournamentPlayerUpdate,
  ITournamentStateUpdate,
  TournamentState,
} from "@/types/api"


// Tournament state type (will be replaced when OpenAPI types are regenerated)
export type { TournamentState } from "@/types/api"

const ACTIVE_TOURNAMENT_STATES: TournamentState[] = [
  "Fired",
  "Drafting",
  "Deckbuilding",
  "DeckbuildingDeckSubmitted",
  "WaitingForFirstRoundToStart",
  "RoundInProgress",
  "BetweenRounds",
]
const PRE_ROUND_COUNTDOWN_MS = 2 * 60 * 1000

export type EventType = "league" | "swiss" | "elimination" | "draft" | "unknown"
export type GameStatus = "active" | "paused" | "scheduled" | "completed"

export interface ActiveGame {
  id: string;
  name: string;
  type: EventType;
  status: GameStatus;
  format: string;
  url: string;
  deck?: string;
  wins?: number;
  losses?: number;
  totalRounds?: number;
  roundNumber?: number;
  totalSwissRounds?: number;
  pod?: string;
  startTime?: string;
  endTime?: string;
  timeRemaining?: string; // e.g. '12:34' or '5:00'
  totalPlayers?: number;
  minimumPlayers?: number;
  roundEndTime?: string;
  inPlayoffs?: boolean;
  hasPlayoffs?: boolean;
  eventStructure?: IEventStructure;
  activePlayerNames?: string[];
  playerNamesWithMatchesInProgress?: string[];
  // Tournament state
  state?: TournamentState;
  _rawStartTime?: string;
  _rawEndTime?: string;
}

// Use EventStructure if available, fallback to format string
// League events do not have eventStructure; treat as 'league' for now.
// When ILeague is added to the API, update this logic to handle both types.
function inferEventTypeFromStructure(structure?: IEventStructure | string | null): EventType {
  if (!structure) return "league" // League events have no eventStructure
  if (typeof structure === "object") {
    if (structure.isDraft) return "draft"
    if (structure.isSingleElimination) return "elimination"
    if (structure.isSwiss) return "swiss"
    if (structure.isConstructed) return "league" // fallback for constructed
    if (structure.isLimited) return "draft" // fallback for limited
    // fallback to name if present
    const s = structure.name || ""
    if (/league/i.test(s)) return "league"
    if (/swiss/i.test(s)) return "swiss"
    if (/elim/i.test(s)) return "elimination"
    if (/draft/i.test(s)) return "draft"
    return "unknown"
  } else if (typeof structure === "string") {
    const s = structure
    if (/league/i.test(s)) return "league"
    if (/swiss/i.test(s)) return "swiss"
    if (/elim/i.test(s)) return "elimination"
    if (/draft/i.test(s)) return "draft"
    return "unknown"
  }
  return "unknown"
}

function inferGameStatus(t: ITournament): GameStatus {
  const now = Date.now()
  const start = t.startTime ? new Date(t.startTime).getTime() : 0
  const end = t.endTime ? new Date(t.endTime).getTime() : 0
  const state = t.state

  if (state) {
    if (state === "Finished") return "completed"
    if (ACTIVE_TOURNAMENT_STATES.includes(state)) return "active"

    // WaitingToStart and other states before the event fires
    if (state === "WaitingToStart" || state === "NotSet") return "scheduled"
  }

  if (now < start) return "scheduled"
  if (now > end) return "completed"

  return "active"
}

function inferStatusFromState(current: GameStatus, state?: TournamentState): GameStatus {
  if (state === "Finished") return "completed"
  if (state && ACTIVE_TOURNAMENT_STATES.includes(state)) return "active"
  if (state === "WaitingToStart" || state === "NotSet") return "scheduled"
  return current
}

export interface ActiveGameWithRawTimes extends ActiveGame {
  _rawStartTime?: string;
  _rawEndTime?: string;
  eventStructure?: IEventStructure;
  roundEndTime?: string;
  inPlayoffs?: boolean;
}

function hasRoundCountdown(state?: TournamentState): boolean {
  return state === "RoundInProgress" ||
    state === "BetweenRounds" ||
    state === "WaitingForFirstRoundToStart" ||
    state === "Deckbuilding" ||
    state === "DeckbuildingDeckSubmitted"
}

function hasPreRoundCountdown(state?: TournamentState): boolean {
  return state === "BetweenRounds" ||
    state === "WaitingForFirstRoundToStart"
}

function getRoundEndTimeForState(state?: TournamentState, roundEndTime?: string): string | undefined {
  if (!hasRoundCountdown(state) ||
      !roundEndTime ||
      roundEndTime.startsWith('0001-01-01')) {
    return undefined
  }

  const timestamp = new Date(roundEndTime).getTime()
  return Number.isFinite(timestamp) && timestamp > Date.now()
    ? roundEndTime
    : undefined
}

function mergeRoundNumber(incoming?: number, existing?: number): number | undefined {
  if (incoming == null) return existing
  if (existing == null) return incoming
  return Math.max(incoming, existing)
}

function mergeRoundScopedMatchPlayers(
  incoming: string[] | undefined,
  existing: string[] | undefined,
  state: TournamentState | undefined,
  roundChanged: boolean
) {
  if (state !== "RoundInProgress" || roundChanged) return []
  return incoming ?? existing
}

function mapTournamentToActiveGame(t: ITournament): ActiveGameWithRawTimes {
  // Filter out invalid DateTime values (C# DateTime.MinValue serializes to 0001-01-01)
  const update = t as ITournament & Partial<ITournamentStateUpdate>
  const state = update.state
  const roundEndTime = update.roundEndTime
  const format = normalizeFormatName(t.format)

  return {
    id: String(t.id),
    name: t.description ?? "Untitled event",
    type: inferEventTypeFromStructure(t.eventStructure ?? format),
    status: inferGameStatus(t),
    format,
    url: `/events/${t.id}`,
    startTime: t.startTime ? formatTimeShort(t.startTime) : undefined,
    endTime: t.endTime ? formatTimeShort(t.endTime) : undefined,
    totalRounds: t.totalRounds,
    totalPlayers: t.totalPlayers,
    minimumPlayers: t.minimumPlayers,
    _rawStartTime: t.startTime,
    _rawEndTime: t.endTime,
    // Pass through eventStructure for playoff/top 8 display
    eventStructure: t.eventStructure,
    hasPlayoffs: t.eventStructure?.hasPlayoffs,
    roundNumber: update.roundNumber,
    roundEndTime: getRoundEndTimeForState(state, roundEndTime),
    inPlayoffs: update.inPlayoffs,
    activePlayerNames: update.activePlayerNames ?? undefined,
    playerNamesWithMatchesInProgress:
      update.playerNamesWithMatchesInProgress ?? undefined,
    state,
  }
}

function shouldIncludeEvent(game: ActiveGameWithRawTimes) {
  return game.totalRounds == null || game.totalRounds >= 3
}

export function EventsProvider({ children }: { children: ReactNode }) {
  const [activeGames, setActiveGames] = useState<ActiveGame[]>([])
  const [upcomingGames, setUpcomingGames] = useState<ActiveGame[]>([])
  const [completedGames, setCompletedGames] = useState<ActiveGame[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [hoveredEventId, setHoveredEventId] = useState<string | null>(null)
  const [selectedEventId, setSelectedEventId] = useState<string | null>(null)

  // Wait for MTGO client to be ready before fetching events
  const { isReady: clientReady, loading: clientLoading } = useClientState()

  // Shared games map that both streams can access
  const gamesMapRef = useRef(new Map<string, ActiveGameWithRawTimes>())
  
  // Dirty flag to track if we need to update React state
  const isDirtyRef = useRef(false)
  const flushTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const transitionTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const updateGamesState = useCallback(() => {
    const allGames = Array.from(gamesMapRef.current.values())

    // Reconcile state/status inconsistencies before filtering
    const now = Date.now()
    let nextTransitionMs = Infinity

    for (const game of allGames) {
      game.format = normalizeFormatName(game.format)
      if (game.state === "Finished" && game.status !== "completed") {
        game.status = "completed"
      }
      if (game.state && ACTIVE_TOURNAMENT_STATES.includes(game.state)) {
        game.status = "active"
      }
      // Promote scheduled events whose start time has passed
      if (game.status === "scheduled" && game._rawStartTime) {
        const start = new Date(game._rawStartTime).getTime()
        if (now >= start) {
          game.status = "active"
        } else {
          const diff = start - now
          if (diff > 0 && diff < nextTransitionMs) nextTransitionMs = diff
        }
      }
      // Demote active events whose end time has passed
      if (game.status === "active" && !game.state && game._rawEndTime) {
        const end = new Date(game._rawEndTime).getTime()
        if (now > end) {
          game.status = "completed"
        } else {
          const diff = end - now
          if (diff > 0 && diff < nextTransitionMs) nextTransitionMs = diff
        }
      }
    }

    // Sort by start time ascending (chronological)
    allGames.sort((a, b) => {
      const dateA = a._rawStartTime ? new Date(a._rawStartTime).getTime() : 0
      const dateB = b._rawStartTime ? new Date(b._rawStartTime).getTime() : 0
      return dateA - dateB
    })

    const active = allGames.filter(g => g.status === "active")
    const upcoming = allGames.filter(g => g.status === "scheduled")
    const completed = allGames.filter(g => g.status === "completed")
    setActiveGames(active)
    setUpcomingGames(upcoming)
    setCompletedGames(completed)
    if (typeof window !== "undefined") {
      window.__activeEvents = active
      window.__upcomingEvents = upcoming
      window.__completedEvents = completed
    }
    
    // Reset dirty flag
    isDirtyRef.current = false

    // Schedule targeted timer for the next closest status transition
    if (transitionTimerRef.current) {
      clearTimeout(transitionTimerRef.current)
      transitionTimerRef.current = null
    }
    if (nextTransitionMs < Infinity) {
      const delay = Math.min(nextTransitionMs + 100, 2147483647)
      transitionTimerRef.current = setTimeout(() => {
        transitionTimerRef.current = null
        isDirtyRef.current = true
        updateGamesState()
      }, delay)
    }
  }, [])

  const markDirtyAndScheduleFlush = useCallback(() => {
    isDirtyRef.current = true
    if (flushTimerRef.current !== null) return

    flushTimerRef.current = setTimeout(() => {
      flushTimerRef.current = null
      if (isDirtyRef.current) {
        updateGamesState()
      }
    }, 200)
  }, [updateGamesState])

  // Clean up any pending timers on unmount
  useEffect(() => {
    return () => {
      if (flushTimerRef.current) clearTimeout(flushTimerRef.current)
      if (transitionTimerRef.current) clearTimeout(transitionTimerRef.current)
    }
  }, [])

  const applyTournamentUpdate = useCallback((t: ITournament) => {
    const game = mapTournamentToActiveGame(t)
    if (!shouldIncludeEvent(game)) {
      gamesMapRef.current.delete(game.id)
      markDirtyAndScheduleFlush()
      return
    }

    const existing = gamesMapRef.current.get(game.id)
    if (existing) {
      const state = game.state ?? existing.state
      const roundNumber = mergeRoundNumber(game.roundNumber, existing.roundNumber)
      const incomingIsOlderRound =
        game.roundNumber != null &&
        existing.roundNumber != null &&
        game.roundNumber < existing.roundNumber
      const shouldKeepExistingRoundEndTime =
        hasRoundCountdown(state) &&
        state === existing.state &&
        roundNumber != null &&
        roundNumber === existing.roundNumber
      const incomingRoundEndTime =
        incomingIsOlderRound ? undefined : game.roundEndTime
      const roundChanged =
        roundNumber != null &&
        existing.roundNumber != null &&
        roundNumber !== existing.roundNumber

      game.state = state
      game.roundNumber = roundNumber
      game.roundEndTime = hasRoundCountdown(state)
        ? (incomingRoundEndTime ??
            (shouldKeepExistingRoundEndTime ? existing.roundEndTime : undefined) ??
            (hasPreRoundCountdown(state)
              ? new Date(Date.now() + PRE_ROUND_COUNTDOWN_MS).toISOString()
              : undefined))
        : undefined
      game.inPlayoffs = game.inPlayoffs ?? existing.inPlayoffs
      game.activePlayerNames = game.activePlayerNames ?? existing.activePlayerNames
      game.playerNamesWithMatchesInProgress =
        mergeRoundScopedMatchPlayers(
          incomingIsOlderRound ? undefined : game.playerNamesWithMatchesInProgress,
          existing.playerNamesWithMatchesInProgress,
          state,
          roundChanged)
      game.status = inferStatusFromState(game.status, state)
    } else if (hasPreRoundCountdown(game.state) && !game.roundEndTime) {
      game.roundEndTime =
        new Date(Date.now() + PRE_ROUND_COUNTDOWN_MS).toISOString()
    }

    gamesMapRef.current.set(game.id, game)
    markDirtyAndScheduleFlush()
    setLoading(false)
  }, [markDirtyAndScheduleFlush])

  const applyTournamentMessage = useCallback((message: ITournament | ITournament[]) => {
    const tournaments = Array.isArray(message) ? message : [message]
    for (const tournament of tournaments) applyTournamentUpdate(tournament)
  }, [applyTournamentUpdate])

  // Initial events stream
  useNDJSONStream<ITournament | ITournament[]>({
    url: getApiUrl("/api/Events/GetEventsList?stream=true"),
    enabled: !clientLoading && clientReady,
    onMessage: applyTournamentMessage,
    onEnd: () => {
      setLoading(false)
      // Force immediate update on completion
      updateGamesState()
    },
    onError: (e) => {
      console.error("Events list stream error:", e.message)
      setError(e.message)
      setLoading(false)
    },
    autoReconnect: false // One-time load only, no reconnection
  })

  // Stream event-driven tournament state updates for the sidebar.
  useNDJSONStream<ITournament | ITournament[]>({
    url: getApiUrl("/api/Events/WatchTournamentListUpdates"),
    enabled: !clientLoading && clientReady,
    onMessage: applyTournamentMessage,
    onError: (e) => {
      console.error("Tournament list updates stream error:", e)
    },
    autoReconnect: true,
    reconnectDelay: 2000,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })

  // Stream player count updates for all events
  useNDJSONStream<ITournamentPlayerUpdate>({
    url: getApiUrl("/api/Events/WatchPlayerCount"),
    enabled: !clientLoading && clientReady,
    onMessage: (update) => {
      // The update could be a single object or an array (due to controller's NdjsonStream handling)
      const updates = Array.isArray(update) ? update : [update]
      
      for (const item of updates) {
        const eventId = String(item.id)
        const game = gamesMapRef.current.get(eventId)
        if (!game) continue

        if (item.totalRounds < 3) {
          gamesMapRef.current.delete(eventId)
          markDirtyAndScheduleFlush()
          continue
        }

        const hasChanges = 
          game.totalPlayers !== item.totalPlayers ||
          game.totalRounds !== item.totalRounds ||
          game._rawEndTime !== item.endTime

        if (hasChanges) {
          game.totalPlayers = item.totalPlayers
          game.totalRounds = item.totalRounds
          game._rawEndTime = item.endTime
          game.endTime = formatTimeShort(item.endTime)
          
          markDirtyAndScheduleFlush()
        }
      }
    },
    onError: (e) => {
      console.error("Player count updates stream error:", e)
    },
    autoReconnect: true,
    reconnectDelay: 2000
  })


  // Set loading state based on client readiness
  useEffect(() => {
    if (clientLoading) {
      setLoading(true)
    } else if (!clientReady) {
      setLoading(false)
      setError("MTGO client is not ready")
    } else {
      setError(null)
    }
  }, [clientLoading, clientReady])

  return (
    <EventsContext.Provider value={{ activeGames, upcomingGames, completedGames, loading, error, hoveredEventId, setHoveredEventId, selectedEventId, setSelectedEventId }}>
      {children}
    </EventsContext.Provider>
  )
}
