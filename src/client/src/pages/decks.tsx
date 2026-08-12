/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

"use client"

import { useEffect, useMemo, useState } from "react"
import { createPortal } from "react-dom"
import { useNavigate, useSearchParams } from "react-router-dom"
import { Loader2 } from "lucide-react"
import {
  DecksLayout,
  compareFormats,
  isLimitedFormat,
  type DeckFeaturedCard,
  type DeckGalleryItem,
  type GameType,
} from "@videreproject/ui"
import {
  type CardEntry,
  type DeckDetail,
  type DeckSummary,
  useDecks,
} from "@/hooks/use-decks"
import { getApiUrl } from "@/utils/api-config"

const ALL_FORMATS = "All"

type FormatSummary = {
  format: string
  deckCount: number
}

function normalizeSearchFormat(value?: string | null) {
  const trimmed = (value ?? "").trim().toLowerCase().replace(/\s+/g, " ")
  return trimmed
}

function resolveFilteredFormat(queryValue: string | null, summaries: FormatSummary[]) {
  const normalizedQuery = normalizeSearchFormat(queryValue)
  if (!normalizedQuery || normalizedQuery === ALL_FORMATS.toLowerCase()) return ALL_FORMATS

  const exact = summaries.find(
    summary => normalizeSearchFormat(summary.format) === normalizedQuery,
  )
  if (exact) return exact.format

  const compact = summaries.find(
    summary =>
      normalizeSearchFormat(summary.format).replace(/\s/g, "") ===
      normalizedQuery.replace(/\s/g, ""),
  )
  return compact?.format ?? ALL_FORMATS
}

