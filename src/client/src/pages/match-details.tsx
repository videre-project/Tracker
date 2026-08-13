/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import { createPortal } from "react-dom"
import {
  MatchDetailsLayout,
  type MatchDetailsData,
  type MatchDetailsGame,
  type ReplayData,
  GAME_LOG_TYPE_ORDER as TYPE_ORDER,
} from "@videreproject/ui"
import { ArrowLeft } from "lucide-react"
import { useNavigate, useParams } from "react-router-dom"

import { useCardArtContext } from "@/hooks/use-card-art"
import { Button } from "@videreproject/ui"
import { Skeleton } from "@videreproject/ui"
import { useClientState } from "@/hooks/use-client-state"
import { useDeckDetail, type DeckDetail } from "@/hooks/use-decks"
import { useMatchDetails, updateOpponentArchetype } from "@/hooks/use-match-details"
import { useNDJSONStream } from "@/hooks/use-ndjson-stream"
import type {
  GameDetailsDTO,
  GameLogDTO,
  MatchDetailsDTO,
  SideboardChangeDTO,
} from "@/types/api"
import { getApiUrl } from "@/utils/api-config"
import {
  getCatalogIdByCardId,
  getOpeningHandCards,
} from "@/utils/opening-hand"

// -- Match DTO -> shared layout helpers --

