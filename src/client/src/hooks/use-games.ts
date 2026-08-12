/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useState, useEffect } from "react"
import { useClientState } from "./use-client-state"
import { getApiUrl } from "../utils/api-config"
import type { MatchHistoryDTO, PaginatedMatchesDTO } from "@/types/api"
import type { MatchHistoryItem } from "@videreproject/ui"

export interface DashboardStats {
  overallWinrate: number
  totalMatches: number
  wins: number
  losses: number
  ties: number
  playWinrate: number
  playMatches: number
  drawWinrate: number
  drawMatches: number
  averageDuration: string
  durationTwoGames: string
  durationThreeGames: string
}

export interface PerformanceTrend {
  date: string
  rawDate: string
  winrate: number | null
  matches: number
  rollingAvg: number | null
  ci95: number[] | null
  ci80: number[] | null
  ci50: number[] | null
}

interface PaginatedMatches {
  items: MatchHistoryItem[]
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
}

function toMatchHistoryItem(match: MatchHistoryDTO): MatchHistoryItem {
  return {
    id: match.id ?? 0,
    eventId: match.eventId ?? 0,
    eventName: match.eventName ?? "",
    format: match.format ?? "",
    startTime: match.startTime ?? "",
    result: match.result ?? "",
    record: match.record ?? "",
    duration: match.duration ?? "",
    deckName: match.deckName ?? undefined,
    deckColors: match.deckColors,
    opponentName: match.opponentName,
    opponentDeckName: match.opponentDeckName,
    opponentDeckArchetype: match.opponentDeckArchetype,
    opponentDeckColors: match.opponentDeckColors,
    isActive: match.isActive,
    isEvent: match.isEvent,
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

const GAMES_CACHE: Record<string, { stats: DashboardStats; trend: PerformanceTrend[]; timestamp: number }> = {}
let FORMATS_CACHE: string[] | null = null

import { DateRange } from "react-day-picker"

export function useGames(timeRange: string | DateRange | undefined, format?: string) {
  const [formats, setFormats] = useState<string[]>(FORMATS_CACHE || [])
  const [stats, setStats] = useState<DashboardStats | null>(null)
  const [trend, setTrend] = useState<PerformanceTrend[]>([])
  const [loading, setLoading] = useState(true)

  // Serialize cache key
  const rangeKey = typeof timeRange === 'string' 
    ? timeRange 
    : timeRange 
      ? `${timeRange.from?.toISOString()}-${timeRange.to?.toISOString()}`
      : 'all'
  
  const cacheKey = `${rangeKey}-${format || "all"}`

  // Wait for MTGO client to be ready before fetching
  const { isReady: clientReady, loading: clientLoading } = useClientState()

  // Initialize from cache
  useEffect(() => {
    if (GAMES_CACHE[cacheKey]) {
      setStats(GAMES_CACHE[cacheKey].stats)
      setTrend(GAMES_CACHE[cacheKey].trend)
      setLoading(false)
    }
  }, [cacheKey])

  // Fetch formats when client becomes ready
  useEffect(() => {
    // Wait for client state to be determined
    if (clientLoading) return
    // Only fetch if client is ready
    if (!clientReady) {
      setLoading(false)
      return
    }

    if (FORMATS_CACHE) return

    fetch(getApiUrl("/api/games/formats"))
      .then(res => res.json())
      .then(data => {
        setFormats(data)
        FORMATS_CACHE = data
      })
      .catch(err => console.error("Failed to fetch formats:", err))
  }, [clientReady, clientLoading])

  // Fetch stats and trend when filters change or client becomes ready
  useEffect(() => {
    // Wait for client state to be determined
    if (clientLoading) return
    // Only fetch if client is ready
    if (!clientReady) {
      setLoading(false)
      return
    }

    // If we have no cache, we MUST show loading
    if (!GAMES_CACHE[cacheKey]) {
      setLoading(true)
    }

    const params = new URLSearchParams()
    if (format) params.append("format", format)

    // Calculate date range
    const now = new Date()
    // Set maxDate to end of today to include all matches for today
    now.setHours(23, 59, 59, 999)
    let minDate: Date | null = null
    let maxDate: Date = now

    if (typeof timeRange === 'string' && timeRange !== "ALL") {
      const days = parseInt(timeRange.replace("D", ""))
      if (!isNaN(days)) {
        minDate = new Date()
        minDate.setDate(now.getDate() - days)
        minDate.setHours(0, 0, 0, 0)
      }
    } else if (typeof timeRange === 'object' && timeRange?.from) {
      minDate = timeRange.from
      minDate.setHours(0, 0, 0, 0)
      
      if (timeRange.to) {
        maxDate = timeRange.to
        maxDate.setHours(23, 59, 59, 999)
      } else {
        // If only from date is selected, maybe default to just that day? 
        // Or from that day until now? usually range picker 'to' is undefined when selecting first date.
        // Let's assume if 'to' is missing, it's a single day or range in progress.
        // Shadcn date picker handles this.
        maxDate = timeRange.from
        maxDate.setHours(23, 59, 59, 999)
      }
    }

    if (minDate) params.append("minDate", minDate.toISOString())
    params.append("maxDate", maxDate.toISOString())

    const queryString = params.toString()

    Promise.all([
      fetch(getApiUrl(`/api/games/dashboard-stats?${queryString}`)).then(res => res.json()),
      fetch(getApiUrl(`/api/games/performance-trend?${queryString}`)).then(res => res.json())
    ])
      .then(([statsData, trendData]) => {
        setStats(statsData)
        setTrend(trendData)
        GAMES_CACHE[cacheKey] = {
          stats: statsData,
          trend: trendData,
          timestamp: Date.now()
        }
      })
      .catch(err => console.error("Failed to fetch game data:", err))
      // .finally(() => setLoading(false)) // Moved to then block to avoid flickering if cache hit happens immediately after? 
      // actually finally is safer. 
      .finally(() => setLoading(false))

  }, [rangeKey, timeRange, format, clientReady, clientLoading, cacheKey])

  return { formats, stats, trend, loading }
}

export function useGamesHistory(
  page: number,
  pageSize: number,
  timeRange: string | DateRange | undefined,
  format?: string,
  deckRevisionId?: number
) {
  const [data, setData] = useState<PaginatedMatches | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const { isReady: clientReady, loading: clientLoading } = useClientState()

  useEffect(() => {
    if (clientLoading) return
    if (!clientReady) {
      setLoading(false)
      return
    }

    setLoading(true)
    setError(null)

    const params = new URLSearchParams()
    params.append("page", page.toString())
    params.append("pageSize", pageSize.toString())
    if (format) params.append("format", format)
    if (deckRevisionId) params.append("deckRevisionId", deckRevisionId.toString())

    const now = new Date()
    now.setHours(23, 59, 59, 999)
    let minDate: Date | null = null
    let maxDate: Date = now

    if (typeof timeRange === 'string' && timeRange !== "ALL") {
      const days = parseInt(timeRange.replace("D", ""))
      if (!isNaN(days)) {
        minDate = new Date()
        minDate.setDate(now.getDate() - days)
        minDate.setHours(0, 0, 0, 0)
      }
    } else if (typeof timeRange === 'object' && timeRange?.from) {
      minDate = timeRange.from
      minDate.setHours(0, 0, 0, 0)
      if (timeRange.to) {
        maxDate = timeRange.to
        maxDate.setHours(23, 59, 59, 999)
      } else {
        maxDate = timeRange.from
        maxDate.setHours(23, 59, 59, 999)
      }
    }

    if (minDate) params.append("minDate", minDate.toISOString())
    params.append("maxDate", maxDate.toISOString())

    fetch(getApiUrl(`/api/games/history?${params.toString()}`))
      .then(res => {
        if (!res.ok) throw new Error(`HTTP error! status: ${res.status}`)
        return res.json()
      })
      .then((json: PaginatedMatchesDTO) => {
        setData(toPaginatedMatches(json))
        setLoading(false)
      })
      .catch(err => {
        console.error("Failed to fetch game history:", err)
        setError(err instanceof Error ? err.message : "Unknown error")
        setLoading(false)
      })
  }, [page, pageSize, timeRange, format, deckRevisionId, clientReady, clientLoading])

  return { data, loading, error }
}