function getFeaturedCards(detail: DeckDetail | null): DeckFeaturedCard[] {
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

function toGalleryItem(
  deck: DeckSummary,
  featuredOverride?: CardEntry[] | DeckFeaturedCard[],
): DeckGalleryItem {
  const featured =
    deck.featuredCards?.length
      ? deck.featuredCards
      : featuredOverride?.length
        ? featuredOverride
        : undefined

  return {
    revisionId: deck.revisionId,
    name: deck.name,
    format: deck.format,
    archetype: deck.archetype,
    colors: deck.colors ?? [],
    wins: deck.wins,
    losses: deck.losses,
    ties: deck.ties,
    mainboardCount: deck.mainboardCount,
    sideboardCount: deck.sideboardCount,
    timestamp: deck.timestamp,
    netDeckId: deck.netDeckId,
    featuredCards: featured?.map(card => ({
      catalogId: card.catalogId,
      name: card.name,
      quantity: card.quantity,
    })),
  }
}

export default function Decks() {
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const { decks, loading, error } = useDecks()
  const [query, setQuery] = useState("")
  const [gameType, setGameType] = useState<GameType>("All")
  const [previewCardsByRevision, setPreviewCardsByRevision] = useState<
    Record<number, DeckFeaturedCard[]>
  >({})

  const formats = useMemo(
    () => Object.keys(decks).filter(Boolean).sort(compareFormats),
    [decks],
  )

  const filteredFormats = useMemo(() => {
    if (gameType === "Limited") {
      return formats.filter(isLimitedFormat)
    }
    if (gameType === "Constructed") {
      return formats.filter(format => !isLimitedFormat(format))
    }
    return formats
  }, [formats, gameType])

  const allDecks = useMemo(
    () => formats.flatMap(format => decks[format] ?? []),
    [decks, formats],
  )

  const summaries = useMemo<FormatSummary[]>(() => {
    const buildSummary = (format: string, formatDecks: DeckSummary[]) => ({
      format,
      deckCount: formatDecks.length,
    })

    return [
      buildSummary(ALL_FORMATS, allDecks),
      ...formats.map(format => buildSummary(format, decks[format] ?? [])),
    ]
  }, [allDecks, decks, formats])

  const selectedFormat = useMemo(
    () => resolveFilteredFormat(searchParams.get("format"), summaries),
    [searchParams, summaries],
  )

  // Format-filtered decks (query filtering left to layout via filterByQuery)
  const formatFilteredDecks = useMemo(() => {
    const source =
      selectedFormat === ALL_FORMATS ? allDecks : (decks[selectedFormat] ?? [])
    return source
  }, [allDecks, decks, selectedFormat])

  // Hydrate featured cards for visible tiles
  useEffect(() => {
    const missingDecks = formatFilteredDecks
      .filter(deck => {
        if (deck.featuredCards?.length) return false
        if (Object.prototype.hasOwnProperty.call(previewCardsByRevision, deck.revisionId)) {
          return false
        }
        return true
      })
      .slice(0, 24)

    if (missingDecks.length === 0) return

    const abortController = new AbortController()

    Promise.all(
      missingDecks.map(async deck => {
        try {
          const response = await fetch(getApiUrl(`/api/decks/${deck.revisionId}`), {
            signal: abortController.signal,
          })
          if (!response.ok) return null
          const detail = (await response.json()) as DeckDetail
          return [deck.revisionId, getFeaturedCards(detail)] as const
        } catch (err) {
          if (err instanceof Error && err.name === "AbortError") return null
          return null
        }
      }),
    ).then(entries => {
      if (abortController.signal.aborted) return

      setPreviewCardsByRevision(current => {
        const next = { ...current }
        for (const entry of entries) {
          if (!entry) continue
          const [revisionId, cards] = entry
          next[revisionId] = cards
        }
        return next
      })
    })

    return () => abortController.abort()
  }, [formatFilteredDecks, previewCardsByRevision])

  const galleryDecks = useMemo(
    () =>
      formatFilteredDecks.map(deck =>
        toGalleryItem(deck, previewCardsByRevision[deck.revisionId]),
      ),
    [formatFilteredDecks, previewCardsByRevision],
  )

  // Clear selected format if it's no longer in the filtered list
  useEffect(() => {
    if (
      selectedFormat &&
      selectedFormat !== ALL_FORMATS &&
      !filteredFormats.includes(selectedFormat)
    ) {
      setSearchParams(
        previous => {
          const next = new URLSearchParams(previous)
          next.delete("format")
          return next
        },
        { replace: true },
      )
    }
  }, [gameType, filteredFormats, selectedFormat, setSearchParams])

  const onFormatSelect = (format: string) => {
    setSearchParams(
      previous => {
        const next = new URLSearchParams(previous)
        if (!format || format === ALL_FORMATS) {
          next.delete("format")
        } else {
          next.set("format", format)
        }
        return next
      },
      { replace: true },
    )
  }

  const totalDecks = allDecks.length
  const totalFormats = formats.length
  const breadcrumbContextHost =
    typeof document === "undefined" ? null : document.getElementById("page-header-context")

  const breadcrumb = (
    <div className="inline-flex min-w-0 items-center gap-2 text-xs text-muted-foreground">
      {loading ? (
        <Loader2 className="h-3.5 w-3.5 animate-spin" aria-label="Loading decks" />
      ) : (
        <>
          <span>
            {totalDecks.toLocaleString()} deck{totalDecks === 1 ? "" : "s"}
          </span>
          <span className="h-1 w-1 rounded-full bg-muted-foreground/45" />
          <span>
            {totalFormats.toLocaleString()} format{totalFormats === 1 ? "" : "s"}
          </span>
        </>
      )}
    </div>
  )

  return (
    <>
      {breadcrumbContextHost ? createPortal(breadcrumb, breadcrumbContextHost) : null}
      <DecksLayout
        className="h-[calc(100vh-2.5rem)]"
        decks={galleryDecks}
        formats={filteredFormats}
        selectedFormat={selectedFormat}
        onFormatChange={onFormatSelect}
        gameType={gameType}
        onGameTypeChange={setGameType}
        query={query}
        onQueryChange={setQuery}
        filterByQuery
        loading={loading}
        error={error}
        empty={!loading && allDecks.length === 0}
        onDeckClick={(revisionId, deck) => {
          navigate(`/decks/${revisionId}`, {
            state: {
              deckName: deck.name,
              deckFormat: deck.format,
              deckColors: deck.colors,
              deckArchetype: deck.archetype,
              deckTimestamp: deck.timestamp,
              deckMainCount: deck.mainboardCount,
              deckSideCount: deck.sideboardCount,
            },
          })
        }}
      />
    </>
  )
}
