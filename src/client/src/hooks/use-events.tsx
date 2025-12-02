// Extend the Window interface for debugging
declare global {
  interface Window {
    __activeEvents?: ActiveGame[];
    __upcomingEvents?: ActiveGame[];
  }
}

import { useEffect, useState, useRef, useCallback } from "react"
import type { ITournament, IEventStructure, ITournamentStateUpdate } from "@/types/api"
import { useClientState } from "./use-client-state"
import { useNDJSONStream } from "./use-ndjson-stream"

// Tournament state type (will be replaced when OpenAPI types are regenerated)
type TournamentState =
  | "NotSet"
  | "Fired"
  | "WaitingToStart"
  | "Drafting"
  | "Deckbuilding"
  | "DeckbuildingDeckSubmitted"
  | "WaitingForFirstRoundToStart"
  | "RoundInProgress"
  | "BetweenRounds"
  | "Finished"

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
  currentRound?: number;
  totalSwissRounds?: number;
  pod?: string;
  startTime?: string;
  endTime?: string;
  timeRemaining?: string; // e.g. '12:34' or '5:00'
  totalPlayers?: number;
  minimumPlayers?: number;
  // Tournament state
  state?: TournamentState;
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

  // First check time boundaries
  if (now < start) return "scheduled"
  if (now > end) return "completed"

  // If we're between start and end time, check tournament state
  const state = (t as any).state as TournamentState | undefined
  if (state) {
    // Only certain states indicate the event is truly active
    const activeStates: TournamentState[] = [
      "Fired",
      "Drafting",
      "Deckbuilding",
      "DeckbuildingDeckSubmitted",
      "WaitingForFirstRoundToStart",
      "RoundInProgress",
      "BetweenRounds"
    ]
    if (activeStates.includes(state)) return "active"
    if (state === "Finished") return "completed"

    // WaitingToStart and other states before the event fires
    if (state === "WaitingToStart" || state === "NotSet") return "scheduled"
  }

  // Default to active if we're between start/end with no clear state
  return "active"
}

export function formatTimeShort(dateStr?: string): string | undefined {
  if (!dateStr) return undefined
  const d = new Date(dateStr)
  // Format as '11:00 AM' (no seconds)
  return d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
}

// Patch: Add _rawStartTime and _rawEndTime for accurate duration calculation
export interface ActiveGameWithRawTimes extends ActiveGame {
  _rawStartTime?: string;
  _rawEndTime?: string;
  eventStructure?: any;
  roundEndTime?: string;
  inPlayoffs?: boolean;
}

function mapTournamentToActiveGame(t: ITournament): ActiveGameWithRawTimes {
  // Filter out invalid DateTime values (C# DateTime.MinValue serializes to 0001-01-01)
  const roundEndTime = (t as any).roundEndTime
  const isValidRoundEndTime = roundEndTime && !roundEndTime.startsWith('0001-01-01')

  return {
    id: String(t.id),
    name: t.description,
    type: inferEventTypeFromStructure((t as any).eventStructure ?? t.format),
    status: inferGameStatus(t),
    format: t.format,
    url: `/events/${t.id}`,
    startTime: t.startTime ? formatTimeShort(t.startTime) : undefined,
    endTime: t.endTime ? formatTimeShort(t.endTime) : undefined,
    totalRounds: t.totalRounds,
    totalPlayers: t.totalPlayers,
    minimumPlayers: t.minimumPlayers,
    _rawStartTime: t.startTime,
    _rawEndTime: t.endTime,
    // Pass through eventStructure for playoff/top 8 display
    eventStructure: (t as any).eventStructure,
    currentRound: (t as any).currentRound,
    roundEndTime: isValidRoundEndTime ? roundEndTime : undefined,
    inPlayoffs: (t as any).inPlayoffs,
    state: (t as any).state,
  }
}

import React, { createContext, useContext } from "react"

interface EventsContextType {
  activeGames: ActiveGame[];
  upcomingGames: ActiveGame[];
  loading: boolean;
  error: string | null;
}

const EventsContext = createContext<EventsContextType | null>(null);

