/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import type { DateRange } from "react-day-picker"
import { BASIC_LAND_NAMES, CARD_COLORS } from "@videreproject/constants"
import type { DeckCardEntry, DeckGalleryItem } from "@videreproject/ui"
import type { components } from "@/types/videre.g"

import { fetchVidere, getCardMetadata, type VidereCard as CardRow } from "@/lib/videre-api"
import { toDeckCards } from "@/utils/deck-list"
const CACHE_TTL_MS = 2 * 60 * 1000
const BASIC_LANDS = new Set(BASIC_LAND_NAMES.map(name => name.toLocaleLowerCase()))
const COLOR_ORDER = CARD_COLORS.map(color => color.symbol)
type MetagameRow = components["schemas"]["Metagame"]
type DeckRow = components["schemas"]["EventDeck"]

interface CachedMetagameDecks {
  decks: MetagameDeckItem[]
  timestamp: number
}

export interface MetagameDeckItem extends DeckGalleryItem {
  sourceDeckId: number
  mainboard: DeckCardEntry[]
  sideboard: DeckCardEntry[]
  detailsLoaded: boolean
  detailsError?: string | null
}

interface UseMetagameDecksOptions {
  formats: string[]
  dateRange?: DateRange
  decksPerFormat?: number
  hydrateLazily?: boolean
}

const cache = new Map<string, CachedMetagameDecks>()

function parsePercentage(value: string | undefined): number {
  const parsed = Number.parseFloat(value ?? "")
  return Number.isFinite(parsed) ? parsed : 0
}

function resolveColors(cards: DeckCardEntry[], cardById: Map<number, CardRow>): string[] {
  const colors = new Set(cards.flatMap(card => cardById.get(card.catalogId)?.colors ?? []))
  return COLOR_ORDER.filter(color => colors.has(color))
}

function parseTypes(typeLine: string | null | undefined): string[] {
  return typeLine
    ?.split("—", 1)[0]
    .trim()
    .split(/\s+/)
    .filter(Boolean) ?? []
}

function hydrateCards(cards: DeckCardEntry[], cardById: Map<number, CardRow>): DeckCardEntry[] {
  return cards.map(card => {
    const metadata = cardById.get(card.catalogId)
    return {
      ...card,
      name: metadata?.display_name?.trim() || metadata?.name?.trim() || card.name,
      cmc: Math.max(0, Math.floor(metadata?.mana_value ?? metadata?.faces?.[0]?.mana_value ?? 0)),
      colors: metadata?.colors ?? [],
      types: parseTypes(metadata?.type_line ?? metadata?.faces?.[0]?.type_line),
      rarity: metadata?.rarity?.toLocaleLowerCase() || "common",
      imageUrl: metadata?.image_url ?? null,
    }
  })
}

function dateParameter(value: Date | undefined, endOfDay = false): string | undefined {
  if (!value) return undefined
  const date = new Date(value)
  date.setHours(endOfDay ? 23 : 0, endOfDay ? 59 : 0, endOfDay ? 59 : 0, endOfDay ? 999 : 0)
  return date.toISOString()
}

function getDeckKey(deck: Pick<MetagameDeckItem, "format" | "name">): string {
  return `${deck.format}\u0000${deck.name}`
}

function createDeckSummary(format: string, row: MetagameRow): MetagameDeckItem {
  const matches = Math.max(0, row.match_count ?? 0)
  const winrate = parsePercentage(row.match_winrate)
  const wins = Math.min(matches, Math.max(0, Math.round(matches * winrate / 100)))
  return {
    revisionId: row.id,
    sourceDeckId: row.id,
    name: row.archetype,
    format,
    archetype: `${row.percentage} of the field`,
    colors: [],
    wins,
    losses: matches - wins,
    ties: 0,
    mainboard: [],
    sideboard: [],
    mainboardCount: 0,
    sideboardCount: 0,
    featuredCards: [],
    detailsLoaded: false,
  }
}

