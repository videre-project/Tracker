/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useMemo } from "react"
import type { DateRange } from "react-day-picker"
import { useSessionQuery } from "./use-session-query"
import { dateRangeParams } from "../utils/api-config"
import type { MatchHistoryDTO, PaginatedMatchesDTO } from "@/types/api"
import type { DashboardStats, GameType, MatchHistoryItem, PerformanceTrendPoint } from "@videreproject/ui"

interface PaginatedMatches {
  items: MatchHistoryItem[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

function toMatchHistoryItem(match: MatchHistoryDTO): MatchHistoryItem {
  return {
    ...match,
    id: match.id ?? 0,
    eventId: match.eventId ?? 0,
    startTime: match.startTime ?? "",
    deckName: match.deckName ?? undefined,
    opponentCards: match.opponentCards?.map(card => ({ catalogId: card.catalogId ?? 0, name: card.name ?? "Unknown card", quantity: card.quantity })),
    matches: match.matches?.map(toMatchHistoryItem),
  }
}

function toPaginatedMatches(response: PaginatedMatchesDTO): PaginatedMatches {
  return {
    items: response.items?.map(toMatchHistoryItem) ?? [],
    totalCount: response.totalCount ?? 0,
    page: response.page ?? 1,
    pageSize: response.pageSize ?? 50,
    totalPages: response.totalPages ?? 0,
  }
}

export function useGameFormats(gameType: GameType, refreshKey?: number) {
  const query = useSessionQuery<string[]>(`/api/games/formats?${new URLSearchParams({ gameType })}`, { refreshKey })
  return { formats: query.data ?? [], loading: query.loading, error: query.error }
}

function gameDateParams(timeRange: string | DateRange | undefined) {
  if (typeof timeRange === "object" && timeRange?.from)
    return dateRangeParams({ from: timeRange.from, to: timeRange.to ?? timeRange.from })
  const period = typeof timeRange === "string" && Number.parseInt(timeRange) > 0 ? timeRange : "ALL"
  return dateRangeParams(period)
}

export function useGames(
  timeRange: string | DateRange | undefined, format?: string, gameType: GameType = "All", refreshKey?: number,
) {
  const params = gameDateParams(timeRange)
  if (format) params.set("format", format)
  params.set("gameType", gameType)
  const stats = useSessionQuery<DashboardStats>(`/api/games/dashboard-stats?${params}`, { cacheForSession: true, refreshKey })
  const trend = useSessionQuery<PerformanceTrendPoint[]>(`/api/games/performance-trend?${params}`, { cacheForSession: true, refreshKey })
  const ready = stats.data != null && trend.data != null
  return {
    stats: ready ? stats.data : null, trend: ready ? trend.data! : [],
    loading: stats.loading || trend.loading, error: stats.error ?? trend.error,
  }
}

export function useGamesHistory(
  page: number,
  pageSize: number,
  timeRange: string | DateRange | undefined,
  format?: string,
  deckRevisionId?: number,
  filters = ""
) {
  const params = new URLSearchParams(filters)
  gameDateParams(timeRange).forEach((value, key) => params.set(key, value))
  if (format) params.set("format", format)
  if (deckRevisionId) params.set("deckRevisionId", String(deckRevisionId))
  const dataset = params.toString()
  params.set("page", String(page))
  params.set("pageSize", String(pageSize))
  const query = useSessionQuery<PaginatedMatchesDTO>(`/api/games/history?${params}`, { retainDataKey: dataset })
  const data = useMemo(() => query.data ? toPaginatedMatches(query.data) : null, [query.data])
  return { data, loading: query.loading, error: query.error, refresh: query.refresh }
}
