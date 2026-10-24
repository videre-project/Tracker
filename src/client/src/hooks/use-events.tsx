/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useEffect, useMemo, useState, useRef, useCallback, type ReactNode } from "react"
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
  state?: TournamentState;
  _rawStartTime?: string;
  _rawEndTime?: string;
}

// Older snapshots identify event structure by name rather than flags.
function inferEventTypeFromStructure(structure?: IEventStructure | string | null): EventType {
  if (!structure) return "league" // League events have no eventStructure
  if (typeof structure === "object") {
    if (structure.isDraft) return "draft"
    if (structure.isSingleElimination) return "elimination"
    if (structure.isSwiss) return "swiss"
    if (structure.isConstructed) return "league"
    if (structure.isLimited) return "draft"
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
  // Unset .NET DateTime values arrive as "0001-01-01..." rather than null.
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

function mapTournamentToActiveGame(t: ITournament): ActiveGame {
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

function shouldIncludeEvent(game: ActiveGame) {
  return game.totalRounds == null || game.totalRounds >= 3
}

export function EventsProvider({ children, liveUpdates = true }: { children: ReactNode; liveUpdates?: boolean }) {
  const [games, setGames] = useState<ActiveGame[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [hoveredEventId, setHoveredEventId] = useState<string | null>(null)
  const [selectedEventId, setSelectedEventId] = useState<string | null>(null)

  const { queryCache, isReady: clientReady, loading: clientLoading } = useClientState()

  // Streams accumulate bursts here; React receives one immutable snapshot per flush.
  const gamesMapRef = useRef(new Map<string, ActiveGame>())
  const flushTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const transitionTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const publishSnapshot = useCallback(() => {
    const allGames = Array.from(gamesMapRef.current.values(), game => ({ ...game }))

    const now = Date.now()
    let nextTransitionMs = Infinity

    for (const game of allGames) {
      if (game.state === "Finished" && game.status !== "completed") {
        game.status = "completed"
      }
      if (game.state && ACTIVE_TOURNAMENT_STATES.includes(game.state)) {
        game.status = "active"
      }
      if (game.status === "scheduled" && game._rawStartTime) {
        const start = new Date(game._rawStartTime).getTime()
        if (now >= start) {
          game.status = "active"
        } else {
          const diff = start - now
          if (diff > 0 && diff < nextTransitionMs) nextTransitionMs = diff
        }
      }
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

    allGames.sort((a, b) => {
      const dateA = a._rawStartTime ? new Date(a._rawStartTime).getTime() : 0
      const dateB = b._rawStartTime ? new Date(b._rawStartTime).getTime() : 0
      return dateA - dateB
    })

    setGames(allGames)
    setLoading(false)
    if (flushTimerRef.current !== null) {
      clearTimeout(flushTimerRef.current)
      flushTimerRef.current = null
    }

    // Start/end boundaries can pass without a new tournament update.
    if (transitionTimerRef.current) {
      clearTimeout(transitionTimerRef.current)
      transitionTimerRef.current = null
    }
    if (nextTransitionMs < Infinity) {
      const delay = Math.min(nextTransitionMs + 100, 2147483647)
      transitionTimerRef.current = setTimeout(() => {
        transitionTimerRef.current = null
        publishSnapshot()
      }, delay)
    }
  }, [])

  const scheduleSnapshot = useCallback(() => {
    if (flushTimerRef.current !== null) return

    flushTimerRef.current = setTimeout(() => {
      flushTimerRef.current = null
      publishSnapshot()
    }, 200)
  }, [publishSnapshot])

  useEffect(() => {
    gamesMapRef.current.clear()
    setGames([])
    setLoading(clientReady)
    setError(null)
    setHoveredEventId(null)
    setSelectedEventId(null)
    return () => {
      if (flushTimerRef.current) clearTimeout(flushTimerRef.current)
      if (transitionTimerRef.current) clearTimeout(transitionTimerRef.current)
      flushTimerRef.current = null
      transitionTimerRef.current = null
    }
  }, [clientReady, queryCache])

  const applyTournamentUpdate = useCallback((t: ITournament) => {
    const game = mapTournamentToActiveGame(t)
    if (!shouldIncludeEvent(game)) {
      gamesMapRef.current.delete(game.id)
      scheduleSnapshot()
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
    scheduleSnapshot()
    setError(null)
  }, [scheduleSnapshot])

  const applyTournamentMessage = useCallback((message: ITournament | ITournament[]) => {
    const tournaments = Array.isArray(message) ? message : [message]
    for (const tournament of tournaments) applyTournamentUpdate(tournament)
  }, [applyTournamentUpdate])

  // Load the catalogue once per client session; watch streams provide later changes.
  useNDJSONStream<ITournament | ITournament[]>({
    url: getApiUrl("/api/Events/GetEventsList?stream=true"),
    restartKey: queryCache,
    enabled: !clientLoading && clientReady,
    onMessage: applyTournamentMessage,
    onEnd: () => {
      publishSnapshot()
    },
    onError: (e) => {
      console.error("Events list stream error:", e.message)
      setError(e.message)
      setLoading(false)
    },
    autoReconnect: false,
  })

  // Keep the snapshot available for navigation; watch changes while an Events page is open.
  useNDJSONStream<ITournament | ITournament[]>({
    url: getApiUrl("/api/Events/WatchTournamentListUpdates"),
    restartKey: queryCache,
    enabled: liveUpdates && !clientLoading && clientReady,
    onMessage: applyTournamentMessage,
    onError: (e) => {
      console.error("Tournament list updates stream error:", e)
    },
    autoReconnect: true,
    reconnectDelay: 2000,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })

  useNDJSONStream<ITournamentPlayerUpdate | ITournamentPlayerUpdate[]>({
    url: getApiUrl("/api/Events/WatchPlayerCount"),
    restartKey: queryCache,
    enabled: liveUpdates && !clientLoading && clientReady,
    onMessage: (update) => {
      const updates = Array.isArray(update) ? update : [update]
      
      for (const item of updates) {
        const eventId = String(item.id)
        const game = gamesMapRef.current.get(eventId)
        if (!game) continue

        if (item.totalRounds !== undefined && item.totalRounds < 3) {
          gamesMapRef.current.delete(eventId)
          scheduleSnapshot()
          continue
        }

        const hasChanges = 
          game.totalPlayers !== item.totalPlayers ||
          game.totalRounds !== item.totalRounds ||
          game._rawEndTime !== item.endTime

        if (hasChanges) {
          gamesMapRef.current.set(eventId, {
            ...game, totalPlayers: item.totalPlayers, totalRounds: item.totalRounds,
            _rawEndTime: item.endTime, endTime: formatTimeShort(item.endTime),
          })
          scheduleSnapshot()
        }
      }
    },
    onError: (e) => {
      console.error("Player count updates stream error:", e)
    },
    autoReconnect: true,
    reconnectDelay: 2000
  })


  const { activeGames, upcomingGames, completedGames } = useMemo(() => ({
    activeGames: clientReady ? games.filter(game => game.status === "active") : [],
    upcomingGames: clientReady ? games.filter(game => game.status === "scheduled") : [],
    completedGames: clientReady ? games.filter(game => game.status === "completed") : [],
  }), [games, clientReady])

  return (
    <EventsContext.Provider value={{ activeGames, upcomingGames, completedGames,
      loading: clientLoading || (clientReady && loading),
      error: !clientLoading && !clientReady ? "MTGO client is not ready" : error,
      hoveredEventId, setHoveredEventId, selectedEventId, setSelectedEventId }}>
      {children}
    </EventsContext.Provider>
  )
}
