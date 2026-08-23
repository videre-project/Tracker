/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import type { DateRange } from "react-day-picker"
import { BASIC_LAND_NAMES } from "@videreproject/constants"
import type { DeckCardEntry, DeckGalleryItem } from "@videreproject/ui"

const VIDERE_API_URL = "https://api.videreproject.com"
const CACHE_TTL_MS = 2 * 60 * 1000
const HYDRATION_BATCH_DELAY_MS = 25
const BASIC_LANDS = new Set(BASIC_LAND_NAMES.map(name => name.toLocaleLowerCase()))
const COLOR_ORDER = ["W", "U", "B", "R", "G"] as const
const TUPLE_RE = /^\((\d+),\s*(?:"([^"]*)"|([^,()]+)),\s*(\d+)\)$/

interface VidereResponse<T> {
  data?: T
}

interface MetagameRow {
  id: number
  archetype: string
  count: number
  percentage: string
  match_count: number
  match_winrate: string
}

interface DeckRow {
  id: number
  mainboard?: unknown[]
  sideboard?: unknown[]
}

interface CardRow {
  id: number
  name?: string | null
  display_name?: string | null
  mana_value?: number | null
  type_line?: string | null
  colors?: string[] | null
  rarity?: string | null
  image_url?: string | null
}

interface CachedMetagameDecks {
  decks: MetagameDeckItem[]
  timestamp: number
}

export interface MetagameDeckItem extends DeckGalleryItem {
  sourceDeckId: number
  mainboard: DeckCardEntry[]
  sideboard: DeckCardEntry[]
  detailsLoaded: boolean
}

export interface UseMetagameDecksOptions {
  formats: string[]
  dateRange?: DateRange
  decksPerFormat?: number
  hydrateLazily?: boolean
}

const cache = new Map<string, CachedMetagameDecks>()
const cardMetadataCache = new Map<number, CardRow>()

function parsePercentage(value: string | undefined): number {
  const parsed = Number.parseFloat(value ?? "")
  return Number.isFinite(parsed) ? parsed : 0
}

