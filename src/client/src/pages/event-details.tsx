import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import { useParams, useNavigate } from "react-router-dom"
import { ArrowLeft, Users, Trophy, Clock, Swords } from "lucide-react"
import { ColumnDef } from "@tanstack/react-table"
import { DataTable } from "@/components/ui/data-table"
import { Button } from "@/components/ui/button"
import { Badge } from "@/components/ui/badge"
import { Skeleton } from "@/components/ui/skeleton"
import { useEvents, ActiveGame } from "@/hooks/use-events"
import { useClientState } from "@/hooks/use-client-state"
import { useNDJSONStream } from "@/hooks/use-ndjson-stream"
import { getApiUrl } from "@/utils/api-config"
import { cn } from "@/lib/utils"
import type { ITournamentStateUpdate } from "@/types/api"

// Matches the runtime shape from SerializeAs<IStandingResult> + Player name Zip
interface StandingEntry {
  rank: number
  player: string
  points: number
  record: string
  opponentMatchWinPercentage: string
  gameWinPercentage: string
  opponentGameWinPercentage: string
}

// --- Format color dots ---

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

// --- Status helpers ---

type TournamentPhase = "pre" | "active" | "finished"

function getPhase(event: ActiveGame): TournamentPhase {
  if (event.status === "completed") return "finished"
  if (event.status === "active") return "active"
  return "pre"
}

function getStatusLabel(event: ActiveGame): string {
  const state = event.state
  if (!state || state === "NotSet") return "Scheduled"
  const labels: Record<string, string> = {
    WaitingToStart: "Waiting to Start",
    Fired: "Fired",
    Drafting: "Drafting",
    Deckbuilding: "Deckbuilding",
    DeckbuildingDeckSubmitted: "Deckbuilding",
    WaitingForFirstRoundToStart: "Starting Soon",
    RoundInProgress: `Round ${event.roundNumber ?? "?"} In Progress`,
    BetweenRounds: `Between Rounds (${event.roundNumber ?? "?"})`,
    Finished: "Finished",
  }
  return labels[state] ?? state
}

function getStatusVariant(event: ActiveGame): "default" | "secondary" | "success" | "warning" {
  const phase = getPhase(event)
  if (phase === "finished") return "success"
  if (phase === "active") return "warning"
  return "secondary"
}

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
  return state === "RoundInProgress" ||
    state === "BetweenRounds" ||
    state === "WaitingForFirstRoundToStart" ||
    state === "Deckbuilding" ||
    state === "DeckbuildingDeckSubmitted"
}

function getRoundEndTimeForState(state?: ActiveGame["state"], current?: string, incoming?: string) {
  return hasRoundCountdown(state)
    ? getNewestRoundEndTime(current, incoming)
    : undefined
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
  roundChanged: boolean
) {
  if (state !== "RoundInProgress" || roundChanged) return []
  return incoming ?? existing
}

function getRecordRoundCount(record: string | null | undefined): number | null {
  const parts = record?.match(/\d+/g)
  if (!parts?.length) return null
  return parts.reduce((sum, part) => sum + Number(part), 0)
}

function hasCurrentRoundMatchInProgress(
  standing: StandingEntry,
  state: ActiveGame["state"] | undefined,
  roundNumber: number | undefined,
  playerNamesWithMatchesInProgress: Set<string>
) {
  const recordRoundCount = getRecordRoundCount(standing.record)
  if (roundNumber != null &&
      roundNumber > 0 &&
      recordRoundCount != null &&
      recordRoundCount >= roundNumber) {
    return false
  }

  return state === "RoundInProgress" &&
    playerNamesWithMatchesInProgress.has(standing.player)
}

// --- Data hooks ---

