/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useCallback, useEffect, useMemo, useRef, useState } from "react"

import { useClientState } from "@/hooks/use-client-state"
import { useSessionQuery } from "@/hooks/use-session-query"
import { useNDJSONStream } from "@/hooks/use-ndjson-stream"
import type { CardEntry, GameLogDTO, MatchDetailsDTO, OpponentArchetypeOption, OpponentArchetypeUpdate } from "@/types/api"
import { GAME_LOG_TYPE_ORDER, type OpponentArchetypeContext } from "@videreproject/ui"
import type { components } from "@/types/videre.g"
import { fetchVidere } from "@/lib/videre-api"
import { ACTIVE_FORMATS } from "@videreproject/constants"
import { getApiUrl } from "@/utils/api-config"

function logParts(log: GameLogDTO): unknown[] {
  if (["ZoneChange", "Reveal", "CardChange", "PlayerChange"].includes(log.gameLogType)) {
    try {
      const parts = JSON.parse(log.data ?? "")
      if (Array.isArray(parts) && parts.length) return parts
    } catch { /* Keep malformed historical payloads as opaque log entries. */ }
  }
  return [log.data]
}

function mergeLogs(recorded: GameLogDTO[], received: GameLogDTO[]) {
  // Historical IDs are synthetic for some types. Match event contents instead,
  // allowing repeated messages and changes batched together by persistence.
  const key = (log: GameLogDTO, part: unknown) => JSON.stringify([log.gameLogType, log.timestamp, part])
  const occurrences = new Map<string, number>()
  for (const log of recorded) for (const part of logParts(log)) {
    const id = key(log, part)
    occurrences.set(id, (occurrences.get(id) ?? 0) + 1)
  }
  const additions: GameLogDTO[] = []
  for (const log of received) {
    const parts = logParts(log)
    const missing = parts.filter(part => {
      const id = key(log, part), count = occurrences.get(id) ?? 0
      if (!count) return true
      occurrences.set(id, count - 1)
      return false
    })
    if (missing.length) additions.push(missing.length === parts.length
      ? log : { ...log, data: JSON.stringify(missing) })
  }
  return [...recorded, ...additions].sort((a, b) =>
    (a.timestamp ? Date.parse(a.timestamp) : 0) - (b.timestamp ? Date.parse(b.timestamp) : 0) ||
    (GAME_LOG_TYPE_ORDER[a.gameLogType] ?? 6) - (GAME_LOG_TYPE_ORDER[b.gameLogType] ?? 6))
}

export function useMatchDetails(matchId: number | null, { refreshKey }: { refreshKey?: unknown } = {}) {
  const { queryCache } = useClientState()
  const query = useSessionQuery<MatchDetailsDTO>(
    matchId ? `/api/games/match/${matchId}` : null, { refreshKey },
  )
  const [feed, setFeed] = useState<{
    session: Map<string, unknown>; matchId: number | null; logs: GameLogDTO[]; connected: boolean
  } | null>(null)
  const currentFeed = feed?.session === queryCache && feed.matchId === matchId ? feed : null
  const liveLogs = currentFeed?.logs
  const data = useMemo(() => query.data ? {
    ...query.data,
    games: query.data.games?.map(game => ({
      ...game,
      logs: mergeLogs(game.logs ?? [], liveLogs?.filter(log => log.gameId === game.id) ?? []),
    })),
  } : null, [query.data, liveLogs])
  const unknownGames = useRef(new Set<number>())
  const refresh = query.refresh
  useEffect(() => { unknownGames.current.clear() }, [query.data, matchId, queryCache])

  const onLog = useCallback((log: GameLogDTO) => {
    setFeed(current => ({
      session: queryCache, matchId, connected: true,
      logs: [...(current?.session === queryCache && current.matchId === matchId ? current.logs : []), log],
    }))
    if (!query.data?.games?.some(game => game.id === log.gameId) && log.gameId && !unknownGames.current.has(log.gameId)) {
      unknownGames.current.add(log.gameId)
      refresh()
    }
  }, [query.data, matchId, queryCache, refresh])

  useNDJSONStream<GameLogDTO>({
    url: matchId ? getApiUrl(`/api/games/match/${matchId}/watch`) : "",
    onMessage: onLog,
    onOpen: () => {
      setFeed(current => ({
        session: queryCache, matchId, connected: true,
        logs: current?.session === queryCache && current.matchId === matchId ? current.logs : [],
      }))
      // Read after subscribing too, so events between the first read and subscription are included.
      refresh()
    },
    onEnd: () => setFeed(current => current && { ...current, connected: false }),
    onError: () => setFeed(current => current && { ...current, connected: false }),
    enabled: query.isReady && matchId != null,
    restartKey: queryCache,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })

  return {
    data, liveLogs, connected: query.isReady && (currentFeed?.connected ?? false),
    loading: query.loading, error: query.error, refresh, liveUpdateCount: liveLogs?.length ?? 0,
  }
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
