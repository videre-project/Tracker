/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useEffect, useState } from "react"

import { useClientState } from "@/hooks/use-client-state"
import type { CardEntry, MatchDetailsDTO, OpponentArchetypeOption, OpponentArchetypeUpdate } from "@/types/api"
import type { OpponentArchetypeContext } from "@videreproject/ui"
import type { components } from "@/types/videre.g"
import { fetchVidere } from "@/lib/videre-api"
import { ACTIVE_FORMATS } from "@videreproject/constants"
import { getApiUrl } from "@/utils/api-config"

export function useMatchDetails(matchId: number | null, initialMatch?: MatchDetailsDTO) {
  const snapshot = initialMatch?.id === matchId ? initialMatch : null
  const [data, setData] = useState<MatchDetailsDTO | null>(snapshot)
  const [loading, setLoading] = useState(!snapshot)
  const [error, setError] = useState<Error | null>(null)
  const { isReady: clientReady, loading: clientLoading } = useClientState()

  useEffect(() => {
    if (clientLoading || !matchId) return
    if (!clientReady) {
      setLoading(false)
      return
    }

    const controller = new AbortController()
    // Reuse the summary's complete match while refreshing it in the background.
    setData(snapshot)
    setLoading(!snapshot)
    setError(null)
    fetch(getApiUrl(`/api/games/match/${matchId}`), { signal: controller.signal })
      .then(response => {
        if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`)
        return response.json() as Promise<MatchDetailsDTO>
      })
      .then(responseData => {
        if (!controller.signal.aborted) setData(responseData)
      })
      .catch(requestError => {
        if (controller.signal.aborted) return
        console.error("Failed to fetch match details:", requestError)
        setError(requestError instanceof Error ? requestError : new Error(String(requestError)))
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })
    return () => controller.abort()
  }, [clientLoading, clientReady, matchId, snapshot])

  const current = data?.id === matchId ? data : snapshot
  return { data: current, loading: loading && !current, error }
}

export async function updateOpponentArchetypes(changes: OpponentArchetypeUpdate[]): Promise<OpponentArchetypeUpdate[]> {
  const response = await fetch(getApiUrl("/api/games/opponent-archetypes"), {
    method: "PUT",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(changes),
  })
  if (!response.ok) {
    const error = await response.json().catch(() => null)
    throw new Error(typeof error === "string" ? error : error?.error ?? error?.title ?? `Could not update classifications (${response.status}).`)
  }
  return response.json()
}

const publicArchetypes = new Map<string, components["schemas"]["Metagame"][]>()

export async function loadOpponentArchetypeContext(matchIds: number[], formats: string[], signal: AbortSignal): Promise<OpponentArchetypeContext> {
  const scope = [...new Set(formats.filter(Boolean))]
  const params = new URLSearchParams(scope.length === 1 ? { format: scope[0] } : {})
  const read = async <T,>(path: string): Promise<T> => {
    const response = await fetch(getApiUrl(path), { signal })
    if (!response.ok) throw new Error(`Could not load opponent context (${response.status}).`)
    return response.json()
  }
  const cardParams = new URLSearchParams(matchIds.map(id => ["matchIds", String(id)]))
  const [recorded, cards, ...published] = await Promise.allSettled([
    read<OpponentArchetypeOption[]>(`/api/games/opponent-archetypes?${params}`),
    read<CardEntry[]>(`/api/games/opponent-cards?${cardParams}`),
    ...scope.filter(format => ACTIVE_FORMATS.some(publicFormat => publicFormat === format)).map(async format => {
      const cached = publicArchetypes.get(format)
      if (cached) return cached
      const response = await fetchVidere<components["schemas"]["Metagame"][]>(`/metagame/${encodeURIComponent(format.toLowerCase())}?limit=500`, AbortSignal.any([signal, AbortSignal.timeout(8000)]))
      publicArchetypes.set(format, response.data)
      return response.data
    }),
  ])
  if (signal.aborted) throw new DOMException("Aborted", "AbortError")
  const archetypes = new Map<string, OpponentArchetypeContext["archetypes"][number]>()
  for (const result of published) if (result.status === "fulfilled")
    for (const item of result.value) archetypes.set(item.archetype.toLocaleLowerCase(), { name: item.archetype })
  if (recorded.status === "fulfilled") for (const item of recorded.value) if (item.name)
    archetypes.set(item.name.toLocaleLowerCase(), { name: item.name, colors: item.colors })
  if (!archetypes.size && recorded.status === "rejected") throw recorded.reason
  return { archetypes: [...archetypes.values()].sort((a, b) => a.name.localeCompare(b.name)),
    cards: cards.status === "fulfilled" ? cards.value.map(card => ({ catalogId: card.catalogId ?? 0, name: card.name ?? "Unknown card" })) : [] }
}
