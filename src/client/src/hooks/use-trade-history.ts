/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useCallback, useEffect, useState } from "react"
import type {
  TradeAttributionStatus,
  TradeEscrowItemRole,
  TradeEscrowKind,
  TradeEscrowResult,
  TradeHistoryDetail,
  TradeHistoryEffect,
  TradeHistoryError,
  TradeHistoryItem,
  TradeHistoryMessage,
  TradeHistorySummary,
} from "@videreproject/ui"

import { getApiUrl } from "@/utils/api-config"

export type {
  TradeAttributionStatus,
  TradeEscrowItemRole,
  TradeEscrowKind,
  TradeEscrowResult,
  TradeHistoryDetail,
  TradeHistoryEffect,
  TradeHistoryError,
  TradeHistoryItem,
  TradeHistoryMessage,
  TradeHistorySummary,
}

export interface TradeHistoryPage {
  items: TradeHistorySummary[]
  nextBeforeId?: number
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
