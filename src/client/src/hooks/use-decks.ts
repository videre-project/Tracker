import { useState, useEffect, useCallback, useRef } from "react"
import { useClientState } from "./use-client-state"
import { getApiUrl } from "../utils/api-config"

export interface CardEntry {
  catalogId: number
  name: string
  quantity: number
}

export interface DeckSummary {
  revisionId: number
  netDeckId: number
  name: string
  format: string
  timestamp: string
  mainboardCount: number
  sideboardCount: number
  wins: number
  losses: number
  ties: number
  archetype?: string
  colors: string[]
  featuredCards?: CardEntry[]
}

export interface DeckDetail {
  revisionId: number
  netDeckId: number
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
  const { isReady: clientReady, loading: clientLoading } = useClientState()
  const [decks, setDecks] = useState<Record<string, DeckSummary[]>>({})
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (clientLoading) return
    if (!clientReady) {
      setDecks({})
      setLoading(false)
      setError(null)
      return
    }

    const controller = new AbortController()
    setLoading(true)
    setError(null)

    fetch(getApiUrl("/api/decks"), { signal: controller.signal })
      .then(res => {
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        return res.json()
      })
      .then(data => setDecks(data))
      .catch(reason => {
        if (reason instanceof Error && reason.name === "AbortError") return
        setError(reason instanceof Error ? reason.message : "Unknown error")
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })

    return () => controller.abort()
  }, [clientLoading, clientReady])

  return { decks, loading: loading || clientLoading, error }
}

export function useDeckDetail(revisionId: string | null) {
  const [detail, setDetail] = useState<DeckDetail | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!revisionId) {
      setDetail(null)
      setLoading(false)
      setError(null)
      return
    }

    const abortController = new AbortController()

    setDetail(null)
    setLoading(true)
    setError(null)

    fetch(getApiUrl(`/api/decks/${revisionId}`), { signal: abortController.signal })
      .then(res => {
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        return res.json()
      })
      .then(data => setDetail(data))
      .catch(err => {
        if (err instanceof Error && err.name === "AbortError") return
        setError(err instanceof Error ? err.message : "Unknown error")
      })
      .finally(() => {
        if (!abortController.signal.aborted) {
          setLoading(false)
        }
      })

    return () => abortController.abort()
  }, [revisionId])

  return { detail, loading, error }
}

export function useDeckArchetype(revisionId: string | null) {
  const [archetype, setArchetype] = useState<NBACResponse | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const loadingRef = useRef(false)
  const requestedRevisionRef = useRef<string | null>(null)

  useEffect(() => {
    setArchetype(null)
    setError(null)
    requestedRevisionRef.current = null
  }, [revisionId])

  const fetchArchetype = useCallback(async (force = false) => {
    if (!revisionId) return
    if (loadingRef.current) return
    if (!force && requestedRevisionRef.current === revisionId) return

    loadingRef.current = true
    requestedRevisionRef.current = revisionId
    setLoading(true)
    setError(null)

    try {
      const res = await fetch(getApiUrl(`/api/decks/${revisionId}/archetype`))
      if (!res.ok) throw new Error(`HTTP ${res.status}`)
      const data = await res.json()
      setArchetype(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error")
    } finally {
      loadingRef.current = false
      setLoading(false)
    }
  }, [revisionId])

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
  revisionId: number
  netDeckId: number
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
