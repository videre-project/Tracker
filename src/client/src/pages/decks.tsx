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
  type DeckGalleryItem,
  type GameType,
} from "@videreproject/ui"
import {
  type DeckSummary,
  importDeck,
  useDecks,
} from "@/hooks/use-decks"
import type { CardSearchResultDTO } from "@/types/api"
import { getApiUrl } from "@/utils/api-config"

const ALL_FORMATS = "All"

type FormatSummary = {
  format: string
  deckCount: number
}

type ParsedDeckCard = { name: string; quantity: number; sideboard: boolean }

function parseDeckList(value: string): ParsedDeckCard[] {
  const cards: ParsedDeckCard[] = []
  let sideboard = false

  for (const rawLine of value.split(/\r?\n/)) {
    let line = rawLine.trim()
    if (!line) {
      if (cards.some(card => !card.sideboard)) sideboard = true
      continue
    }
    if (/^(deck|mainboard):?$/i.test(line)) {
      sideboard = false
      continue
    }
    if (/^(sideboard|side board|sb):?$/i.test(line)) {
      sideboard = true
      continue
    }
    if (/^sb:/i.test(line)) {
      sideboard = true
      line = line.replace(/^sb:\s*/i, "")
    }

    const match = line.match(/^(\d+)x?\s+(.+)$/i)
    if (!match) throw new Error(`Invalid deck-list line: ${rawLine}`)
    const quantity = Number(match[1])
    const name = match[2].trim()
    if (!Number.isSafeInteger(quantity) || quantity <= 0 || !name) {
      throw new Error(`Invalid deck-list line: ${rawLine}`)
    }
    cards.push({ name, quantity, sideboard })
  }

  if (cards.length === 0) throw new Error("Enter at least one card.")
  return cards
}

async function resolveImportCards(cards: ParsedDeckCard[]) {
  const names = [...new Set(cards.map(card => card.name.toLocaleLowerCase()))]
  const matches = new Map<string, CardSearchResultDTO>()

  await Promise.all(names.map(async normalizedName => {
    const originalName = cards.find(
      card => card.name.toLocaleLowerCase() === normalizedName,
    )!.name
    const params = new URLSearchParams({ q: originalName, limit: "24" })
    const response = await fetch(getApiUrl(`/api/decks/search-cards?${params}`))
    if (!response.ok) throw new Error(`Could not resolve “${originalName}”.`)
    const results = await response.json() as CardSearchResultDTO[]
    const exact = results.find(
      result => result.name.localeCompare(originalName, undefined, { sensitivity: "accent" }) === 0,
    )
    if (!exact) throw new Error(`No exact card match found for “${originalName}”.`)
    matches.set(normalizedName, exact)
  }))

  return cards.map(card => {
    const match = matches.get(card.name.toLocaleLowerCase())!
    return {
      catalogId: match.mtgoId,
      name: match.name,
      quantity: card.quantity,
      cmc: match.cmc,
      rarity: match.rarity,
      colors: match.colors ?? [],
      types: match.types,
    }
  })
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

export default function Decks() {
  const navigate = useNavigate()
  const [searchParams, setSearchParams] = useSearchParams()
  const { decks, loading, error } = useDecks()
  const [query, setQuery] = useState("")
  const [gameType, setGameType] = useState<GameType>("All")
  const [importOpen, setImportOpen] = useState(false)
  const [importName, setImportName] = useState("")
  const [importFormat, setImportFormat] = useState("")
  const [importList, setImportList] = useState("")
  const [importError, setImportError] = useState<string | null>(null)
  const [importing, setImporting] = useState(false)

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

  const galleryDecks = useMemo<DeckGalleryItem[]>(() => formatFilteredDecks.map(deck => ({
    ...deck, colors: deck.colors ?? [], featuredCards: deck.featuredCards ?? undefined,
  })), [formatFilteredDecks])

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

  const submitImport = async (event: React.FormEvent) => {
    event.preventDefault()
    setImporting(true)
    setImportError(null)
    try {
      const parsed = parseDeckList(importList)
      const resolved = await resolveImportCards(parsed)
      const result = await importDeck({
        name: importName.trim(),
        format: importFormat.trim(),
        mainboard: resolved.filter((_, index) => !parsed[index].sideboard),
        sideboard: resolved.filter((_, index) => parsed[index].sideboard),
      })
      navigate(`/decks/${result.revisionId}`, { replace: false })
    } catch (reason) {
      setImportError(reason instanceof Error ? reason.message : "Deck import failed")
    } finally {
      setImporting(false)
    }
  }
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
        onCreateDeck={() => {
          setImportError(null)
          setImportOpen(true)
        }}
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
      {importOpen ? (
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="import-deck-title"
          className="fixed inset-0 z-[100] flex items-center justify-center bg-black/60 p-6 backdrop-blur-sm"
          onMouseDown={event => {
            if (event.target === event.currentTarget && !importing) setImportOpen(false)
          }}
        >
          <form
            onSubmit={event => void submitImport(event)}
            className="w-full max-w-xl space-y-4 rounded-lg border border-sidebar-border bg-card p-5 shadow-2xl"
          >
            <div>
              <h2 id="import-deck-title" className="text-base font-semibold">Import deck</h2>
              <p className="mt-1 text-xs text-muted-foreground">
                Paste a quantity-and-name deck list. Start the sideboard with a blank line or a Sideboard heading.
              </p>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <label className="space-y-1 text-xs font-medium">
                <span>Deck name</span>
                <input
                  autoFocus
                  required
                  value={importName}
                  onChange={event => setImportName(event.target.value)}
                  className="h-9 w-full rounded-md border border-input bg-background px-3 text-sm"
                />
              </label>
              <label className="space-y-1 text-xs font-medium">
                <span>Format</span>
                <input
                  required
                  value={importFormat}
                  onChange={event => setImportFormat(event.target.value)}
                  placeholder="Modern"
                  className="h-9 w-full rounded-md border border-input bg-background px-3 text-sm"
                />
              </label>
            </div>
            <label className="block space-y-1 text-xs font-medium">
              <span>Deck list</span>
              <textarea
                required
                value={importList}
                onChange={event => setImportList(event.target.value)}
                placeholder={"4 Lightning Bolt\n4 Monastery Swiftspear\n\nSideboard\n2 Smash to Smithereens"}
                className="h-64 w-full resize-none rounded-md border border-input bg-background p-3 font-mono text-sm"
              />
            </label>
            {importError ? <p className="text-xs text-destructive">{importError}</p> : null}
            <div className="flex justify-end gap-2">
              <button
                type="button"
                disabled={importing}
                onClick={() => setImportOpen(false)}
                className="h-9 rounded-md border border-input px-4 text-sm hover:bg-accent disabled:opacity-50"
              >
                Cancel
              </button>
              <button
                type="submit"
                disabled={importing}
                className="h-9 rounded-md bg-primary px-4 text-sm font-medium text-primary-foreground hover:bg-primary/90 disabled:opacity-50"
              >
                {importing ? "Importing…" : "Import"}
              </button>
            </div>
          </form>
        </div>
      ) : null}
    </>
  )
}