function parseDeckCards(values: unknown[] | undefined): DeckCardEntry[] {
  if (!values) return []
  return values.flatMap(value => {
    if (typeof value !== "string") return []
    const match = value.match(TUPLE_RE)
    if (!match) return []
    const catalogId = Number(match[1])
    const name = (match[2] ?? match[3] ?? "").trim()
    const quantity = Number(match[4])
    return catalogId > 0 && name ? [{ catalogId, name, quantity }] : []
  })
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
      cmc: Math.max(0, Math.floor(metadata?.mana_value ?? 0)),
      colors: metadata?.colors ?? [],
      types: parseTypes(metadata?.type_line),
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

async function fetchData<T>(
  path: string,
  signal: AbortSignal,
  init?: Omit<RequestInit, "signal">,
): Promise<T> {
  const response = await fetch(`${VIDERE_API_URL}${path}`, { ...init, signal })
  if (!response.ok) throw new Error(`Videre API request failed (${response.status})`)
  const payload = await response.json() as VidereResponse<T>
  return payload.data as T
}

async function fetchOptionalData<T>(
  path: string,
  signal: AbortSignal,
  init?: Omit<RequestInit, "signal">,
): Promise<T | null> {
  try {
    return await fetchData<T>(path, signal, init)
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") throw error
    return null
  }
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
  const deckResults = await Promise.all(decks.map(async summary => {
    const params = new URLSearchParams(dateParams)
    params.set("limit", "1")
    params.set("archetype", summary.name)
    const rows = await fetchOptionalData<DeckRow[]>(
      `/decks/${encodeURIComponent(summary.format.toLocaleLowerCase())}?${params}`,
      signal,
    )
    const deck = rows?.[0]
    return {
      summary,
      deck,
      mainboard: parseDeckCards(deck?.mainboard),
      sideboard: parseDeckCards(deck?.sideboard),
    }
  }))

  const catalogIds = [...new Set(deckResults.flatMap(result =>
    [...result.mainboard, ...result.sideboard].map(card => card.catalogId),
  ))]
  const missingCatalogIds = catalogIds.filter(catalogId => !cardMetadataCache.has(catalogId))
  const cardPages = await Promise.all(missingCatalogIds
    .reduce<number[][]>((chunks, catalogId, index) => {
      const chunkIndex = Math.floor(index / 500)
      const chunk = chunks[chunkIndex] ?? (chunks[chunkIndex] = [])
      chunk.push(catalogId)
      return chunks
    }, [])
    .map(ids => fetchOptionalData<CardRow[]>(
      "/cards/search?limit=500&unique=prints",
      signal,
      {
        method: "QUERY",
        headers: { "content-type": "application/json" },
        body: JSON.stringify({
          collection: { ids, mode: "only", match: "prints" },
        }),
      },
    )))
  cardPages.flatMap(cards => cards ?? []).forEach(card => {
    cardMetadataCache.set(card.id, card)
  })
  const cardById = new Map(catalogIds.flatMap(catalogId => {
    const card = cardMetadataCache.get(catalogId)
    return card ? [[catalogId, card] as const] : []
  }))

  return deckResults.map(({ summary, deck, mainboard, sideboard }) => {
    const hydratedMainboard = hydrateCards(mainboard, cardById)
    const hydratedSideboard = hydrateCards(sideboard, cardById)
    return {
      ...summary,
      revisionId: deck?.id ?? summary.revisionId,
      sourceDeckId: deck?.id ?? summary.sourceDeckId,
      colors: resolveColors(hydratedMainboard, cardById),
      mainboard: hydratedMainboard,
      sideboard: hydratedSideboard,
      mainboardCount: hydratedMainboard.reduce((total, card) => total + card.quantity, 0),
      sideboardCount: hydratedSideboard.reduce((total, card) => total + card.quantity, 0),
      featuredCards: hydratedMainboard
        .filter(card => !BASIC_LANDS.has(card.name.toLocaleLowerCase()))
        .slice(0, 5),
      detailsLoaded: true,
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
  const pendingHydrationRef = useRef(new Map<string, MetagameDeckItem>())
  const hydratingKeysRef = useRef(new Set<string>())
  const hydrationTimerRef = useRef<number | null>(null)
  const hydrationControllersRef = useRef(new Set<AbortController>())
  const activeCacheKeyRef = useRef(cacheKey)
  activeCacheKeyRef.current = cacheKey

  useEffect(() => {
    decksRef.current = decks
  }, [decks])

  useEffect(() => () => {
    if (hydrationTimerRef.current != null) {
      window.clearTimeout(hydrationTimerRef.current)
      hydrationTimerRef.current = null
    }
    pendingHydrationRef.current.clear()
    hydratingKeysRef.current.clear()
    hydrationControllersRef.current.forEach(controller => controller.abort())
    hydrationControllersRef.current.clear()
  }, [cacheKey])

  const hydrateDecks = useCallback((requestedDecks: MetagameDeckItem[]) => {
    requestedDecks.forEach(requestedDeck => {
      const key = getDeckKey(requestedDeck)
      const currentDeck = decksRef.current.find(deck => getDeckKey(deck) === key)
      if (!currentDeck || currentDeck.detailsLoaded || hydratingKeysRef.current.has(key)) return
      pendingHydrationRef.current.set(key, currentDeck)
    })

    if (pendingHydrationRef.current.size === 0 || hydrationTimerRef.current != null) return

    hydrationTimerRef.current = window.setTimeout(() => {
      hydrationTimerRef.current = null
      const batch = [...pendingHydrationRef.current.values()]
      pendingHydrationRef.current.clear()
      const batchKeys = batch.map(getDeckKey)
      batchKeys.forEach(key => hydratingKeysRef.current.add(key))

      const controller = new AbortController()
      hydrationControllersRef.current.add(controller)
      const requestedCacheKey = cacheKey
      const dateParams = new URLSearchParams()
      if (from) dateParams.set("min_date", from)
      if (to) dateParams.set("max_date", to)

      void hydrateDeckBatch(batch, dateParams, controller.signal)
        .then(hydratedDecks => {
          if (activeCacheKeyRef.current !== requestedCacheKey) return
          const hydratedByKey = new Map(
            hydratedDecks.map(deck => [getDeckKey(deck), deck]),
          )
          setDecks(currentDecks => {
            const nextDecks = currentDecks.map(deck =>
              hydratedByKey.get(getDeckKey(deck)) ?? deck,
            )
            decksRef.current = nextDecks
            cache.set(requestedCacheKey, { decks: nextDecks, timestamp: Date.now() })
            return nextDecks
          })
        })
        .catch(reason => {
          if (reason instanceof DOMException && reason.name === "AbortError") return
        })
        .finally(() => {
          batchKeys.forEach(key => hydratingKeysRef.current.delete(key))
          hydrationControllersRef.current.delete(controller)
        })
    }, HYDRATION_BATCH_DELAY_MS)
  }, [cacheKey, from, to])

  useEffect(() => {
    if (stableFormats.length === 0) {
      setDecks([])
      setLoading(false)
      setError(null)
      return
    }

    const currentCache = cache.get(cacheKey)
    if (currentCache && Date.now() - currentCache.timestamp < CACHE_TTL_MS) {
      setDecks(currentCache.decks)
      setLoading(false)
      setError(null)
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
        const rows = await fetchData<MetagameRow[]>(
          `/metagame/${encodeURIComponent(format.toLocaleLowerCase())}?${params}`,
          controller.signal,
        )
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
      const nextDecks = hydrateLazily
        ? summaries
        : await hydrateDeckBatch(summaries, dateParams, controller.signal)

      if (!active) return
      cache.set(cacheKey, { decks: nextDecks, timestamp: Date.now() })
      decksRef.current = nextDecks
      setDecks(nextDecks)
    })().catch(reason => {
      if (!active || (reason instanceof DOMException && reason.name === "AbortError")) return
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
