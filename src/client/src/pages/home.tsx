/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useApplicationSettings } from "@/hooks/use-application-settings"
import { initialDateRange } from "@/lib/application-preferences"
import { useState, useEffect, useMemo, useCallback } from "react"
import { Link, useNavigate } from "react-router-dom"
import type { DateRange } from "react-day-picker"
import { ACTIVE_FORMATS } from "@videreproject/constants"

import {
  DashboardLayout,
  compareFormats,
  isLimitedFormat,
  type DashboardGameType,
} from "@videreproject/ui"
import { useGameFormats, useGames } from "@/hooks/use-games"
import { useClientState } from "@/hooks/use-client-state"
import { useNDJSONStream } from "@/hooks/use-ndjson-stream"
import { importDeck, useAggregatedArchetypes } from "@/hooks/use-decks"
import { useCardArtContext } from "@/hooks/use-card-art"
import {
  useMetagameDecks,
  type MetagameDeckItem,
} from "@/hooks/use-metagame-decks"
import { MetagameDeckPreview } from "@/components/metagame-deck-preview"
import { MatchupAnalysis } from "@/components/matchup-analysis"
import type { DashboardDeckSelection } from "@/utils/match-results"
import { getApiUrl } from "@/utils/api-config"

const METAGAME_DECKS_PER_FORMAT = 16

