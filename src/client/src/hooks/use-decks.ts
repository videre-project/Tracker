import { useState, useEffect, useCallback } from "react"
import { useClientState } from "./use-client-state"
import { getApiUrl } from "../utils/api-config"

export interface CardEntry {
  catalogId: number
  name: string
  quantity: number
}

export interface DeckSummary {
  hash: string
  id: number
  name: string
  format: string
  timestamp: string
  mainboardCount: number
  sideboardCount: number
  archetype?: string
  colors: string[]
}

export interface DeckDetail {
  hash: string
  id: number
  name: string
  format: string
  timestamp: string
  mainboard: CardEntry[]
  sideboard: CardEntry[]
}

export interface NBACResponse {
  meta?: {
    database: string
    backend: string
    exec_ms: number
    read_count: number
    model: string
  }
  data?: Record<string, number>
  explain?: {
    method: string
    top: number
    n: number
    archetypes: Record<string, Array<{ card: string; quantity: number; score: number }>>
  }
  error?: string
  message?: string
}

export function useDecks() {
  const [decks, setDecks] = useState<Record<string, DeckSummary[]>>({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Fetch all decks grouped by format
  useEffect(() => {
    setLoading(true)
    setError(null)

    fetch(getApiUrl("/api/decks"))
      .then(res => {
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        return res.json()
      })
      .then(data => setDecks(data))
      .catch(err => setError(err.message))
      .finally(() => setLoading(false))
  }, [])

  return { decks, loading, error }
}

export function useDeckDetail(hash: string | null) {
  const [detail, setDetail] = useState<DeckDetail | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!hash) {
      setDetail(null)
      return
    }

    setLoading(true)
    setError(null)

    fetch(getApiUrl(`/api/decks/${hash}`))
      .then(res => {
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        return res.json()
      })
      .then(data => setDetail(data))
      .catch(err => setError(err.message))
      .finally(() => setLoading(false))
  }, [hash])

  return { detail, loading, error }
}

export function useDeckArchetype(hash: string | null) {
  const [archetype, setArchetype] = useState<NBACResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchArchetype = useCallback(async () => {
    if (!hash) return

    setLoading(true)
    setError(null)

    try {
      const res = await fetch(getApiUrl(`/api/decks/${hash}/archetype`))
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      const data = await res.json()
      setArchetype(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error")
    } finally {
      setLoading(false)
    }
  }, [hash])

  return { archetype, loading, error, fetchArchetype }
}

export interface AggregatedArchetype {
  archetype: string
  colors: string[]
  matches: number
  wins: number
  losses: number
  winrate: number
  topCard: string
  topCardAvgScore: number
  topCardAvgQuantity: number
}

const ARCHETYPES_CACHE: Record<string, { data: AggregatedArchetype[]; timestamp: number }> = {}

import { DateRange } from "react-day-picker"

export function useAggregatedArchetypes(timeRange: string | DateRange | undefined, format?: string) {
  const [archetypes, setArchetypes] = useState<AggregatedArchetype[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

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
    if (ARCHETYPES_CACHE[cacheKey]) {
      setArchetypes(ARCHETYPES_CACHE[cacheKey].data)
      setLoading(false)
    }
  }, [cacheKey])

  useEffect(() => {
    // Wait for client state to be determined
    if (clientLoading) return
    // Only fetch if client is ready
    if (!clientReady) {
      setLoading(false)
      return
    }

    if (!ARCHETYPES_CACHE[cacheKey]) {
      setLoading(true)
    }
    setError(null)

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
        maxDate = timeRange.from
        maxDate.setHours(23, 59, 59, 999)
      }
    }

    if (minDate) params.append("minDate", minDate.toISOString())
    params.append("maxDate", maxDate.toISOString())

    const queryString = params.toString()

    fetch(getApiUrl(`/api/decks/archetypes/aggregated?${queryString}`))
      .then(res => {
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        return res.json()
      })
      .then(data => {
        setArchetypes(data)
        ARCHETYPES_CACHE[cacheKey] = {
          data,
          timestamp: Date.now()
        }
      })
      .catch(err => setError(err.message))
      .finally(() => setLoading(false))
  }, [rangeKey, format, clientReady, clientLoading, cacheKey])

  return { archetypes, loading, error }
}


export interface DeckIdentifier {
  hash: string
  id: number
  name: string
  format: string
}

export function useDeckIdentifiers() {
  const [identifiers, setIdentifiers] = useState<DeckIdentifier[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setLoading(true)
    setError(null)

    fetch(getApiUrl("/api/decks/identifiers"))
      .then(res => {
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        return res.json()
      })
      .then(data => setIdentifiers(data))
      .catch(err => setError(err.message))
      .finally(() => setLoading(false))
  }, [])

  return { identifiers, loading, error }
}