async function hydrateDeckBatch(
  decks: MetagameDeckItem[],
  dateParams: URLSearchParams,
  signal: AbortSignal,
): Promise<MetagameDeckItem[]> {
  const deckResults = await Promise.allSettled(decks.map(async summary => {
    // Keep a fetched decklist when only its card metadata needs another attempt.
    if (summary.mainboard.length) return summary
    const params = new URLSearchParams(dateParams)
    params.set("limit", "1")
    params.set("archetype", summary.name)
    const { data: rows } = await fetchVidere<DeckRow[]>(
      `/decks/${encodeURIComponent(summary.format.toLocaleLowerCase())}?${params}`,
      signal,
    )
    const deck = rows?.[0]
    if (!deck) throw new Error("No published decklist is available for this archetype.")
    return {
      ...summary,
      revisionId: deck.id,
      sourceDeckId: deck.id,
      mainboard: toDeckCards(deck.mainboard),
      sideboard: toDeckCards(deck.sideboard),
    }
  }))

  const catalogIds = [...new Set(deckResults.flatMap(result =>
    result.status === "fulfilled"
      ? [...result.value.mainboard, ...result.value.sideboard].map(card => card.catalogId) : [],
  ))]
  let metadataError: string | null = null
  const cardById = await getCardMetadata(catalogIds, signal).catch(() => {
    metadataError = "Could not load card details. Try again."
    return new Map<number, CardRow>()
  })
  if (signal.aborted) throw new DOMException("Aborted", "AbortError")

  return deckResults.map((result, index) => {
    if (result.status === "rejected") return {
      ...decks[index], detailsError: "Could not load this decklist. Try again.",
    }
    const { mainboard, sideboard, ...summary } = result.value
    const hydratedMainboard = hydrateCards(mainboard, cardById)
    const hydratedSideboard = hydrateCards(sideboard, cardById)
    return {
      ...summary,
      colors: resolveColors(hydratedMainboard, cardById),
      mainboard: hydratedMainboard,
      sideboard: hydratedSideboard,
      mainboardCount: hydratedMainboard.reduce((total, card) => total + card.quantity, 0),
      sideboardCount: hydratedSideboard.reduce((total, card) => total + card.quantity, 0),
      featuredCards: hydratedMainboard
        .filter(card => !BASIC_LANDS.has(card.name.toLocaleLowerCase()))
        .slice(0, 5),
      detailsLoaded: !metadataError,
      detailsError: metadataError,
    }
  })
}

