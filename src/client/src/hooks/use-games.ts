import { useState, useEffect } from "react"
import { useClientState } from "./use-client-state"
import { getApiUrl } from "../utils/api-config"

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
        setLoading(false)
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

  }, [rangeKey, format, clientReady, clientLoading, cacheKey]) // Use rangeKey instead of object

  return { formats, stats, trend, loading }
}

