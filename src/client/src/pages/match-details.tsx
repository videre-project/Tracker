import React, { useState, useEffect, useCallback, useRef } from "react"
import { createPortal } from "react-dom"
import { useParams, useNavigate } from "react-router-dom"
import { getApiUrl } from "@/utils/api-config"
import { useClientState } from "@/hooks/use-client-state"
import { useMatchDetails } from "@/hooks/use-match-details"
import { useNDJSONStream } from "@/hooks/use-ndjson-stream"
import { Button } from "@/components/ui/button"
import { ArrowLeft, Clock, Calendar, Radio, Play, Layers3, FileText, UserRound } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Skeleton } from "@/components/ui/skeleton"
import type { GameDetailsDTO, GameLogDTO, MatchDetailsDTO } from "@/types/api"
import { TYPE_ORDER } from "@/utils/game-log-rendering"
import { cn } from "@/lib/utils"
import { useDeckDetail } from "@/hooks/use-decks"
import type { ReplayData } from "@/types/replay-types"
import { GameReviewPanel } from "@/components/match/game-review-panel"
import {
  DeckManaSymbols,
  MatchDeckCard,
  getDeckPreviewCards,
} from "@/components/match/match-deck-card"
import {
  getCatalogIdByCardId,
  getOpeningHandCards,
  getSideboardingDiff,
} from "@/components/match/match-review-model"

function formatMatchDate(dateString?: string | null) {
  if (!dateString) return "-"

  const date = new Date(dateString)
  if (Number.isNaN(date.getTime())) return "-"

  return date.toLocaleString(undefined, {
    month: "short",
    day: "numeric",
    year: "numeric",
    hour: "numeric",
    minute: "2-digit",
  })
}

function getResultBadgeVariant(result?: string | null) {
  if (result === "Win") return "default"
  if (result === "Loss") return "destructive"
  return "secondary"
}

function getInProgressBadgeClass(isInProgress: boolean) {
  return isInProgress
    ? "border-yellow-500/30 bg-yellow-500/15 text-yellow-700 dark:text-yellow-400"
    : ""
}

function getGameKey(game: GameDetailsDTO) {
  return game.id ?? game.gameNumber ?? 0
}

function getLatestGameKey(games: GameDetailsDTO[]) {
  if (games.length === 0) return null

  const latest = [...games].sort((a, b) => {
    const aOrder = a.gameNumber ?? a.id ?? 0
    const bOrder = b.gameNumber ?? b.id ?? 0
    return bOrder - aOrder
  })[0]

  return latest.id ?? latest.gameNumber ?? null
}

function MatchHeaderMeta({
  label,
  value,
  icon: Icon,
  className,
}: {
  label: string
  value: React.ReactNode
  icon: React.ComponentType<{ className?: string }>
  className?: string
}) {
  return (
    <span className={cn("inline-flex h-6 min-w-0 items-center gap-1.5 leading-none", className)}>
      <Icon className="h-3.5 w-3.5 shrink-0 translate-y-px text-muted-foreground" />
      <span className="shrink-0 text-xs font-medium text-muted-foreground">{label}</span>
      <span className="min-w-0 truncate text-xs font-semibold text-foreground">{value}</span>
    </span>
  )
}

function OpponentMetaValue({
  opponentName,
  opponentDeckName,
  opponentDeckArchetype,
  opponentDeckColors,
}: {
  opponentName?: string | null
  opponentDeckName?: string | null
  opponentDeckArchetype?: string | null
  opponentDeckColors?: string[] | null
}) {
  const name = opponentName?.trim()
  const deckLabel = opponentDeckArchetype?.trim() || opponentDeckName?.trim() || "Deck unknown"

  return (
    <span className="inline-flex min-w-0 items-center gap-1.5">
      <span className="truncate">{name ? `vs ${name}` : "Opponent unknown"}</span>
      <span className="text-muted-foreground/60">-</span>
      <span className="truncate text-muted-foreground">{deckLabel}</span>
      <DeckManaSymbols colors={opponentDeckColors} />
    </span>
  )
}