export function EventsProvider({ children }: { children: React.ReactNode }) {
  const [activeGames, setActiveGames] = useState<ActiveGame[]>([])
  const [upcomingGames, setUpcomingGames] = useState<ActiveGame[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Wait for MTGO client to be ready before fetching events
  const { isReady: clientReady, loading: clientLoading } = useClientState()

  // Shared games map that both streams can access
  const gamesMapRef = useRef(new Map<string, ActiveGameWithRawTimes>())
  const retryAttempts = useRef(new Map<string, number>())
  const retryTimeouts = useRef(new Map<string, NodeJS.Timeout>())

  const updateGamesState = useCallback(() => {
    const allGames = Array.from(gamesMapRef.current.values())

    // Sort by start time ascending (chronological)
    allGames.sort((a, b) => {
      const dateA = a._rawStartTime ? new Date(a._rawStartTime).getTime() : 0
      const dateB = b._rawStartTime ? new Date(b._rawStartTime).getTime() : 0
      return dateA - dateB
    })

    const active = allGames.filter(g => g.status === "active")
    const upcoming = allGames.filter(g => g.status === "scheduled")
    setActiveGames(active)
    setUpcomingGames(upcoming)
    if (typeof window !== "undefined") {
      window.__activeEvents = active
      window.__upcomingEvents = upcoming
    }
  }, [])

  // Helper function to fetch tournament state when roundEndTime is missing
  const fetchTournamentState = useCallback(async (tournamentId: string) => {
    try {
      const response = await fetch(`/api/Events/GetTournamentState/${tournamentId}`)

      if (!response.ok) {
        if (response.status === 404) {
          console.log(`Tournament ${tournamentId} not found, stopping retries`)
          return null
        }
        throw new Error(`Failed to fetch tournament state: ${response.status}`)
      }

      const stateUpdate: ITournamentStateUpdate = await response.json()
      return stateUpdate
    } catch (error) {
      console.error(`Error fetching tournament state for ${tournamentId}:`, error)
      return null
    }
  }, [])

  // Retry mechanism to fetch missing roundEndTime
  const retryFetchRoundEndTime = useCallback(async (game: ActiveGameWithRawTimes) => {
    const tournamentId = game.id
    const currentAttempts = retryAttempts.current.get(tournamentId) || 0
    const maxAttempts = 3
    const retryDelays = [2000, 5000, 10000] // 2s, 5s, 10s

    if (currentAttempts >= maxAttempts) {
      console.log(`Max retry attempts reached for tournament ${tournamentId}`)
      return
    }

    // Clear any existing timeout for this tournament
    const existingTimeout = retryTimeouts.current.get(tournamentId)
    if (existingTimeout) {
      clearTimeout(existingTimeout)
    }

    const delay = retryDelays[currentAttempts]
    console.log(`Scheduling retry ${currentAttempts + 1}/${maxAttempts} for tournament ${tournamentId} in ${delay}ms`)

    const timeout = setTimeout(async () => {
      const stateUpdate = await fetchTournamentState(tournamentId)

      if (stateUpdate) {
        const updatedGame = gamesMapRef.current.get(tournamentId)
        if (updatedGame) {
          // Filter out invalid DateTime values
          const roundEndTime = stateUpdate.roundEndTime && !stateUpdate.roundEndTime.startsWith('0001-01-01')
            ? stateUpdate.roundEndTime
            : undefined

          if (roundEndTime) {
            console.log(`Successfully fetched roundEndTime for tournament ${tournamentId}:`, roundEndTime)
            updatedGame.roundEndTime = roundEndTime
            updatedGame.currentRound = stateUpdate.currentRound
            updatedGame.state = stateUpdate.state as TournamentState
            updatedGame.inPlayoffs = stateUpdate.inPlayoffs
            updateGamesState()

            // Clear retry tracking
            retryAttempts.current.delete(tournamentId)
            retryTimeouts.current.delete(tournamentId)
          } else {
            // Still no valid roundEndTime, schedule another retry
            retryAttempts.current.set(tournamentId, currentAttempts + 1)
            retryFetchRoundEndTime(updatedGame)
          }
        }
      } else {
        // Failed to fetch, schedule another retry
        retryAttempts.current.set(tournamentId, currentAttempts + 1)
        const updatedGame = gamesMapRef.current.get(tournamentId)
        if (updatedGame) {
          retryFetchRoundEndTime(updatedGame)
        }
      }
    }, delay)

    retryTimeouts.current.set(tournamentId, timeout)
    retryAttempts.current.set(tournamentId, currentAttempts + 1)
  }, [fetchTournamentState, updateGamesState])

  // Initial events stream
  useNDJSONStream<ITournament>({
    url: "/api/Events/GetEventsList?stream=true",
    enabled: !clientLoading && clientReady,
    onMessage: (t) => {
      const game = mapTournamentToActiveGame(t)

      // Merge with existing game if present, but prefer new data over undefined
      const existing = gamesMapRef.current.get(game.id)
      if (existing) {
        // Keep existing values only if new values are undefined
        game.state = game.state || existing.state
        game.currentRound = game.currentRound || existing.currentRound
        game.roundEndTime = game.roundEndTime || existing.roundEndTime
        game.inPlayoffs = game.inPlayoffs ?? existing.inPlayoffs
        // Don't override status from incoming data - let inferGameStatus decide
      }

      gamesMapRef.current.set(game.id, game)
      updateGamesState()

      // Trigger retry mechanism if roundEndTime is missing for active tournaments
      if (game.status === "active" && !game.roundEndTime) {
        console.log(`Tournament ${game.id} is active but missing roundEndTime, scheduling retry`)
        retryFetchRoundEndTime(game)
      }
    },
    onEnd: () => {
      console.log("Events list fully loaded")
      setLoading(false)
      updateGamesState()
    },
    onError: (e) => {
      console.error("Events list stream error:", e.message)
      setError(e.message)
      setLoading(false)
    },
    autoReconnect: false // One-time load only, no reconnection
  })

  // Stream tournament state updates for active events
  useNDJSONStream<ITournamentStateUpdate>({
    url: "/api/Events/WatchTournamentUpdates",
    enabled: !clientLoading && clientReady,
    onMessage: (update) => {
      const eventId = String(update.id)
      const game = gamesMapRef.current.get(eventId)

      if (!game) {
        // Ignore updates for tournaments we don't have in our list
        console.log(`Received state update for unknown event ${eventId}, skipping`)
        return
      }

      // Check if this update actually changes anything
      const hasChanges =
        game.state !== (update.state as TournamentState) ||
        game.currentRound !== update.currentRound ||
        game.inPlayoffs !== update.inPlayoffs ||
        game.roundEndTime !== update.roundEndTime

      if (!hasChanges) {
        // Ignore redundant updates (common on initial connection)
        console.log(`Ignoring redundant state update for event ${eventId}`)
        return
      }

      console.log(`Updating event ${eventId}:`, update)

      // Filter out invalid DateTime values (C# DateTime.MinValue)
      const roundEndTime = update.roundEndTime && !update.roundEndTime.startsWith('0001-01-01')
        ? update.roundEndTime
        : undefined

      // Update tournament state fields
      game.currentRound = update.currentRound
      game.state = update.state as TournamentState
      game.inPlayoffs = update.inPlayoffs
      game.roundEndTime = roundEndTime

      // Determine status based on time and state
      const now = Date.now()
      const start = game._rawStartTime ? new Date(game._rawStartTime).getTime() : 0
      const end = game._rawEndTime ? new Date(game._rawEndTime).getTime() : 0

      // Check time boundaries first
      if (now < start) {
        game.status = "scheduled"
      } else if (now > end) {
        game.status = "completed"
      } else {
        // Between start and end - check state
        const activeStates: TournamentState[] = [
          "Fired",
          "Drafting",
          "Deckbuilding",
          "DeckbuildingDeckSubmitted",
          "WaitingForFirstRoundToStart",
          "RoundInProgress",
          "BetweenRounds"
        ]

        if (update.state && activeStates.includes(update.state as TournamentState)) {
          game.status = "active"
        } else if (update.state === "Finished") {
          game.status = "completed"
        } else if (update.state === "WaitingToStart" || update.state === "NotSet") {
          game.status = "scheduled"
        } else {
          // Default to active if between start/end with unclear state
          game.status = "active"
        }
      }

      // Calculate time remaining for display
      if (update.roundEndTime) {
        const now = Date.now()
        const end = new Date(update.roundEndTime).getTime()
        const ms = Math.max(0, end - now)
        const min = Math.floor(ms / 60000)
        const sec = Math.floor((ms % 60000) / 1000)
        game.timeRemaining = `${min}:${sec.toString().padStart(2, '0')}`
      } else {
        game.timeRemaining = undefined
      }

      // Update the shared map
      gamesMapRef.current.set(eventId, game)

      // Update React state
      updateGamesState()
    },
    onError: (e) => {
      console.error("Tournament state updates stream error:", e)
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
    }
  }, [clientLoading, clientReady])

  return (
    <EventsContext.Provider value={{ activeGames, upcomingGames, loading, error }}>
      {children}
    </EventsContext.Provider>
  )
}

/**
 * Fetch all events once and return both active and upcoming games
 * This is the recommended hook to use instead of separate hooks
 */
export function useEvents() {
  const context = useContext(EventsContext)
  if (!context) {
    throw new Error("useEvents must be used within an EventsProvider")
  }
  return context
}
