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
} from "@videreproject/ui"
import { ArrowLeft } from "lucide-react"
import { useLocation, useNavigate, useParams, useSearchParams } from "react-router-dom"

import { useCardArtContext } from "@/hooks/use-card-art"
import { Button } from "@videreproject/ui"
import { Skeleton } from "@videreproject/ui"
import { useClientState } from "@/hooks/use-client-state"
import { useDeckDetail, type DeckDetail } from "@/hooks/use-decks"
import { useMatchDetails, loadOpponentArchetypeContext, updateOpponentArchetypes } from "@/hooks/use-match-details"
import { OpponentClassificationNotice } from "@/components/opponent-classification-notice"
import type {
  GameDetailsDTO,
  MatchDetailsDTO,
  OpponentArchetypeUpdate,
} from "@/types/api"
import { getApiUrl } from "@/utils/api-config"
import { readReplayData } from "@/utils/replay-data"
import { getSideboardingDiff } from "@/utils/sideboarding"
import {
  getCatalogIdByCardId,
  getOpeningHandCards,
} from "@/utils/opening-hand"

// -- Match DTO -> shared layout helpers --

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
  const catalogIdByCardId = getCatalogIdByCardId(
    selectedGameReplay?.gameId === selectedGameKey ? selectedGameReplay : null,
  )
  const eventName = match.eventName || "Match"

  return {
    eventName: match.matchNumber ? `${eventName} - Match ${match.matchNumber}` : eventName,
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
  const location = useLocation()
  const [searchParams] = useSearchParams()
  const routeMatchNumber = (location.state as { matchNumber?: number } | null)?.matchNumber
  const eventSummaryId = searchParams.get("eventSummary")
  const { matchId } = useParams<{ matchId: string }>()
  const navigate = useNavigate()
  const parsedMatchId = matchId ? parseInt(matchId, 10) : null
  const { data, refresh, error, liveUpdateCount } = useMatchDetails(
    parsedMatchId,
  )
  const { isReady: clientReady } = useClientState()
  const { getArtUrl, prefetchCards, isReady: cardArtReady } = useCardArtContext()

  const [selectedGameKey, setSelectedGameKey] = useState<number | null>(() => {
    const gameId = Number(searchParams.get("gameId"))
    return Number.isSafeInteger(gameId) && gameId > 0 ? gameId : null
  })
  const [selectedGameReplay, setSelectedGameReplay] = useState<ReplayData | null>(null)
  const [archetypePending, setArchetypePending] = useState(false)
  const [archetypeError, setArchetypeError] = useState<string | null>(null)
  const [archetypeUndo, setArchetypeUndo] = useState<OpponentArchetypeUpdate[] | null>(null)
  const archetypeMatchId = useRef(parsedMatchId)
  useEffect(() => {
    archetypeMatchId.current = parsedMatchId
    setArchetypeUndo(null); setArchetypeError(null); setArchetypePending(false)
  }, [parsedMatchId])
  const loadOpponentContext = useCallback((signal: AbortSignal) =>
    loadOpponentArchetypeContext(parsedMatchId == null ? [] : [parsedMatchId], data?.format ? [data.format] : [], signal), [parsedMatchId, data?.format])

  useEffect(() => {
    if (!data) return
    const games = data.games ?? []
    if (games.length === 0) {
      setSelectedGameKey(current => current === null ? current : null)
      return
    }

    const gameKeys = new Set(games.map(getGameKey))
    if (selectedGameKey === null || !gameKeys.has(selectedGameKey)) {
      setSelectedGameKey(getGameKey(games[0]))
    }
  }, [data, selectedGameKey])

  useEffect(() => {
    if (!clientReady || parsedMatchId == null || selectedGameKey == null) {
      setSelectedGameReplay(null)
      return
    }

    const controller = new AbortController()
    fetch(getApiUrl(`/api/games/game/${selectedGameKey}/replay`), { signal: controller.signal })
      .then(response => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return response.json()
      })
      .then(readReplayData)
      .then(replay => { if (!controller.signal.aborted) setSelectedGameReplay(replay) })
      .catch(requestError => {
        if (controller.signal.aborted || requestError instanceof Error && requestError.name === "AbortError") return
        setSelectedGameReplay(null)
      })

    return () => controller.abort()
  }, [clientReady, parsedMatchId, selectedGameKey])

  const { detail: deckDetail } = useDeckDetail(data?.deckRevisionId?.toString() ?? null)
  const previewCards = useMemo(() => getDeckPreviewCards(deckDetail), [deckDetail])
  const backgroundCard = previewCards[Math.floor(previewCards.length / 2)]
  const deckBackgroundArtUrl = backgroundCard ? getArtUrl(backgroundCard.name) : null

  useEffect(() => {
    if (!cardArtReady || !backgroundCard?.name || deckBackgroundArtUrl) return
    void prefetchCards([backgroundCard.name])
  }, [backgroundCard?.name, cardArtReady, deckBackgroundArtUrl, prefetchCards])

  const sharedMatch = useMemo(() => data ? toSharedMatchDetails({
    match: { ...data, matchNumber: data.matchNumber ?? routeMatchNumber },
    deckDetail,
    selectedGameKey,
    selectedGameReplay,
    deckBackgroundArtUrl,
  }) : null, [data, deckBackgroundArtUrl, deckDetail, selectedGameKey, selectedGameReplay, routeMatchNumber])

  if (error) {
    return (
      <div className="w-full space-y-4 px-4 pb-4 pt-1">
        <Button variant="ghost" onClick={() => navigate("/history")} className="-ml-3 h-8">
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back
        </Button>
        <div className="rounded-md bg-destructive/15 px-4 py-3 text-sm font-medium text-destructive">
          Error loading match details: {error}
        </div>
      </div>
    )
  }

  if (!data || !sharedMatch) {
    return (
      <div className="w-full space-y-4 px-4 pb-4 pt-1">
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
        <div className="grid gap-3 xl:grid-cols-[23.5rem_minmax(0,1fr)]">
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
        className="flex-1"
        match={sharedMatch}
        initialGameId={selectedGameKey == null ? undefined : String(selectedGameKey)}
        liveUpdateCount={liveUpdateCount}
        onBack={() => navigate(eventSummaryId && /^\d+$/.test(eventSummaryId)
          ? `/history/events/${eventSummaryId}?matchId=${parsedMatchId}&gameId=${selectedGameKey ?? ""}` : "/history")}
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
        loadOpponentArchetypeContext={loadOpponentContext}
        onOpponentArchetypeChange={parsedMatchId == null ? undefined : async (archetype, colors) => {
          setArchetypePending(true)
          setArchetypeError(null)
          try {
            const nextColors = colors ?? data?.opponentDeckColors ?? null
            const previous = await updateOpponentArchetypes([{ matchId: parsedMatchId, archetype, colors: nextColors }])
            if (archetypeMatchId.current !== parsedMatchId) return
            setArchetypeUndo(previous)
            refresh()
          } catch (requestError) {
            if (archetypeMatchId.current === parsedMatchId) setArchetypeError(requestError instanceof Error ? requestError.message : String(requestError))
            throw requestError
          } finally {
            if (archetypeMatchId.current === parsedMatchId) setArchetypePending(false)
          }
        }}
      />
      {archetypeUndo && <OpponentClassificationNotice count={archetypeUndo.length} pending={archetypePending} error={archetypeError}
        onDismiss={() => setArchetypeUndo(null)} onUndo={() => {
          setArchetypePending(true); setArchetypeError(null)
          void updateOpponentArchetypes(archetypeUndo).then(() => {
            if (archetypeMatchId.current !== parsedMatchId) return
            refresh()
            setArchetypeUndo(null)
          }).catch(error => { if (archetypeMatchId.current === parsedMatchId) setArchetypeError(error instanceof Error ? error.message : "Could not undo classification.") })
            .finally(() => { if (archetypeMatchId.current === parsedMatchId) setArchetypePending(false) })
        }} />}
    </>
  )
}
