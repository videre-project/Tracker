import { useCallback, useEffect, useRef, useState } from "react"
import { useClientState } from "./use-client-state"
import { useNDJSONStream } from "./use-ndjson-stream"
import { getApiUrl } from "../utils/api-config"
import type {
  TradeMarketplaceUpdateDTO,
  TradePartner,
  TradePost,
  TradePostsPageDTO,
  TradeSnapshotDTO,
} from "@/types/api"

export type { TradePartner, TradePost }
export type CurrentTrade = NonNullable<TradeSnapshotDTO["currentTrade"]>
export type TradeSnapshot = TradeSnapshotDTO
export type TradePostsPage = TradePostsPageDTO

export type TradePostFormatFilter = "all" | "message" | "offeredWantedList"

export interface TradePostFilters {
  format: TradePostFormatFilter
  user: string
  message: string
}

type TradeMarketplaceUpdate = TradeMarketplaceUpdateDTO

const MARKETPLACE_SILENT_REFRESH_MIN_INTERVAL_MS = 1000

export function useTrades() {
  const { isReady: clientReady, loading: clientLoading } = useClientState()
  const [data, setData] = useState<TradeSnapshot | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchTrades = useCallback(async () => {
    if (clientLoading) return

    if (!clientReady) {
      setData(null)
      setLoading(false)
      setError(null)
      return
    }

    setLoading(true)
    setError(null)

    try {
      const response = await fetch(getApiUrl("/api/trades"))
      if (!response.ok) {
        if (response.status === 503) {
          setData(null)
          return
        }
        throw new Error(`HTTP ${response.status}`)
      }

      setData(await response.json())
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error")
    } finally {
      setLoading(false)
    }
  }, [clientLoading, clientReady])

  useEffect(() => {
    fetchTrades()
  }, [fetchTrades])

  return {
    data,
    loading: loading || clientLoading,
    error,
    refresh: fetchTrades,
    clientReady
  }
}

export function useTradePosts(
  page: number,
  pageSize: number,
  filters: TradePostFilters = { format: "all", user: "", message: "" }
) {
  const { isReady: clientReady, loading: clientLoading } = useClientState()
  const [data, setData] = useState<TradePostsPage | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const silentRefreshInFlight = useRef(false)
  const pendingSilentRefresh = useRef(false)
  const postsAbortController = useRef<AbortController | null>(null)
  const requestSequence = useRef(0)
  const lastSilentRefreshAt = useRef(0)
  const silentRefreshTimer = useRef<number | null>(null)
  const scheduleSilentRefreshRef = useRef<(() => void) | null>(null)

  const clearQueuedSilentRefresh = useCallback(() => {
    pendingSilentRefresh.current = false
    if (silentRefreshTimer.current != null) {
      window.clearTimeout(silentRefreshTimer.current)
      silentRefreshTimer.current = null
    }
  }, [])

  const fetchPosts = useCallback(async (options: { silent?: boolean; force?: boolean } = {}) => {
    const silent = options.silent ?? false
    const force = options.force ?? false

    if (silent && (silentRefreshInFlight.current || postsAbortController.current)) {
      pendingSilentRefresh.current = true
      return
    }

    if (clientLoading) return

    if (!clientReady) {
      setData(null)
      if (!silent) {
        setLoading(false)
      }
      setError(null)
      return
    }

    if (!silent) {
      clearQueuedSilentRefresh()
      setLoading(true)
    } else {
      silentRefreshInFlight.current = true
    }
    setError(null)

    if (!silent) {
      postsAbortController.current?.abort()
    }
    const controller = new AbortController()
    postsAbortController.current = controller
    const requestId = ++requestSequence.current

    try {
      const params = new URLSearchParams({
        page: page.toString(),
        pageSize: pageSize.toString()
      })
      if (filters.format !== "all") {
        params.set("format", filters.format)
      }
      const user = filters.user.trim()
      if (user) {
        params.set("user", user)
      }
      const message = filters.message.trim()
      if (message) {
        params.set("message", message)
      }
      if (force) {
        params.set("force", "true")
      }

      const response = await fetch(getApiUrl(`/api/trades/posts?${params}`), {
        signal: controller.signal
      })
      if (!response.ok) {
        if (response.status === 503) {
          if (requestId === requestSequence.current) {
            setData(null)
          }
          return
        }
        throw new Error(`HTTP ${response.status}`)
      }

      const nextData = await response.json()
      if (requestId === requestSequence.current) {
        setData(nextData)
      }
    } catch (err) {
      if (err instanceof Error && err.name === "AbortError") {
        return
      }
      if (requestId === requestSequence.current) {
        setError(err instanceof Error ? err.message : "Unknown error")
      }
    } finally {
      if (postsAbortController.current === controller) {
        postsAbortController.current = null
      }
      if (!silent && requestId === requestSequence.current) {
        setLoading(false)
      }
      if (silent) {
        silentRefreshInFlight.current = false
      }

      if (pendingSilentRefresh.current && !postsAbortController.current) {
        pendingSilentRefresh.current = false
        scheduleSilentRefreshRef.current?.()
      }
    }
  }, [
    clientLoading,
    clientReady,
    page,
    pageSize,
    filters.format,
    filters.user,
    filters.message,
    clearQueuedSilentRefresh
  ])

  useEffect(() => {
    fetchPosts()
  }, [fetchPosts])

  useEffect(() => {
    clearQueuedSilentRefresh()
  }, [
    page,
    pageSize,
    filters.format,
    filters.user,
    filters.message,
    clearQueuedSilentRefresh
  ])

  useEffect(() => {
    return () => {
      requestSequence.current++
      postsAbortController.current?.abort()
      postsAbortController.current = null
      clearQueuedSilentRefresh()
    }
  }, [clearQueuedSilentRefresh])

  const scheduleSilentRefresh = useCallback(() => {
    const now = Date.now()
    const elapsed = now - lastSilentRefreshAt.current

    if (elapsed >= MARKETPLACE_SILENT_REFRESH_MIN_INTERVAL_MS && silentRefreshTimer.current == null) {
      lastSilentRefreshAt.current = now
      void fetchPosts({ silent: true })
      return
    }

    if (silentRefreshTimer.current != null) return

    silentRefreshTimer.current = window.setTimeout(() => {
      silentRefreshTimer.current = null
      lastSilentRefreshAt.current = Date.now()
      void fetchPosts({ silent: true })
    }, Math.max(0, MARKETPLACE_SILENT_REFRESH_MIN_INTERVAL_MS - elapsed))
  }, [fetchPosts])

  useEffect(() => {
    scheduleSilentRefreshRef.current = scheduleSilentRefresh
  }, [scheduleSilentRefresh])

  useNDJSONStream<TradeMarketplaceUpdate>({
    url: getApiUrl("/api/trades/watchmarketplace"),
    enabled: clientReady && !clientLoading,
    onMessage: scheduleSilentRefresh,
    onError: (err) => {
      console.warn("Marketplace stream error:", err)
    },
    autoReconnect: true,
    reconnectDelay: 3000,
    maxReconnectAttempts: 0,
    useConstantRetry: true
  })

  return {
    data,
    loading: loading || clientLoading,
    error,
    refresh: fetchPosts,
    clientReady
  }
}