function useStandings(
  eventId: string | null,
  enabled: boolean,
  liveUpdates: boolean,
  manualRefreshRevision: number
) {
  const [standings, setStandings] = useState<StandingEntry[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [lastUpdatedAt, setLastUpdatedAt] = useState<Date | null>(null)
  const { isReady: clientReady } = useClientState()

  // Map for O(1) upsert by player name; flushes to sorted array on each batch
  const mapRef = useRef(new Map<string, StandingEntry>())
  const isDirtyRef = useRef(false)
  const snapshotRequestRef = useRef(0)
  const eventIdRef = useRef(eventId)
  eventIdRef.current = eventId

  // Single stream: initial standings + live deltas
  const flushStandings = useCallback(() => {
    const sorted = Array.from(mapRef.current.values())
      .sort((a, b) => a.rank - b.rank)
    setStandings(sorted)
    setLastUpdatedAt(new Date())
    setLoading(false)
    setError(null)
  }, [])

  useNDJSONStream<StandingEntry>({
    url: getApiUrl(`/api/Events/WatchStandings/${eventId}`),
    enabled: enabled && liveUpdates && clientReady && !!eventId,
    onMessage: (entry) => {
      mapRef.current.set(entry.player, entry)
      isDirtyRef.current = true
    },
    onError: (e) => {
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
        throw new Error(`Failed to refresh standings: ${response.status} ${response.statusText}`)
      }

      const entries = await response.json() as StandingEntry[]
      if (requestId !== snapshotRequestRef.current || requestEventId !== eventIdRef.current) return

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

  // Flush dirty state to React at a capped rate
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

  // Set loading + reset when eventId changes
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


// --- Standings table columns ---

const standingsColumns: ColumnDef<StandingEntry>[] = [
  {
    id: "rank",
    accessorFn: (row) => row.rank,
    header: "#",
    size: 50,
    cell: ({ row }) => (
      <span className="tabular-nums text-muted-foreground">{row.original.rank}</span>
    ),
  },
  {
    id: "player",
    accessorKey: "player",
    header: "Player",
  },
  {
    id: "record",
    accessorKey: "record",
    header: "Record",
    size: 90,
    cell: ({ row }) => (
      <span className="tabular-nums">{row.original.record ?? "—"}</span>
    ),
  },
  {
    id: "points",
    accessorKey: "points",
    header: "Points",
    size: 70,
    cell: ({ row }) => (
      <span className="tabular-nums">{row.original.points ?? 0}</span>
    ),
  },
  {
    id: "omw",
    accessorKey: "opponentMatchWinPercentage",
    header: "OMW%",
    size: 80,
    cell: ({ row }) => (
      <span className="tabular-nums text-muted-foreground">
        {row.original.opponentMatchWinPercentage ?? "—"}
      </span>
    ),
  },
  {
    id: "gw",
    accessorKey: "gameWinPercentage",
    header: "GW%",
    size: 80,
    cell: ({ row }) => (
      <span className="tabular-nums text-muted-foreground">
        {row.original.gameWinPercentage ?? "—"}
      </span>
    ),
  },
  {
    id: "ogw",
    accessorKey: "opponentGameWinPercentage",
    header: "OGW%",
    size: 80,
    cell: ({ row }) => (
      <span className="tabular-nums text-muted-foreground">
        {row.original.opponentGameWinPercentage ?? "—"}
      </span>
    ),
  },
]

// --- Page ---

export default function EventDetails() {
  const { eventId } = useParams<{ eventId: string }>()
  const navigate = useNavigate()
  const { activeGames, upcomingGames, completedGames, loading: eventsLoading } = useEvents()
  const { isReady: clientReady } = useClientState()
  const [liveEvent, setLiveEvent] = useState<ActiveGame | null>(null)
  const [standingsRefreshRevision, setStandingsRefreshRevision] = useState(0)
  const lastPendingStandingsRefreshKeyRef = useRef<string | null>(null)
  const latestRoundNumberRef = useRef<number | undefined>(undefined)

  // Find the event from the shared context
  const event = useMemo(() => {
    return [...activeGames, ...upcomingGames, ...completedGames].find(e => e.id === eventId) ?? null
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

    setLiveEvent((prev) => {
      if (!prev || prev.id !== event.id) {
        const roundNumber = mergeRoundNumber(event.roundNumber, latestRoundNumberRef.current)
        latestRoundNumberRef.current = roundNumber
        return { ...event, roundNumber }
      }

      const state = event.state ?? prev.state
      const roundNumber = mergeRoundNumber(
        event.roundNumber,
        mergeRoundNumber(prev.roundNumber, latestRoundNumberRef.current))
      latestRoundNumberRef.current = roundNumber
      const eventIsOlderRound =
        event.roundNumber != null &&
        prev.roundNumber != null &&
        event.roundNumber < prev.roundNumber
      const currentRoundEndTime =
        roundNumber != null && roundNumber === prev.roundNumber
          ? prev.roundEndTime
          : undefined
      const roundChanged =
        roundNumber != null &&
        prev.roundNumber != null &&
        roundNumber !== prev.roundNumber

      return {
        ...event,
        state,
        roundNumber,
        roundEndTime: getRoundEndTimeForState(
          state,
          currentRoundEndTime,
          eventIsOlderRound ? undefined : event.roundEndTime),
        inPlayoffs: event.inPlayoffs ?? prev.inPlayoffs,
        activePlayerNames: event.activePlayerNames ?? prev.activePlayerNames,
        playerNamesWithMatchesInProgress:
          mergeRoundScopedMatchPlayers(
            eventIsOlderRound ? undefined : event.playerNamesWithMatchesInProgress,
            prev.playerNamesWithMatchesInProgress,
            state,
            roundChanged),
        timeRemaining: hasRoundCountdown(state) ? (event.timeRemaining ?? prev.timeRemaining) : undefined,
      }
    })
  }, [eventRevision])

  useEffect(() => {
    lastPendingStandingsRefreshKeyRef.current = null
    latestRoundNumberRef.current = event?.roundNumber
    setStandingsRefreshRevision(0)
  }, [eventId])

  const displayEvent = liveEvent ?? event
  const phase = displayEvent ? getPhase(displayEvent) : "pre"
  const hasCountdown = hasRoundCountdown(displayEvent?.state)
  const roundCountdown = useCountdown(hasCountdown ? displayEvent?.roundEndTime : undefined)
  const timerText = hasCountdown ? (roundCountdown ?? displayEvent?.timeRemaining) : undefined

  const applyTournamentStateUpdate = useCallback((update: ITournamentStateUpdate) => {
    if (String(update.id) !== eventId) return

    const updateRoundNumber = mergeRoundNumber(
      update.roundNumber,
      latestRoundNumberRef.current)
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
      setStandingsRefreshRevision((revision) => revision + 1)
    }

    setLiveEvent((prev) => {
      if (!prev) return prev

      const nextState = update.state as ActiveGame["state"]
      const roundNumber = mergeRoundNumber(updateRoundNumber, prev.roundNumber)
      const updateIsOlderRound =
        update.roundNumber != null &&
        prev.roundNumber != null &&
        update.roundNumber < prev.roundNumber
      const hasSameRound = hasRoundCountdown(prev.state) && prev.roundNumber === roundNumber
      const roundEndTime = hasRoundCountdown(nextState)
        ? (!updateIsOlderRound && isValidRoundEndTime(update.roundEndTime)
            ? update.roundEndTime
            : (hasSameRound ? prev.roundEndTime : undefined))
        : undefined
      const roundChanged =
        roundNumber != null &&
        prev.roundNumber != null &&
        roundNumber !== prev.roundNumber
      const next: ActiveGame = {
        ...prev,
        state: nextState,
        roundNumber,
        roundEndTime,
        inPlayoffs: update.inPlayoffs,
        activePlayerNames: update.activePlayerNames ?? prev.activePlayerNames,
        playerNamesWithMatchesInProgress:
          mergeRoundScopedMatchPlayers(
            updateIsOlderRound ? undefined : update.playerNamesWithMatchesInProgress,
            prev.playerNamesWithMatchesInProgress,
            nextState,
            roundChanged),
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
  }, [eventId])

  useNDJSONStream<ITournamentStateUpdate | ITournamentStateUpdate[]>({
    url: getApiUrl(`/api/Events/WatchTournamentUpdates/${eventId}`),
    enabled: !!eventId && clientReady && phase === "active",
    onMessage: (message) => {
      const updates = Array.isArray(message) ? message : [message]
      for (const update of updates) applyTournamentStateUpdate(update)
    },
    onError: (e) => {
      console.error("Tournament state update stream error:", e)
    },
    autoReconnect: true,
    reconnectDelay: 2000,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })

  const { standings, loading: standingsLoading, error: standingsError, lastUpdatedAt } = useStandings(
    phase !== "pre" ? eventId ?? null : null,
    phase !== "pre",
    phase === "active",
    standingsRefreshRevision
  )

  const playerNamesWithMatchesInProgress = useMemo(
    () => new Set(displayEvent?.playerNamesWithMatchesInProgress ?? []),
    [displayEvent?.playerNamesWithMatchesInProgress]
  )

  const getStandingRowClassName = useCallback((standing: StandingEntry) => {
    return hasCurrentRoundMatchInProgress(
      standing,
      displayEvent?.state,
      displayEvent?.roundNumber,
      playerNamesWithMatchesInProgress
    )
      ? "bg-yellow-500/15 hover:bg-yellow-500/20"
      : ""
  }, [playerNamesWithMatchesInProgress, displayEvent?.state, displayEvent?.roundNumber])

  if (eventsLoading) {
    return (
      <div className="container mx-auto py-4 px-4 space-y-6">
        <Button variant="ghost" onClick={() => navigate('/events')} className="-ml-4">
          <ArrowLeft className="mr-2 h-4 w-4" />
          Events
        </Button>
        <Skeleton className="h-10 w-2/3" />
        <div className="grid gap-4 md:grid-cols-2">
          <Skeleton className="h-20" />
          <Skeleton className="h-20" />
        </div>
        <Skeleton className="h-[300px]" />
      </div>
    )
  }

  if (!displayEvent) {
    return (
      <div className="container mx-auto py-4 px-4 space-y-4">
        <Button variant="ghost" onClick={() => navigate('/events')} className="-ml-4">
          <ArrowLeft className="mr-2 h-4 w-4" />
          Events
        </Button>
        <div className="text-muted-foreground text-sm">
          Event not found. It may have ended or not yet loaded.
        </div>
      </div>
    )
  }

  const formatSchedule = () => {
    const start = displayEvent._rawStartTime
    const end = displayEvent._rawEndTime
    if (!start) return "—"
    const startDate = new Date(start)
    const date = startDate.toLocaleDateString(undefined, {
      weekday: "short", month: "short", day: "numeric",
    })
    const startTime = startDate.toLocaleTimeString([], { hour: "numeric", minute: "2-digit" })
    const endTime = end ? new Date(end).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" }) : "?"
    return `${date}, ${startTime} – ${endTime}`
  }

  const formatLastStandingsUpdate = () => {
    if (!lastUpdatedAt) return "never"
    return lastUpdatedAt.toLocaleTimeString([], {
      hour: "numeric",
      minute: "2-digit",
      second: "2-digit",
    })
  }

  return (
    <div className="container mx-auto py-4 px-4 space-y-5">
      {/* Header */}
      <div className="flex items-start gap-4">
        <Button variant="ghost" size="icon" onClick={() => navigate('/events')} className="shrink-0">
          <ArrowLeft className="h-5 w-5" />
        </Button>
        <div className="flex-1 min-w-0">
          <h1 className="text-2xl font-bold tracking-tight flex items-center gap-3">
            <span className="truncate">{displayEvent.name}</span>
            <Badge variant={getStatusVariant(displayEvent)} className="shrink-0 rounded-md">
              {getStatusLabel(displayEvent)}
            </Badge>
          </h1>
          <div className="flex items-center gap-4 mt-1 text-muted-foreground text-sm">
            <span className="flex items-center gap-1.5">
              <span className={cn("w-2 h-2 rounded-full shrink-0", getFormatDot(displayEvent.format))} />
              {displayEvent.format}
            </span>
            <span className="flex items-center gap-1.5">
              <Clock className="h-3.5 w-3.5" />
              {formatSchedule()}
            </span>
            <span className="flex items-center gap-1.5">
              <Users className="h-3.5 w-3.5" />
              {displayEvent.totalPlayers ?? 0} / {displayEvent.minimumPlayers ?? 0} players
            </span>
            {displayEvent.totalRounds != null && (
              <span className="flex items-center gap-1.5">
                <Swords className="h-3.5 w-3.5" />
                {displayEvent.totalRounds} rounds
              </span>
            )}
          </div>
        </div>
        {hasCountdown && timerText && (
          <div className="ml-auto shrink-0 rounded-lg border border-sidebar-border/60 bg-muted/30 px-3 py-2 text-right">
            <div className="text-[11px] uppercase tracking-wide text-muted-foreground">
              Round timer
            </div>
            <div className="mt-0.5 flex items-center justify-end gap-1.5 text-sm font-semibold tabular-nums">
              <Clock className="h-3.5 w-3.5 text-muted-foreground" />
              {timerText} left
            </div>
          </div>
        )}
      </div>

      {/* Standings / Players section */}
      <div>
        <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
          <h2 className="text-lg font-semibold tracking-tight flex items-center gap-2">
            <Trophy className="h-4 w-4" />
            {phase === "pre" ? "Players" : "Standings"}
            {standings.length > 0 && (
              <span className="text-sm font-normal text-muted-foreground">
                ({standings.length})
              </span>
            )}
          </h2>
          {phase !== "pre" && (
            <div className="rounded-md border border-sidebar-border/60 bg-muted/30 px-2.5 py-1 text-xs text-muted-foreground">
              Last standings update: {formatLastStandingsUpdate()}
            </div>
          )}
        </div>

        {phase === "pre" ? (
          <div className="rounded-lg border border-sidebar-border/60 p-6 text-center text-sm text-muted-foreground">
            <Users className="h-8 w-8 mx-auto mb-2 opacity-40" />
            <p>
              {displayEvent.totalPlayers ?? 0} of {displayEvent.minimumPlayers ?? 0} players registered.
            </p>
            <p className="mt-1">
              Standings will be available once the tournament begins.
            </p>
          </div>
        ) : standingsLoading ? (
          <div className="space-y-2">
            <Skeleton className="h-10 w-full" />
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
          </div>
        ) : standingsError ? (
          <div className="bg-destructive/15 text-destructive px-4 py-3 rounded-md text-sm font-medium">
            Error loading standings: {standingsError}
          </div>
        ) : standings.length === 0 ? (
          <div className="rounded-lg border border-sidebar-border/60 p-6 text-center text-sm text-muted-foreground">
            <Trophy className="h-8 w-8 mx-auto mb-2 opacity-40" />
            <p>No standings available yet.</p>
          </div>
        ) : (
          <DataTable
            columns={standingsColumns}
            data={standings}
            pageSize={16}
            autoResetPageIndex={false}
            className="[&_td]:py-1.5 [&_th]:py-1.5"
            wrapperClassName="overflow-visible"
            getRowClassName={getStandingRowClassName}
          />
        )}
      </div>
    </div>
  )
}
