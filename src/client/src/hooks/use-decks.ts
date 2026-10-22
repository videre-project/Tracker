/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useState, useEffect } from "react"
import type { DateRange } from "react-day-picker"
import { useSessionQuery } from "./use-session-query"
import { dateRangeParams, getApiUrl } from "../utils/api-config"

import type { DeckCardEntry, GameType } from "@videreproject/ui"
import type {
  DeckDTO, DeckDetailDTO, AggregatedArchetypeDTO, DeckCardMutationRequest,
  DeckCardZone, DeckMutationResponse, ImportDeckRequest, ImportDeckResponse,
} from "@/types/api"

export type CardEntry = DeckCardEntry

export type DeckSummary = Omit<DeckDTO, "featuredCards"> & {
  featuredCards?: CardEntry[] | null
}

export type DeckDetail = Omit<DeckDetailDTO, "mainboard" | "sideboard"> & {
  mainboard: CardEntry[]
  sideboard: CardEntry[]
}

export type DeckCardMutation = DeckCardMutationRequest & {
  zone: DeckCardZone
}

async function readApiError(response: Response, fallback: string) {
  const payload = await response.json().catch(() => null) as { error?: string } | null
  return payload?.error || fallback
}

export async function addDeckCard(
  revisionId: string | number,
  card: DeckCardMutation,
) {
  const response = await fetch(getApiUrl(`/api/decks/${revisionId}/cards`), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ quantity: 1, annotation: 0, ...card }),
  })
  if (!response.ok) {
    throw new Error(await readApiError(response, `Failed to add card (HTTP ${response.status})`))
  }
  return await response.json() as DeckMutationResponse
}

export async function removeDeckCard(
  revisionId: string | number,
  card: DeckCardMutation,
) {
  const response = await fetch(getApiUrl(`/api/decks/${revisionId}/cards`), {
    method: "DELETE",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ quantity: 1, annotation: 0, ...card }),
  })
  if (!response.ok) {
    throw new Error(await readApiError(response, `Failed to remove card (HTTP ${response.status})`))
  }
  return await response.json() as DeckMutationResponse
}

export async function moveDeckCards(
  revisionId: string | number,
  fromZone: DeckCardZone,
  toZone: DeckCardZone,
  cards: Array<{ catalogId: number; quantity: number }>,
) {
  const response = await fetch(getApiUrl(`/api/decks/${revisionId}/cards/move`), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ fromZone, toZone, cards }),
  })
  if (!response.ok) {
    throw new Error(await readApiError(response, `Failed to move cards (HTTP ${response.status})`))
  }
  return await response.json() as DeckMutationResponse
}

export async function deleteDeck(revisionId: string | number) {
  const response = await fetch(getApiUrl(`/api/decks/${revisionId}`), {
    method: "DELETE",
  })
  if (!response.ok) {
    throw new Error(await readApiError(response, `Failed to delete deck (HTTP ${response.status})`))
  }
  return await response.json() as DeckMutationResponse
}

export async function importDeck(request: ImportDeckRequest) {
  const response = await fetch(getApiUrl("/api/decks/import"), {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(request),
  })
  if (!response.ok) {
    throw new Error(await readApiError(response, `Failed to import deck (HTTP ${response.status})`))
  }
  return await response.json() as ImportDeckResponse
}

export function useDecks() {
  const { data, loading, error } = useSessionQuery<Record<string, DeckSummary[]>>("/api/decks")
  return { decks: data ?? {}, loading, error }
}

export function useDeckDetail(revisionId: string | null) {
  const [result, setResult] = useState<{
    id: string; detail: DeckDetail | null; loading: boolean; error: string | null
  } | null>(null)
  useEffect(() => {
    if (!revisionId) return
    const controller = new AbortController()
    setResult({ id: revisionId, detail: null, loading: true, error: null })
    void fetch(getApiUrl(`/api/decks/${revisionId}`), { signal: controller.signal })
      .then(response => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return response.json() as Promise<DeckDetail>
      })
      .then(detail => {
        if (!controller.signal.aborted) setResult({ id: revisionId, detail, loading: false, error: null })
      })
      .catch(error => {
        if (!controller.signal.aborted) setResult({ id: revisionId, detail: null, loading: false,
          error: error instanceof Error ? error.message : "Could not load this deck." })
      })
    return () => controller.abort()
  }, [revisionId])
  const current = result?.id === revisionId ? result : null
  return { detail: current?.detail ?? null, loading: Boolean(revisionId) && (current?.loading ?? true), error: current?.error ?? null }
}

export function useAggregatedArchetypes(
  timeRange: string | DateRange | undefined, format?: string, gameType: GameType = "All", refreshKey?: number,
) {
  const range = typeof timeRange === "object"
    ? { from: timeRange.from, to: timeRange.to ?? timeRange.from } : timeRange ?? "ALL"
  const params = dateRangeParams(range)
  if (format) params.set("format", format)
  params.set("gameType", gameType)
  const { data, loading, error } = useSessionQuery<AggregatedArchetypeDTO[]>(
    `/api/decks/archetypes/aggregated?${params}`, { refreshKey },
  )
  return { archetypes: data ?? [], loading, error }
}

export async function updateDeckArchetype(
  revisionId: number | string,
  archetype: string
): Promise<{ revisionId: number; archetype: string | null }> {
  const res = await fetch(getApiUrl(`/api/decks/${revisionId}/archetype`), {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ archetype }),
  })
  if (!res.ok) throw new Error(`HTTP ${res.status}`)
  return res.json()
}
