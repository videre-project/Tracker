/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useEffect, useMemo, useState } from "react"
import { getApiUrl } from "@/utils/api-config"
import { preloadCardImages } from "@/utils/card-image-cache"

export interface DeckHistoryChange {
  catalogId: number
  name: string
  quantityDelta: number
  zone: "Mainboard" | "Sideboard"
  cmc?: number
  colors?: string[]
  types?: string[]
  rarity?: string
}

export interface CardEntry {
  catalogId: number
  name: string
  quantity: number
  cmc?: number
  colors?: string[]
  types?: string[]
  rarity?: string
}

export interface DeckHistoryRevision {
  revisionId: number
  cardGroupingId: number
  observedAt: string
  timestamp: string
  name: string
  format: string
  mainboardCount: number
  sideboardCount: number
  colors: string[]
  archetype?: string | null
  featuredCard?: string | null
  mainboard: CardEntry[]
  sideboard: CardEntry[]
  changesFromPrevious: DeckHistoryChange[]
  changesFromLatest: DeckHistoryChange[]
}

export interface DeckHistoryData {
  currentRevisionId: number
  cardGroupingId: number
  name: string
  format: string
  revisions: DeckHistoryRevision[]
}

export function useDeckHistory(revisionId: string | number | null) {
  const [history, setHistory] = useState<DeckHistoryData | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [selectedRevisionId, setSelectedRevisionId] = useState<number | null>(null)

  useEffect(() => {
    if (!revisionId) {
      setHistory(null)
      setSelectedRevisionId(null)
      return
    }

    let cancelled = false
    setLoading(true)
    setError(null)

    fetch(getApiUrl(`/api/decks/${revisionId}/history`))
      .then(res => {
        if (!res.ok) throw new Error(`Failed to load deck history (${res.status})`)
        return res.json()
      })
      .then((data: DeckHistoryData) => {
        if (cancelled) return
        setHistory(data)
        setLoading(false)
      })
      .catch(err => {
        if (cancelled) return
        setError(err instanceof Error ? err.message : "Failed to load deck history")
        setLoading(false)
      })

    return () => {
      cancelled = true
    }
  }, [revisionId])

  useEffect(() => {
    if (!history?.revisions) return
    const ids = new Set<number>()
    for (const rev of history.revisions) {
      rev.mainboard?.forEach(c => ids.add(c.catalogId))
      rev.sideboard?.forEach(c => ids.add(c.catalogId))
      rev.changesFromPrevious?.forEach(c => ids.add(c.catalogId))
    }
    preloadCardImages(Array.from(ids))
  }, [history])

  const latestRevision = useMemo(() => {
    if (!history?.revisions.length) return null
    return history.revisions[0] // Revisions returned newest-first
  }, [history])

  const selectedRevision = useMemo(() => {
    if (!history?.revisions.length) return null
    if (selectedRevisionId != null) {
      return history.revisions.find(r => r.revisionId === selectedRevisionId) ?? history.revisions[0]
    }
    return history.revisions[0]
  }, [history, selectedRevisionId])

  // Compute card diff map relative to the previous revision (changes introduced in this revision)
  const diffMap = useMemo(() => {
    if (!selectedRevision || !selectedRevision.changesFromPrevious?.length) {
      return new Map<number, { delta: number; zone: string; name: string; cmc?: number; colors?: string[]; types?: string[]; rarity?: string }>()
    }

    const map = new Map<number, { delta: number; zone: string; name: string; cmc?: number; colors?: string[]; types?: string[]; rarity?: string }>()
    for (const change of selectedRevision.changesFromPrevious) {
      map.set(change.catalogId, {
        delta: change.quantityDelta,
        zone: change.zone,
        name: change.name,
        cmc: change.cmc,
        colors: change.colors,
        types: change.types,
        rarity: change.rarity,
      })
    }
    return map
  }, [selectedRevision])

  const selectedRevisionCards = useMemo(() => {
    if (!selectedRevision) return []
    const cards: Array<{
      index: number
      originalIndex: number
      catalogId: number
      name: string
      quantity: number
      cmc: number
      colors: string[]
      types: string[]
      rarity: string
      zone: 'Mainboard' | 'Sideboard'
    }> = []
    let index = 0

    selectedRevision.mainboard.forEach(c => {
      cards.push({
        index: index++,
        originalIndex: index,
        catalogId: c.catalogId,
        name: c.name,
        quantity: c.quantity,
        cmc: c.cmc ?? 0,
        colors: c.colors ?? [],
        types: c.types ?? [],
        rarity: c.rarity ?? "common",
        zone: "Mainboard",
      })
    })

    selectedRevision.sideboard.forEach(c => {
      cards.push({
        index: index++,
        originalIndex: index,
        catalogId: c.catalogId,
        name: c.name,
        quantity: c.quantity,
        cmc: c.cmc ?? 0,
        colors: c.colors ?? [],
        types: c.types ?? [],
        rarity: c.rarity ?? "common",
        zone: "Sideboard",
      })
    })

    return cards
  }, [selectedRevision])

  return {
    history,
    loading,
    error,
    selectedRevisionId,
    setSelectedRevisionId,
    selectedRevision,
    selectedRevisionCards,
    latestRevision,
    diffMap,
  }
}