export default function Home() {
  const { preferences } = useApplicationSettings()
  const navigate = useNavigate()
  const [dateRange, setDateRange] = useState<DateRange | undefined>(() => initialDateRange(preferences.historyRangeDays))

  const effectiveRange = dateRange || "ALL"

  const [selectedFormat, setSelectedFormat] = useState<string>("")
  const [performanceDeck, setPerformanceDeck] = useState<DashboardDeckSelection>(null)
  const [gameType, setGameType] = useState<DashboardGameType>("Constructed")
  const [historyRevision, setHistoryRevision] = useState(0)
  const refreshDashboard = useCallback(() => setHistoryRevision(revision => revision + 1), [])
  const { isReady, queryCache } = useClientState()

  // One history subscription refreshes all of the dashboard's recorded data.
  useNDJSONStream<number>({
    url: getApiUrl("/api/games/history/watch"),
    enabled: isReady,
    restartKey: queryCache,
    onMessage: refreshDashboard,
    onOpen: refreshDashboard,
    reconnectDelay: 2000,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })

  const { formats, loading: formatsLoading, error: formatsError } = useGameFormats(gameType, historyRevision)
  const { stats, trend: trendData, loading } = useGames(effectiveRange, selectedFormat, gameType, historyRevision)

  const filteredFormats = useMemo(() => {
    const result = [...new Set([...(gameType === "Limited" ? [] : ACTIVE_FORMATS), ...formats])]

    return [...result].sort(compareFormats)
  }, [formats, gameType])

  useEffect(() => {
    if (selectedFormat && !formatsLoading && !formatsError && !filteredFormats.includes(selectedFormat)) {
      setSelectedFormat("")
    }
  }, [filteredFormats, formatsLoading, formatsError, selectedFormat])

  const { archetypes, loading: archetypesLoading } = useAggregatedArchetypes(
    effectiveRange,
    selectedFormat || undefined,
    gameType,
    historyRevision,
  )
  const metagameFormats = useMemo(() => {
    if (gameType === "Limited") return []
    if (selectedFormat) return [selectedFormat]
    return [...ACTIVE_FORMATS].filter(format => !isLimitedFormat(format)).sort(compareFormats)
  }, [gameType, selectedFormat])
  const {
    decks: metagameDecks,
    loading: metagameDecksLoading,
    error: metagameDecksError,
    hydrateDecks,
  } = useMetagameDecks({
    formats: metagameFormats,
    dateRange,
    decksPerFormat: selectedFormat ? METAGAME_DECKS_PER_FORMAT : 1,
  })
  const [selectedDeck, setSelectedDeck] = useState<MetagameDeckItem | null>(null)
  const [importing, setImporting] = useState(false)
  const [importError, setImportError] = useState<string | null>(null)

  useEffect(() => {
    if (!selectedDeck) return
    const refreshedDeck = metagameDecks.find(deck =>
      deck.format === selectedDeck.format && deck.name === selectedDeck.name,
    )
    if (refreshedDeck && refreshedDeck !== selectedDeck) setSelectedDeck(refreshedDeck)
  }, [metagameDecks, selectedDeck])

  const importSelectedDeck = async () => {
    if (!selectedDeck?.detailsLoaded || importing) return
    setImporting(true)
    setImportError(null)
    try {
      const payload = await importDeck({
        name: selectedDeck.name,
        format: selectedDeck.format,
        archetype: selectedDeck.name,
        mainboard: selectedDeck.mainboard,
        sideboard: selectedDeck.sideboard,
      })

      const deck = selectedDeck
      setSelectedDeck(null)
      navigate(`/decks/${payload.revisionId}`, {
        state: {
          deckName: deck.name,
          deckFormat: deck.format,
          deckColors: deck.colors,
          deckArchetype: deck.name,
          deckTimestamp: new Date().toISOString(),
          deckMainCount: deck.mainboardCount,
          deckSideCount: deck.sideboardCount,
        },
      })
    } catch (reason) {
      setImportError(reason instanceof Error ? reason.message : "Could not import this deck.")
    } finally {
      setImporting(false)
    }
  }

  const metagameSearchHref = useMemo(() => {
    const params = new URLSearchParams()
    if (selectedFormat) params.set("format", selectedFormat)
    if (dateRange?.from) params.set("from", dateRange.from.toISOString())
    if (dateRange?.to) params.set("to", dateRange.to.toISOString())
    const query = params.toString()
    return query ? `/metagame?${query}` : "/metagame"
  }, [dateRange, selectedFormat])
  const { getArtUrl, prefetchCards, isReady: clientReady } = useCardArtContext()

  useEffect(() => {
    if (archetypesLoading || archetypes.length === 0 || !clientReady) return
    const topCards = archetypes.slice(0, 10).map(a => a.topCard).filter(Boolean)
    prefetchCards(topCards)
  }, [clientReady, archetypesLoading, archetypes, prefetchCards])

  return (
    <>
      <DashboardLayout
      stickyFilterOffset={56}
      gameType={gameType}
      onGameTypeChange={value => { setGameType(value); setPerformanceDeck(null) }}
      selectedFormat={selectedFormat}
      formats={filteredFormats}
      onFormatChange={value => { setSelectedFormat(value); setPerformanceDeck(null) }}
      dateRange={dateRange}
      onDateRangeChange={value => { setDateRange(value); setPerformanceDeck(null) }}
      stats={stats}
      loading={loading}
      trend={trendData}
      archetypes={archetypes}
      archetypesLoading={archetypesLoading && !archetypes.length}
      selectedArchetype={performanceDeck?.archetype}
      onDeckClick={archetype => setPerformanceDeck({ archetype })}
      metagameDecks={metagameDecks}
      metagameDecksLoading={metagameDecksLoading}
      metagameDecksError={metagameDecks.length ? null : metagameDecksError}
      onMetagameDeckClick={(_, deck) => {
        const selected = metagameDecks.find(item =>
          item.format === deck.format && item.name === deck.name,
        )
        if (!selected) return
        setImportError(null)
        hydrateDecks([selected])
        setSelectedDeck(selected)
      }}
      renderSearchMoreMetagameDecks={({ children }) => (
        <Link
          to={metagameSearchHref}
          className="block h-full text-left outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
        >
          {children}
        </Link>
      )}
      getArtUrl={getArtUrl}
      performanceContent={
        <MatchupAnalysis dateRange={dateRange} format={selectedFormat} gameType={gameType}
          archetypeDecks={metagameDecks} selection={performanceDeck} onSelectionChange={setPerformanceDeck}
          refreshKey={historyRevision}
          />
      }
      />

      <MetagameDeckPreview
        deck={selectedDeck}
        dateRange={dateRange}
        importing={importing}
        importError={importError}
        detailsError={selectedDeck?.detailsError}
        onRetry={() => { if (selectedDeck) hydrateDecks([selectedDeck]) }}
        onOpenChange={open => {
          if (!open && !importing) setSelectedDeck(null)
        }}
        onClose={() => setSelectedDeck(null)}
        onImport={() => void importSelectedDeck()}
      />
    </>
  )
}
