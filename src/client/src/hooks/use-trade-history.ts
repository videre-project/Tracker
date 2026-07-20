import { useCallback, useEffect, useState } from "react"

import { getApiUrl } from "@/utils/api-config"

export type TradeEscrowKind = "Player" | "NonPlayer"
export type TradeEscrowResult =
  | "InProgress"
  | "Completed"
  | "Cancelled"
  | "Failed"
  | "ClosedUnknown"
  | "Interrupted"
export type TradeAttributionStatus =
  | "NotApplicable"
  | "Pending"
  | "Inferred"
  | "InferredAmbiguous"
  | "Unavailable"
export type TradeEscrowItemRole =
  | "LocalOffer"
  | "RemoteOffer"
  | "InferredOutput"

export interface TradeHistorySummary {
  id: number
  escrowId?: number
  kind: TradeEscrowKind
  partnerId?: number
  partnerName?: string
  startedAt: string
  closedAt?: string
  state: number
  stateName?: string
  result: TradeEscrowResult
  attributionStatus: TradeAttributionStatus
  outgoingQuantity: number
  outgoingCatalogCount: number
  incomingQuantity: number
  incomingCatalogCount: number
}

export interface TradeHistoryPage {
  items: TradeHistorySummary[]
  nextBeforeId?: number
}

export interface TradeHistoryItem {
  role: TradeEscrowItemRole
  catalogId: number
  quantity: number
}

export interface TradeHistoryEffect {
  catalogId: number
  quantity: number
  isInferred: boolean
}

export interface TradeHistoryMessage {
  id: number
  timestamp: string
  senderId?: number
  senderName?: string
  text: string
}

export interface TradeHistoryError {
  id: number
  observedAt: string
  errorCode: number
  errorName?: string
}

export interface TradeHistoryDetail {
  summary: TradeHistorySummary
  token: string
  accountId: number
  items: TradeHistoryItem[]
  effects: TradeHistoryEffect[]
  messages: TradeHistoryMessage[]
  errors: TradeHistoryError[]
}
export interface TradeHistoryFilters {
  search?: string
  kind?: TradeEscrowKind
  result?: TradeEscrowResult
}

export function useTradeHistory(filters: TradeHistoryFilters = {}, limit = 50) {
  const search = filters.search?.trim() ?? ""
  const kind = filters.kind
  const result = filters.result
  const [items, setItems] = useState<TradeHistorySummary[]>([])
  const [nextBeforeId, setNextBeforeId] = useState<number | null>(null)
  const [loading, setLoading] = useState(true)
  const [loadingMore, setLoadingMore] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const fetchPage = useCallback(async (beforeId?: number) => {
    if (beforeId) setLoadingMore(true)
    else setLoading(true)
    setError(null)
    try {
      const params = new URLSearchParams({ limit: String(limit) })
      if (beforeId) params.set("beforeId", String(beforeId))
      if (search) params.set("search", search)
      if (kind) params.set("kind", kind)
      if (result) params.set("result", result)
      const response = await fetch(getApiUrl(`/api/trades/history?${params}`))
      if (!response.ok) throw new Error(`HTTP ${response.status}`)
      const page = await response.json() as TradeHistoryPage
      setItems(current => beforeId ? [...current, ...page.items] : page.items)
      setNextBeforeId(page.nextBeforeId ?? null)
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "Unknown error")
    } finally {
      setLoading(false)
      setLoadingMore(false)
    }
  }, [kind, limit, result, search])

  useEffect(() => {
    void fetchPage()
  }, [fetchPage])

  return {
    items,
    loading,
    loadingMore,
    error,
    hasMore: nextBeforeId != null,
    refresh: () => fetchPage(),
    loadMore: () => nextBeforeId == null
      ? Promise.resolve()
      : fetchPage(nextBeforeId),
  }
}

export function useTradeHistoryDetail(id: number | null) {
  const [data, setData] = useState<TradeHistoryDetail | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (id == null) {
      setData(null)
      setError(null)
      return
    }

    const controller = new AbortController()
    setLoading(true)
    setError(null)
    fetch(getApiUrl(`/api/trades/history/${id}`), { signal: controller.signal })
      .then(response => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        return response.json() as Promise<TradeHistoryDetail>
      })
      .then(setData)
      .catch(reason => {
        if (reason instanceof Error && reason.name === "AbortError") return
        setError(reason instanceof Error ? reason.message : "Unknown error")
      })
      .finally(() => setLoading(false))

    return () => controller.abort()
  }, [id])

  return { data, loading, error }
}