export function useMetagameDecks({
  formats,
  dateRange,
  decksPerFormat = 1,
  hydrateLazily = false,
}: UseMetagameDecksOptions) {
  const from = dateParameter(dateRange?.from)
  const to = dateParameter(dateRange?.to ?? dateRange?.from, true)
  const formatsKey = formats.map(format => format.trim()).filter(Boolean).join("|")
  const cacheKey = `${formatsKey}:${from ?? "all"}:${to ?? "all"}:${decksPerFormat}`
  const cached = cache.get(cacheKey)
  const hasFreshCache = Boolean(cached && Date.now() - cached.timestamp < CACHE_TTL_MS)
  const [decks, setDecks] = useState<MetagameDeckItem[]>(() => hasFreshCache ? cached!.decks : [])
  const [loading, setLoading] = useState(!hasFreshCache && formats.length > 0)
  const [error, setError] = useState<string | null>(null)

  const stableFormats = useMemo(
    () => formatsKey.split("|").filter(Boolean),
    [formatsKey],
  )

  const decksRef = useRef(decks)
  const pending = useRef(new Map<string, MetagameDeckItem>())
  const requests = useRef(new Map<string, AbortController>())
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const activeCacheKey = useRef(cacheKey)
  activeCacheKey.current = cacheKey

  useEffect(() => () => {
    if (timer.current != null) clearTimeout(timer.current)
    timer.current = null
    pending.current.clear()
    requests.current.forEach(controller => controller.abort())
    requests.current.clear()
    decksRef.current = []
  }, [cacheKey])

  const hydrateDecks = useCallback((requested: MetagameDeckItem[]) => {
    for (const item of requested) {
      const key = getDeckKey(item)
      const deck = decksRef.current.find(deck => getDeckKey(deck) === key)
      if (deck && !deck.detailsLoaded && !requests.current.has(key)) pending.current.set(key, deck)
    }
    if (!pending.current.size || timer.current != null) return
    // Visible tiles share one metadata request instead of requesting the same cards separately.
    timer.current = setTimeout(() => {
      timer.current = null
      const batch = [...pending.current.values()]
      pending.current.clear()
      const keys = new Set(batch.map(getDeckKey))
      const controller = new AbortController()
      keys.forEach(key => requests.current.set(key, controller))
      decksRef.current = decksRef.current.map(deck => keys.has(getDeckKey(deck))
        ? { ...deck, detailsError: null } : deck)
      setDecks(decksRef.current)
      const params = new URLSearchParams()
      if (from) params.set("min_date", from)
      if (to) params.set("max_date", to)
      void hydrateDeckBatch(batch, params, controller.signal)
        .then(loaded => {
          if (controller.signal.aborted || activeCacheKey.current !== cacheKey) return
          const byKey = new Map(loaded.map(deck => [getDeckKey(deck), deck]))
          const next = decksRef.current.map(deck => byKey.get(getDeckKey(deck)) ?? deck)
          decksRef.current = next
          cache.set(cacheKey, { decks: next, timestamp: Date.now() })
          setDecks(next)
          setError(next.find(deck => deck.detailsError)?.detailsError ?? null)
        })
        .catch(reason => {
          if (!controller.signal.aborted) setError(reason instanceof Error ? reason.message : "Could not load this decklist.")
        })
        .finally(() => {
          keys.forEach(key => { if (requests.current.get(key) === controller) requests.current.delete(key) })
        })
    }, 25)
  }, [cacheKey, from, to])

  useEffect(() => {
    if (stableFormats.length === 0) {
      decksRef.current = []
      setDecks([])
      setLoading(false)
      setError(null)
      return
    }

    const currentCache = cache.get(cacheKey)
    if (currentCache && Date.now() - currentCache.timestamp < CACHE_TTL_MS) {
      decksRef.current = currentCache.decks
      setDecks(currentCache.decks)
      setLoading(false)
      setError(currentCache.decks.find(deck => deck.detailsError)?.detailsError ?? null)
      return
    }

    const controller = new AbortController()
    let active = true
    setLoading(true)
    setError(null)

    void (async () => {
      const dateParams = new URLSearchParams()
      if (from) dateParams.set("min_date", from)
      if (to) dateParams.set("max_date", to)

      const metagameResults = await Promise.allSettled(stableFormats.map(async format => {
        const params = new URLSearchParams(dateParams)
        params.set("limit", String(decksPerFormat))
        const rows = (await fetchVidere<MetagameRow[]>(
          `/metagame/${encodeURIComponent(format.toLocaleLowerCase())}?${params}`,
          controller.signal,
        )).data
        return (rows ?? []).map(row => ({ format, row }))
      }))

      const formatRows = metagameResults.flatMap(result =>
        result.status === "fulfilled" ? result.value : [],
      )
      const failedFormats = metagameResults.filter(result => result.status === "rejected").length
      if (formatRows.length === 0 && failedFormats === metagameResults.length) {
        throw new Error("Could not load recent tournament results from the Videre API.")
      }

      const summaries = formatRows.map(({ format, row }) => createDeckSummary(format, row))
      const nextDecks = hydrateLazily ? summaries : await hydrateDeckBatch(summaries, dateParams, controller.signal)

      if (!active) return
      cache.set(cacheKey, { decks: nextDecks, timestamp: Date.now() })
      decksRef.current = nextDecks
      setDecks(nextDecks)
      setError(nextDecks.find(deck => deck.detailsError)?.detailsError ?? null)
    })().catch(reason => {
      if (!active || (reason instanceof DOMException && reason.name === "AbortError")) return
      decksRef.current = []
      setDecks([])
      setError(reason instanceof Error
        ? reason.message
        : "Could not load recent tournament results from the Videre API.")
    }).finally(() => {
      if (active) setLoading(false)
    })

    return () => {
      active = false
      controller.abort()
    }
  }, [cacheKey, decksPerFormat, from, hydrateLazily, stableFormats, to])

  return { decks, loading, error, hydrateDecks }
}