export default function MatchDetails() {
  const { matchId } = useParams<{ matchId: string }>()
  const navigate = useNavigate()
  const parsedMatchId = matchId ? parseInt(matchId, 10) : null

  const { data: fetchedData, loading, error } = useMatchDetails(parsedMatchId)
  const { isReady: clientReady } = useClientState()

  // Local mutable copy of match data — receives both initial fetch and live SSE updates
  const [data, setData] = useState<MatchDetailsDTO | null>(null)
  const [liveLogCount, setLiveLogCount] = useState(0)
  const [selectedGameKey, setSelectedGameKey] = useState<number | null>(null)
  const [selectedGameReplay, setSelectedGameReplay] = useState<ReplayData | null>(null)

  useEffect(() => {
    if (fetchedData) {
      setData(fetchedData)
      setLiveLogCount(0)
    }
  }, [fetchedData])

  useEffect(() => {
    if (!clientReady || parsedMatchId == null || selectedGameKey == null) {
      setSelectedGameReplay(null)
      return
    }

    const controller = new AbortController()
    fetch(getApiUrl(`/api/games/game/${selectedGameKey}/replay`), {
      signal: controller.signal,
    })
      .then(response => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return response.json() as Promise<ReplayData>
      })
      .then(json => {
        setSelectedGameReplay(json)
      })
      .catch(error => {
        if (error instanceof Error && error.name === "AbortError") return
        setSelectedGameReplay(null)
      })

    return () => controller.abort()
  }, [clientReady, parsedMatchId, selectedGameKey])

  useEffect(() => {
    const games = data?.games ?? []
    if (games.length === 0) {
      setSelectedGameKey(current => current === null ? current : null)
      return
    }

    const gameKeys = new Set(games.map(getGameKey))
    if (selectedGameKey === null || !gameKeys.has(selectedGameKey)) {
      setSelectedGameKey(getGameKey(games[0]))
    }
  }, [data?.games, selectedGameKey])

  // Refetch trigger for when SSE delivers a log for an unknown gameId
  const [refetchTrigger, setRefetchTrigger] = useState(0)
  const unknownGameIdRef = useRef(new Set<number>())

  useEffect(() => {
    if (refetchTrigger === 0 || !parsedMatchId || !clientReady) return
    fetch(getApiUrl(`/api/games/match/${parsedMatchId}`))
      .then(r => r.json())
      .then(json => { setData(json); unknownGameIdRef.current.clear() })
      .catch(console.error)
  }, [refetchTrigger, parsedMatchId, clientReady])

  // SSE message handler — appends new logs to the correct game
  const onSSEMessage = useCallback((dto: GameLogDTO) => {
    setData(prev => {
      if (!prev?.games) return prev
      const idx = prev.games.findIndex(g => g.id === (dto.gameId ?? 0))
      if (idx === -1) {
        // Unknown game — trigger a full refetch
        if (dto.gameId && !unknownGameIdRef.current.has(dto.gameId)) {
          unknownGameIdRef.current.add(dto.gameId)
          setTimeout(() => setRefetchTrigger(t => t + 1), 0)
        }
        return prev
      }
      return {
        ...prev,
        games: prev.games.map((g, i) => {
          if (i !== idx) return g
          const logs = [...(g.logs ?? []), dto]
          logs.sort((a, b) => {
            const na = a.nonce ?? 0, nb = b.nonce ?? 0
            if (na !== 0 && nb !== 0 && na === nb) {
              const ta = TYPE_ORDER[a.gameLogType] ?? 6
              const tb = TYPE_ORDER[b.gameLogType] ?? 6
              if (ta !== tb) return ta - tb
            }
            return new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime()
          })
          return { ...g, logs }
        }),
      }
    })
    setLiveLogCount(c => c + 1)
  }, [])

  // Connect to SSE stream (only after initial fetch completes)
  const streamUrl = parsedMatchId
    ? getApiUrl(`/api/games/match/${parsedMatchId}/watch`)
    : ""

  useNDJSONStream<GameLogDTO>({
    url: streamUrl,
    onMessage: onSSEMessage,
    enabled: clientReady && parsedMatchId != null && data != null,
    autoReconnect: true,
    reconnectDelay: 2000,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })

  const { detail: deckDetail, loading: deckDetailLoading } = useDeckDetail(data?.deckHash ?? null)

  if (error) {
    return (
      <div className="mx-auto w-full max-w-7xl space-y-4 px-4 pb-4 pt-1">
        <Button variant="ghost" onClick={() => navigate('/history')} className="-ml-3 h-8">
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back
        </Button>
        <div className="rounded-md bg-destructive/15 px-4 py-3 text-sm font-medium text-destructive">
          Error loading match details: {error.message || String(error)}
        </div>
      </div>
    )
  }

  if (loading || !data) {
    return (
      <div className="mx-auto w-full max-w-7xl space-y-4 px-4 pb-4 pt-1">
        <div className="flex items-start justify-between gap-3">
          <div className="flex min-w-0 items-start gap-3">
            <Skeleton className="h-8 w-8 shrink-0" />
            <div className="min-w-0 space-y-2">
              <Skeleton className="h-6 w-80 max-w-full" />
              <Skeleton className="h-4 w-[32rem] max-w-full" />
            </div>
          </div>
          <Skeleton className="h-8 w-24 shrink-0" />
        </div>
        <div className="flex flex-wrap items-center gap-x-5 gap-y-2 rounded-md border border-sidebar-border/60 bg-card px-3 py-2">
          {Array.from({ length: 6 }).map((_, index) => (
            <div key={index} className="flex items-center gap-1.5">
              <Skeleton className="h-4 w-4" />
              <Skeleton className="h-3 w-12" />
              <Skeleton className="h-4 w-20" />
            </div>
          ))}
        </div>
        <div className="grid gap-3 lg:grid-cols-[18rem_minmax(0,1fr)]">
          <div className="space-y-2">
            <Skeleton className="h-5 w-24" />
            <Skeleton className="h-20 w-full" />
            <Skeleton className="h-20 w-full" />
            <Skeleton className="h-20 w-full" />
          </div>
          <Skeleton className="min-h-[360px] w-full" />
        </div>
      </div>
    )
  }

  const games = data.games ?? []
  const isInProgress = data.isActive || data.result === "In Progress"
  const resultVariant = isInProgress ? "secondary" : getResultBadgeVariant(data.result)
  const resultLabel = isInProgress ? "In Progress" : data.record || data.result || "Match"
  const selectedGame = games.find(game => getGameKey(game) === selectedGameKey) ?? games[0]
  const selectedGameId = selectedGame ? getGameKey(selectedGame) : null
  const latestGameId = getLatestGameKey(games)
  const deckPreviewCards = getDeckPreviewCards(deckDetail)
  const catalogIdByCardId = getCatalogIdByCardId(selectedGameReplay)
  const selectedOpeningHandCards = getOpeningHandCards(selectedGame?.logs ?? [], catalogIdByCardId)
  const selectedSideboardingDiff = getSideboardingDiff(selectedGame?.sideboardChanges)
  const headerEndHost = typeof document === "undefined"
    ? null
    : document.getElementById("page-header-end")
  const openDeckEditor = () => {
    if (!data.deckHash) return

    navigate(`/decks/${encodeURIComponent(data.deckHash)}`, {
      state: {
        deckName: data.deckName ?? undefined,
        deckFormat: data.format ?? undefined,
        deckColors: data.deckColors ?? undefined,
      },
    })
  }

  return (
    <div className="mx-auto flex h-[calc(100vh-2.5rem)] min-h-0 w-full max-w-7xl flex-col gap-4 overflow-hidden px-4 pb-4 pt-1">
      {headerEndHost && data.eventId != null ? createPortal(
        <div className="inline-flex items-center gap-1.5 text-xs text-muted-foreground">
          <span>Event ID</span>
          <span className="font-semibold text-foreground">{data.eventId}</span>
        </div>,
        headerEndHost
      ) : null}
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex min-w-0 items-start gap-3">
          <Button
            variant="ghost"
            size="icon"
            onClick={() => navigate('/history')}
            className="mt-0.5 h-8 w-8 shrink-0"
            aria-label="Back"
          >
            <ArrowLeft className="h-5 w-5" />
          </Button>
          <div className="min-w-0 pt-0.5">
            <div className="flex min-w-0 flex-wrap items-center gap-2">
              <h1 className="min-w-0 truncate text-xl font-semibold leading-7 tracking-tight">
                {data.eventName || "Match"}
              </h1>
              <Badge
                variant={resultVariant}
                className={cn("rounded-md capitalize", getInProgressBadgeClass(Boolean(isInProgress)))}
              >
                {resultLabel}
              </Badge>
            </div>
          </div>
        </div>
        <div className="flex items-center gap-2">
          {liveLogCount > 0 && (
            <Badge variant="success" className="rounded-md text-xs">
              +{liveLogCount} live
            </Badge>
          )}
          <Button
            variant="outline"
            size="sm"
            className="h-8 shrink-0 gap-1.5 border-sidebar-border/60"
            disabled={parsedMatchId == null || latestGameId == null}
            onClick={() => {
              if (parsedMatchId == null || latestGameId == null) return
              navigate(`/history/${parsedMatchId}/game/${latestGameId}/replay`, {
                state: { eventId: data.eventId ?? null },
              })
            }}
          >
            <Radio className="h-3.5 w-3.5 translate-y-px" />
            Watch Live
          </Button>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-x-5 gap-y-1.5 rounded-md border border-sidebar-border/60 bg-card px-3 py-2">
        <MatchHeaderMeta label="Format" value={data.format || "-"} icon={Layers3} />
        <MatchHeaderMeta
          label="Opponent"
          value={(
            <OpponentMetaValue
              opponentName={data.opponentName}
              opponentDeckName={data.opponentDeckName}
              opponentDeckArchetype={data.opponentDeckArchetype}
              opponentDeckColors={data.opponentDeckColors}
            />
          )}
          icon={UserRound}
          className="max-w-full sm:max-w-[28rem]"
        />
        <MatchHeaderMeta label="Date" value={formatMatchDate(data.startTime)} icon={Calendar} />
        <MatchHeaderMeta label="Duration" value={data.duration || "-"} icon={Clock} />
      </div>

      <section className="grid min-h-0 flex-1 gap-3 lg:grid-cols-[18rem_minmax(0,1fr)]">
        <div className="flex min-h-0 flex-col gap-3 overflow-visible">
          <div className="min-h-0 flex-1 space-y-2 overflow-y-auto overflow-x-hidden">
            <h2 className="text-sm font-medium text-foreground">Games ({games.length})</h2>
            {games.map((game) => {
              const gameId = game.id ?? game.gameNumber ?? 0
              const gameResultVariant = getResultBadgeVariant(game.result)
              const selected = selectedGameId === gameId
              return (
                <button
                  key={gameId}
                  type="button"
                  onClick={() => setSelectedGameKey(gameId)}
                  className={cn(
                    "block w-full rounded-md border border-sidebar-border/60 bg-card px-3 py-2.5 text-left transition-colors hover:bg-muted/35",
                    selected && "border-sidebar-accent/70 bg-muted/45"
                  )}
                >
                  <div className="flex items-start gap-3">
                    <div className={cn(
                      "flex h-8 w-8 shrink-0 items-center justify-center rounded-md bg-muted/40 text-sm font-semibold text-foreground",
                      selected && "bg-sidebar-accent/40"
                    )}>
                      {game.gameNumber ?? "-"}
                    </div>
                    <div className="min-w-0 flex-1">
                      <div className="flex min-w-0 flex-wrap items-center gap-2">
                        <span className="font-medium">Game {game.gameNumber ?? "-"}</span>
                        <Badge variant={gameResultVariant} className="rounded-md capitalize">
                          {game.result || "Unknown"}
                        </Badge>
                      </div>
                      <div className="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground">
                        <span>On the {game.playDraw || "-"}</span>
                        <span>{game.duration || "-"}</span>
                      </div>
                    </div>
                  </div>
                </button>
              )
            })}
          </div>
          <MatchDeckCard
            deckName={data.deckName}
            deckHash={data.deckHash}
            deckArchetype={data.deckArchetype}
            deckColors={data.deckColors}
            previewCards={deckPreviewCards}
            loading={deckDetailLoading}
            onOpen={openDeckEditor}
          />
        </div>

        <div className="flex min-h-0 min-w-0 flex-col overflow-hidden rounded-md border border-sidebar-border/60 bg-card">
          {selectedGame ? (
            <>
              <div className="min-h-0 flex-1 overflow-hidden">
                <GameReviewPanel
                  openingHandCards={selectedOpeningHandCards}
                  sideboardingDiff={selectedSideboardingDiff}
                  endContent={selectedGameId !== null ? (
                    <>
                      <Button
                        variant="outline"
                        size="sm"
                        className="h-7 shrink-0 gap-1.5 border-sidebar-border/60 px-2.5 text-xs"
                        onClick={() => navigate(`/history/${parsedMatchId}/watch?gameId=${selectedGameId}`)}
                      >
                        <FileText className="h-3.5 w-3.5" />
                        Game Log
                      </Button>
                      <Button
                        variant="outline"
                        size="sm"
                        className="h-7 shrink-0 gap-1.5 border-sidebar-border/60 px-2.5 text-xs"
                        onClick={() => navigate(`/history/${parsedMatchId}/game/${selectedGameId}/replay`, {
                          state: {
                            eventId: data.eventId ?? null,
                            gameNumber: selectedGame?.gameNumber ?? null,
                          },
                        })}
                      >
                        <Play className="h-3.5 w-3.5 translate-y-px" />
                        Replay
                      </Button>
                    </>
                  ) : null}
                />
              </div>
            </>
          ) : (
            <div className="flex min-h-[360px] items-center justify-center px-4 py-8 text-sm text-muted-foreground">
              No games recorded for this match.
            </div>
          )}
        </div>
      </section>
    </div>
  )
}
