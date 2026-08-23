/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import { Search } from "lucide-react"
import { useNavigate, useSearchParams } from "react-router-dom"
import type { DateRange } from "react-day-picker"
import { ACTIVE_FORMATS } from "@videreproject/constants"
import {
  DashboardFilters,
  DeckGalleryTile,
  Input,
  compareFormats,
  isLimitedFormat,
  type DashboardGameType,
} from "@videreproject/ui"
import {
  useMetagameDecks,
  type MetagameDeckItem,
} from "@/hooks/use-metagame-decks"
import { MetagameDeckPreview } from "@/components/metagame-deck-preview"
import { getApiUrl } from "@/utils/api-config"

const SEARCH_LIMIT = 100

function parseDate(value: string | null): Date | undefined {
  if (!value) return undefined
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? undefined : date
}

function defaultDateRange(): DateRange {
  const to = new Date()
  const from = new Date(to)
  from.setDate(to.getDate() - 30)
  return { from, to }
}

function readDateRange(params: URLSearchParams): DateRange | undefined {
  const from = parseDate(params.get("from"))
  const to = parseDate(params.get("to"))
  if (from || to) return { from, to }
  return defaultDateRange()
}

interface ImportDeckResult {
  revisionId: number
  netDeckId: number
  created: boolean
}

function LazyMetagameDeckTile({
  deck,
  onVisible,
  onDeckClick,
}: {
  deck: MetagameDeckItem
  onVisible: (deck: MetagameDeckItem) => void
  onDeckClick: (deck: MetagameDeckItem) => void
}) {
  const tileRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const tile = tileRef.current
    if (!tile || deck.detailsLoaded) return

    const observer = new IntersectionObserver(entries => {
      if (!entries.some(entry => entry.isIntersecting)) return
      onVisible(deck)
      observer.disconnect()
    }, {
      root: tile.closest<HTMLElement>(".overflow-y-auto"),
      rootMargin: "600px 0px",
    })
    observer.observe(tile)
    return () => observer.disconnect()
  }, [deck, onVisible])

  return (
    <div ref={tileRef} className="min-w-0 [&>button]:w-full">
      <DeckGalleryTile
        deck={deck}
        onDeckClick={() => onDeckClick(deck)}
      />
    </div>
  )
}

export default function Metagame() {
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const [gameType, setGameType] = useState<DashboardGameType>("Constructed")
  const [dateRange, setDateRange] = useState<DateRange | undefined>(() => readDateRange(searchParams))
  const [query, setQuery] = useState("")
  const [selectedDeck, setSelectedDeck] = useState<MetagameDeckItem | null>(null)
  const [importing, setImporting] = useState(false)
  const [importError, setImportError] = useState<string | null>(null)

  const formats = useMemo(() => {
    const available = [...new Set(ACTIVE_FORMATS)]
    if (gameType === "Limited") return available.filter(isLimitedFormat).sort(compareFormats)
    if (gameType === "Constructed") return available.filter(format => !isLimitedFormat(format)).sort(compareFormats)
    return available.sort(compareFormats)
  }, [gameType])

  const selectedFormat = searchParams.get("format") ?? ""
  const effectiveFormat = formats.some(format => format === selectedFormat) ? selectedFormat : ""
  const fetchFormats = gameType === "Limited"
    ? []
    : effectiveFormat
      ? [effectiveFormat]
      : formats
  const { decks, loading, error, hydrateDecks } = useMetagameDecks({
    formats: fetchFormats,
    dateRange,
    decksPerFormat: SEARCH_LIMIT,
    hydrateLazily: true,
  })

  const hydrateVisibleDeck = useCallback((deck: MetagameDeckItem) => {
    hydrateDecks([deck])
  }, [hydrateDecks])

  useEffect(() => {
    if (!selectedDeck) return
    const refreshedDeck = decks.find(deck =>
      deck.format === selectedDeck.format && deck.name === selectedDeck.name,
    )
    if (refreshedDeck && refreshedDeck !== selectedDeck) setSelectedDeck(refreshedDeck)
  }, [decks, selectedDeck])

  const visibleDecks = useMemo(() => {
    const normalizedQuery = query.trim().toLocaleLowerCase()
    if (!normalizedQuery) return decks
    return decks.filter(deck => [
      deck.name,
      deck.format,
      deck.archetype ?? "",
      ...(deck.colors ?? []),
    ].join(" ").toLocaleLowerCase().includes(normalizedQuery))
  }, [decks, query])

  const updateFormat = (format: string) => {
    setSearchParams(previous => {
      const next = new URLSearchParams(previous)
      if (format) next.set("format", format)
      else next.delete("format")
      return next
    }, { replace: true })
  }

  const updateDateRange = (nextRange: DateRange | undefined) => {
    setDateRange(nextRange)
    setSearchParams(previous => {
      const next = new URLSearchParams(previous)
      if (nextRange?.from) next.set("from", nextRange.from.toISOString())
      else next.delete("from")
      if (nextRange?.to) next.set("to", nextRange.to.toISOString())
      else next.delete("to")
      return next
    }, { replace: true })
  }

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
      const payload = await response.json().catch(() => ({})) as Partial<ImportDeckResult> & {
        error?: string
      }
      if (!response.ok || payload.revisionId == null) {
        throw new Error(payload.error || `Deck import failed (${response.status})`)
      }

      const revisionId = payload.revisionId
      const deck = selectedDeck
      setSelectedDeck(null)
      navigate(`/decks/${revisionId}`, {
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

  return (
    <main className="videre-ui flex min-h-[calc(100vh-3.5rem)] flex-col gap-4 px-4 pb-8 pt-2 font-sans">
      <div className="flex flex-col gap-1">
        <h1 className="text-lg font-medium">Metagame</h1>
        <p className="text-sm text-muted-foreground">
          Search the leading archetypes from recent Magic Online events.
        </p>
      </div>

      <DashboardFilters
        gameType={gameType}
        onGameTypeChange={setGameType}
        selectedFormat={effectiveFormat}
        formats={formats}
        onFormatChange={updateFormat}
        dateRange={dateRange}
        onDateRangeChange={updateDateRange}
        middleContent={(
          <div className="relative w-full max-w-xl">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
            <Input
              value={query}
              onChange={event => setQuery(event.target.value)}
              placeholder="Search archetypes"
              aria-label="Search metagame archetypes"
              className="h-9 pl-9"
            />
          </div>
        )}
      />

      {error ? (
        <div className="rounded-lg border border-destructive/30 bg-destructive/10 p-6 text-sm text-destructive">
          {error}
        </div>
      ) : loading ? (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2 2xl:grid-cols-3">
          {Array.from({ length: 6 }).map((_, index) => (
            <div key={index} className="h-64 animate-pulse rounded-lg border border-sidebar-border/60 bg-card/60" />
          ))}
        </div>
      ) : visibleDecks.length === 0 ? (
        <div className="flex min-h-64 items-center justify-center rounded-lg border border-sidebar-border/60 bg-card/30 text-sm text-muted-foreground">
          No metagame decks match the current filters.
        </div>
      ) : (
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2 2xl:grid-cols-3">
          {visibleDecks.map(deck => (
            <LazyMetagameDeckTile
              key={`${deck.format}-${deck.name}-${deck.revisionId}`}
              deck={deck}
              onVisible={hydrateVisibleDeck}
              onDeckClick={selected => {
                setImportError(null)
                hydrateDecks([selected])
                setSelectedDeck(selected)
              }}
            />
          ))}
        </div>
      )}

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
    </main>
  )
}