type SideboardingCard = {
  key: string
  name: string
  quantity: number
  catalogId?: number | null
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

function getDeckPreviewCards(detail?: DeckDetail | null) {
  if (!detail) return []

  return [...detail.mainboard]
    .sort((a, b) => b.quantity - a.quantity)
    .slice(0, 5)
    .map(card => ({
      catalogId: card.catalogId,
      name: card.name,
      quantity: card.quantity,
    }))
}

function getSideboardingDiff(changes?: SideboardChangeDTO[] | null) {
  const added = new Map<number, SideboardingCard>()
  const removed = new Map<number, SideboardingCard>()

  for (const change of changes ?? []) {
    const quantity = change.quantity ?? 0
    const catalogId = change.catalogId ?? 0
    if (quantity === 0 || catalogId <= 0) continue

    const cards = quantity > 0 ? added : removed
    const direction = quantity > 0 ? "in" : "out"
    const amount = Math.abs(quantity)
    const existing = cards.get(catalogId)
    if (existing) {
      existing.quantity += amount
      continue
    }

    cards.set(catalogId, {
      key: `${direction}:${catalogId}`,
      name: change.name?.trim() || `Card ID #${catalogId}`,
      quantity: amount,
      catalogId,
    })
  }

  const byName = (a: SideboardingCard, b: SideboardingCard) =>
    a.name.localeCompare(b.name) || (a.catalogId ?? 0) - (b.catalogId ?? 0)

  return {
    in: Array.from(added.values()).sort(byName),
    out: Array.from(removed.values()).sort(byName),
  }
}

function toSharedMatchDetails({
  match,
  deckDetail,
  selectedGameKey,
  selectedGameReplay,
  deckBackgroundArtUrl,
}: {
  match: MatchDetailsDTO
  deckDetail?: DeckDetail | null
  selectedGameKey: number | null
  selectedGameReplay?: ReplayData | null
  deckBackgroundArtUrl?: string | null
}): MatchDetailsData {
  const catalogIdByCardId = getCatalogIdByCardId(selectedGameReplay)

  return {
    eventName: match.eventName || "Match",
    result: match.result,
    isActive: match.isActive,
    record: match.record || "",
    format: match.format || "-",
    opponentName: match.opponentName,
    opponentDeckName: match.opponentDeckName,
    opponentDeckArchetype: match.opponentDeckArchetype,
    opponentDeckColors: match.opponentDeckColors,
    date: formatMatchDate(match.startTime),
    duration: match.duration || "-",
    deckName: match.deckName,
    deckArchetype: match.deckArchetype,
    deckColors: match.deckColors,
    deckBackgroundArtUrl,
    deckPreviewCards: getDeckPreviewCards(deckDetail),
    games: (match.games ?? []).map((game, index) => {
      const gameKey = getGameKey(game)
      const sideboarding = getSideboardingDiff(game.sideboardChanges)
      const openingHand = gameKey === selectedGameKey
        ? getOpeningHandCards(game.logs ?? [], catalogIdByCardId)
        : []

      return {
        id: String(gameKey),
        gameNumber: game.gameNumber ?? index + 1,
        result: game.result || "Unknown",
        playDraw: game.playDraw || "-",
        duration: game.duration || "-",
        openingHand: openingHand.map(card => ({
          catalogId: card.catalogId ?? 0,
          name: card.name,
          bottomed: card.bottomed,
        })),
        sideboarding: {
          in: sideboarding.in.map(card => ({
            catalogId: card.catalogId ?? 0,
            name: card.name,
            quantity: card.quantity,
          })),
          out: sideboarding.out.map(card => ({
            catalogId: card.catalogId ?? 0,
            name: card.name,
            quantity: card.quantity,
          })),
        },
      }
    }),
  }
}

// -- Page --

export default function MatchDetails() {
  const { matchId } = useParams<{ matchId: string }>()
  const navigate = useNavigate()
  const parsedMatchId = matchId ? parseInt(matchId, 10) : null
  const { data: fetchedData, loading, error } = useMatchDetails(parsedMatchId)
  const { isReady: clientReady } = useClientState()
  const { getArtUrl, prefetchCards, isReady: cardArtReady } = useCardArtContext()

  const [data, setData] = useState<MatchDetailsDTO | null>(null)
  const [liveLogCount, setLiveLogCount] = useState(0)
  const [selectedGameKey, setSelectedGameKey] = useState<number | null>(null)
  const [selectedGameReplay, setSelectedGameReplay] = useState<ReplayData | null>(null)
  const [archetypePending, setArchetypePending] = useState(false)
  const [archetypeError, setArchetypeError] = useState<string | null>(null)

  useEffect(() => {
    if (!fetchedData) return
    setData(fetchedData)
    setLiveLogCount(0)
  }, [fetchedData])

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

  useEffect(() => {
    if (!clientReady || parsedMatchId == null || selectedGameKey == null) {
      setSelectedGameReplay(null)
      return
    }

    const controller = new AbortController()
    fetch(getApiUrl(`/api/games/game/${selectedGameKey}/replay`), { signal: controller.signal })
      .then(response => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return response.json() as Promise<ReplayData>
      })
      .then(setSelectedGameReplay)
      .catch(requestError => {
        if (requestError instanceof Error && requestError.name === "AbortError") return
        setSelectedGameReplay(null)
      })

    return () => controller.abort()
  }, [clientReady, parsedMatchId, selectedGameKey])

  const [refetchTrigger, setRefetchTrigger] = useState(0)
  const unknownGameIdRef = useRef(new Set<number>())

  useEffect(() => {
    if (refetchTrigger === 0 || !parsedMatchId || !clientReady) return
    fetch(getApiUrl(`/api/games/match/${parsedMatchId}`))
      .then(response => response.json())
      .then(json => {
        setData(json)
        unknownGameIdRef.current.clear()
      })
      .catch(console.error)
  }, [refetchTrigger, parsedMatchId, clientReady])

  const onSSEMessage = useCallback((dto: GameLogDTO) => {
    setData(previous => {
      if (!previous?.games) return previous
      const gameIndex = previous.games.findIndex(game => game.id === (dto.gameId ?? 0))
      if (gameIndex === -1) {
        if (dto.gameId && !unknownGameIdRef.current.has(dto.gameId)) {
          unknownGameIdRef.current.add(dto.gameId)
          setTimeout(() => setRefetchTrigger(value => value + 1), 0)
        }
        return previous
      }

      return {
        ...previous,
        games: previous.games.map((game, index) => {
          if (index !== gameIndex) return game
          const logs = [...(game.logs ?? []), dto]
          logs.sort((a, b) => {
            const nonceA = a.nonce ?? 0
            const nonceB = b.nonce ?? 0
            if (nonceA !== 0 && nonceB !== 0 && nonceA === nonceB) {
              const typeA = TYPE_ORDER[a.gameLogType] ?? 6
              const typeB = TYPE_ORDER[b.gameLogType] ?? 6
              if (typeA !== typeB) return typeA - typeB
            }
            const timestampA = a.timestamp ? new Date(a.timestamp).getTime() : 0
            const timestampB = b.timestamp ? new Date(b.timestamp).getTime() : 0
            return timestampA - timestampB
          })
          return { ...game, logs }
        }),
      }
    })
    setLiveLogCount(count => count + 1)
  }, [])

  useNDJSONStream<GameLogDTO>({
    url: parsedMatchId ? getApiUrl(`/api/games/match/${parsedMatchId}/watch`) : "",
    onMessage: onSSEMessage,
    enabled: clientReady && parsedMatchId != null && data != null,
    autoReconnect: true,
    reconnectDelay: 2000,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })

  const { detail: deckDetail } = useDeckDetail(data?.deckRevisionId?.toString() ?? null)
  const previewCards = useMemo(() => getDeckPreviewCards(deckDetail), [deckDetail])
  const backgroundCard = previewCards[Math.floor(previewCards.length / 2)]
  const deckBackgroundArtUrl = backgroundCard ? getArtUrl(backgroundCard.name) : null

  useEffect(() => {
    if (!cardArtReady || !backgroundCard?.name || deckBackgroundArtUrl) return
    void prefetchCards([backgroundCard.name])
  }, [backgroundCard?.name, cardArtReady, deckBackgroundArtUrl, prefetchCards])

  const sharedMatch = useMemo(() => data ? toSharedMatchDetails({
    match: data,
    deckDetail,
    selectedGameKey,
    selectedGameReplay,
    deckBackgroundArtUrl,
  }) : null, [data, deckBackgroundArtUrl, deckDetail, selectedGameKey, selectedGameReplay])

  if (error) {
    return (
      <div className="mx-auto w-full max-w-7xl space-y-4 px-4 pb-4 pt-1">
        <Button variant="ghost" onClick={() => navigate("/history")} className="-ml-3 h-8">
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back
        </Button>
        <div className="rounded-md bg-destructive/15 px-4 py-3 text-sm font-medium text-destructive">
          Error loading match details: {error.message || String(error)}
        </div>
      </div>
    )
  }

  if (loading || !data || !sharedMatch) {
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

  const latestGameKey = getLatestGameKey(data.games ?? [])
  const headerEndHost = typeof document === "undefined" ? null : document.getElementById("page-header-end")

  const navigateToGameLog = (game: MatchDetailsGame) => {
    if (parsedMatchId == null) return
    navigate(`/history/${parsedMatchId}/watch?gameId=${game.id}`)
  }

  const navigateToReplay = (game: MatchDetailsGame) => {
    if (parsedMatchId == null) return
    navigate(`/history/${parsedMatchId}/game/${game.id}/replay`, {
      state: { eventId: data.eventId ?? null, gameNumber: game.gameNumber },
    })
  }

  return (
    <>
      {headerEndHost && data.eventId != null ? createPortal(
        <div className="inline-flex items-center gap-1.5 text-xs text-muted-foreground">
          <span>Event ID</span>
          <span className="font-semibold text-foreground">{data.eventId}</span>
        </div>,
        headerEndHost,
      ) : null}
      <MatchDetailsLayout
        className="h-[calc(100vh-2.5rem)]"
        match={sharedMatch}
        initialGameId={selectedGameKey == null ? undefined : String(selectedGameKey)}
        liveUpdateCount={liveLogCount}
        onBack={() => navigate("/history")}
        onWatchLive={() => {
          if (parsedMatchId == null || latestGameKey == null) return
          navigate(`/history/${parsedMatchId}/game/${latestGameKey}/replay`, {
            state: { eventId: data.eventId ?? null },
          })
        }}
        onOpenDeck={data.deckRevisionId ? () => navigate(`/decks/${data.deckRevisionId}`, {
          state: {
            deckName: data.deckName ?? undefined,
            deckFormat: data.format ?? undefined,
            deckColors: data.deckColors ?? undefined,
            deckArchetype: data.deckArchetype ?? undefined,
            deckTimestamp: deckDetail?.timestamp ?? data.startTime ?? undefined,
            deckMainCount: deckDetail?.mainboard.reduce((total, card) => total + card.quantity, 0),
            deckSideCount: deckDetail?.sideboard.reduce((total, card) => total + card.quantity, 0),
          },
        }) : undefined}
        onGameChange={game => setSelectedGameKey(Number(game.id))}
        onGameLog={navigateToGameLog}
        onReplay={navigateToReplay}
        opponentArchetypePending={archetypePending}
        opponentArchetypeError={archetypeError}
        onOpponentArchetypeChange={parsedMatchId == null ? undefined : async archetype => {
          setArchetypePending(true)
          setArchetypeError(null)
          try {
            const updated = await updateOpponentArchetype(parsedMatchId, archetype)
            setData(previous => previous ? {
              ...previous,
              opponentDeckArchetype: updated.opponentDeckArchetype,
              opponentDeckColors: updated.opponentDeckColors,
            } : previous)
          } catch (requestError) {
            setArchetypeError(requestError instanceof Error ? requestError.message : String(requestError))
            throw requestError
          } finally {
            setArchetypePending(false)
          }
        }}
      />
    </>
  )
}
