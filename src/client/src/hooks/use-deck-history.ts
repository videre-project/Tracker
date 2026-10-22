/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useCallback, useEffect, useMemo, useState } from "react"
import { toEditorCards } from "@/utils/deck-list"
import { useSessionQuery } from "./use-session-query"
import {
  useCardMedia, type DeckDiffEntry, type DeckDiffMap, type DeckZone, type DeckCardEntry,
  type DeckHistoryChange as EditorHistoryChange,
} from "@videreproject/ui"
import type { DeckHistoryChangeView, DeckHistoryRevisionView, DeckHistoryView } from "@/types/api"

// Refine generated responses with the snapshot fields required by the editor.
export type DeckHistoryChange = DeckHistoryChangeView & EditorHistoryChange & { zone: DeckZone }

export type DeckHistoryRevision = Omit<DeckHistoryRevisionView,
  "mainboard" | "sideboard" | "changesFromPrevious" | "changesFromLatest"> & {
  revisionId: number
  mainboard: DeckCardEntry[]
  sideboard: DeckCardEntry[]
  changesFromPrevious: DeckHistoryChange[]
  changesFromLatest: DeckHistoryChange[]
}

export type DeckHistoryData = Omit<DeckHistoryView, "revisions"> & { revisions: DeckHistoryRevision[] }

export function useDeckHistory(revisionId: string | number | null) {
  const { preloadCardImages } = useCardMedia()
  const path = revisionId == null ? null : `/api/decks/${revisionId}/history`
  const { data: history, loading, error } = useSessionQuery<DeckHistoryData>(path)
  const [selection, setSelection] = useState<{
    history: DeckHistoryData
    revisionId: number
  } | null>(null)
  // Reconnecting can replace this deck's history without changing the route.
  const selectedRevisionId = selection?.history === history ? selection.revisionId : null
  const setSelectedRevisionId = useCallback((id: number | null) => {
    setSelection(history && id != null ? { history, revisionId: id } : null)
  }, [history])

  useEffect(() => {
    if (!history?.revisions) return
    const ids = new Set<number>()
    for (const rev of history.revisions) {
      rev.mainboard?.forEach(c => ids.add(c.catalogId))
      rev.sideboard?.forEach(c => ids.add(c.catalogId))
      rev.changesFromPrevious?.forEach(c => ids.add(c.catalogId))
    }
    preloadCardImages(Array.from(ids))
  }, [history, preloadCardImages])

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
  const diffMap = useMemo<DeckDiffMap>(() => {
    const zones = new Map<DeckZone, Map<number, DeckDiffEntry>>()
    for (const change of selectedRevision?.changesFromPrevious ?? []) {
      let cards = zones.get(change.zone)
      if (!cards) {
        cards = new Map()
        zones.set(change.zone, cards)
      }
      cards.set(change.catalogId, {
        delta: change.quantityDelta,
        name: change.name ?? "Unknown card",
        cmc: change.cmc,
        colors: change.colors ?? undefined,
        types: change.types ?? undefined,
        rarity: change.rarity ?? undefined,
      })
    }
    return zones
  }, [selectedRevision])

  const selectedRevisionCards = useMemo(() => toEditorCards(selectedRevision), [selectedRevision])

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
