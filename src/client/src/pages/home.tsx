/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useState, useEffect, useMemo } from "react"
import { Link, useNavigate } from "react-router-dom"
import type { DateRange } from "react-day-picker"
import { ACTIVE_FORMATS } from "@videreproject/constants"

import {
  DashboardLayout,
  compareFormats,
  isLimitedFormat,
  type DashboardGameType,
} from "@videreproject/ui"
import { useGames } from "@/hooks/use-games"
import { useAggregatedArchetypes } from "@/hooks/use-decks"
import { useCardArtContext } from "@/hooks/use-card-art"
import {
  useMetagameDecks,
  type MetagameDeckItem,
} from "@/hooks/use-metagame-decks"
import { MetagameDeckPreview } from "@/components/metagame-deck-preview"
import { getApiUrl } from "@/utils/api-config"

const METAGAME_DECKS_PER_FORMAT = 16

export default function Home() {
  const navigate = useNavigate()
  const [dateRange, setDateRange] = useState<DateRange | undefined>(() => {
    const to = new Date()
    const from = new Date()
    from.setDate(to.getDate() - 30)
    return { from, to }
  })

  const effectiveRange = dateRange || "ALL"

  const [selectedFormat, setSelectedFormat] = useState<string>("")
  const [gameType, setGameType] = useState<DashboardGameType>("Constructed")

  const { formats, stats, trend: trendData, loading } = useGames(effectiveRange, selectedFormat)

  const filteredFormats = useMemo(() => {
    let result = [...new Set([...ACTIVE_FORMATS, ...formats])]
    if (gameType === "Limited") {
      result = result.filter(isLimitedFormat)
    } else if (gameType === "Constructed") {
      result = result.filter(format => !isLimitedFormat(format))
    }

    return [...result].sort(compareFormats)
  }, [formats, gameType])

  useEffect(() => {
    if (selectedFormat && !filteredFormats.includes(selectedFormat)) {
      setSelectedFormat("")
    }
  }, [gameType, filteredFormats, selectedFormat])

  const { archetypes, loading: archetypesLoading } = useAggregatedArchetypes(
    effectiveRange,
    selectedFormat || undefined,
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
      const response = await fetch(getApiUrl("/api/decks/import"), {
        method: "POST",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          name: selectedDeck.name,
          format: selectedDeck.format,
          archetype: selectedDeck.name,
          mainboard: selectedDeck.mainboard,
          sideboard: selectedDeck.sideboard,
        }),
      })
      const payload = await response.json().catch(() => ({})) as {
        revisionId?: number
        error?: string
      }
      if (!response.ok || payload.revisionId == null) {
        throw new Error(payload.error || `Deck import failed (${response.status})`)
      }

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
      gameType={gameType}
      onGameTypeChange={setGameType}
      selectedFormat={selectedFormat}
      formats={filteredFormats}
      onFormatChange={setSelectedFormat}
      dateRange={dateRange}
      onDateRangeChange={setDateRange}
      stats={stats}
      loading={loading}
      trend={trendData}
      archetypes={archetypes}
      archetypesLoading={archetypesLoading}
      metagameDecks={metagameDecks}
      metagameDecksLoading={metagameDecksLoading}
      metagameDecksError={metagameDecksError}
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
      renderViewMoreDecks={({ children }) => <Link to="/decks">{children}</Link>}
      />

      <MetagameDeckPreview
        deck={selectedDeck}
        dateRange={dateRange}
        importing={importing}
        importError={importError}
        onOpenChange={open => {
          if (!open && !importing) setSelectedDeck(null)
        }}
        onClose={() => setSelectedDeck(null)}
        onImport={() => void importSelectedDeck()}
      />
    </>
  )
}
