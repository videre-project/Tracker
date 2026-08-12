/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useEffect, useState } from "react"
import type {
  CollectionCardEntry,
  CollectionPriceHistoryPoint,
  CollectionSelection,
} from "@videreproject/ui"

import { getApiUrl } from "@/utils/api-config"

type CollectionCardDetail = Omit<CollectionCardEntry, "quantity" | "prices"> & {
  canonicalName?: string
  colors?: string[]
  imageUrl?: string
}

type CollectionPriceHistorySnapshot = {
  catalogId: number
  priceCacheExpiresAt: string
  prices: CollectionPriceHistoryPoint[]
}

const cardDetailCache = new Map<number, CollectionCardDetail>()
const priceHistoryCache = new Map<number, CollectionPriceHistorySnapshot>()

function normalizeText(text?: string | null) {
  return text
    ?.replace(/\r\n/g, "\n")
    .replace(/\\n/g, "\n")
    .replace(/\r/g, "\n")
    .replace(/(^|\n)\*\*\s*/g, "$1• ")
    .trim() ?? text
}

function isHistoryFresh(snapshot?: CollectionPriceHistorySnapshot) {
  if (!snapshot) return false
  const expiresAt = Date.parse(snapshot.priceCacheExpiresAt)
  return Number.isFinite(expiresAt) && Date.now() < expiresAt
}

export function useCollectionSelection(selection: CollectionSelection | null) {
  const [resolvedSelection, setResolvedSelection] = useState<CollectionSelection | null>(selection)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!selection) {
      setResolvedSelection(null)
      setLoading(false)
      setError(null)
      return
    }

    const { item, viewMode } = selection
    const cachedDetail = viewMode === "cards" ? cardDetailCache.get(item.catalogId) : null
    const cachedHistory = priceHistoryCache.get(item.catalogId)
    const historyIsFresh = isHistoryFresh(cachedHistory)
    const mergeSelection = (
      detail: CollectionCardDetail | null | undefined,
      history: CollectionPriceHistorySnapshot | null | undefined,
    ): CollectionSelection => ({
      viewMode,
      item: {
        ...item,
        ...(detail ?? {}),
        oracleText: normalizeText(detail?.oracleText),
        flavorText: normalizeText(detail?.flavorText),
        prices: history?.prices ?? [],
      },
    })

    setResolvedSelection(mergeSelection(cachedDetail, historyIsFresh ? cachedHistory : null))
    if ((viewMode !== "cards" || cachedDetail) && historyIsFresh) {
      setLoading(false)
      setError(null)
      return
    }

    const controller = new AbortController()
    setLoading(true)
    setError(null)

    const to = new Date()
    const from = new Date(to)
    from.setUTCDate(from.getUTCDate() - 365)
    const historyParams = new URLSearchParams({
      from: from.toISOString().slice(0, 10),
      to: to.toISOString().slice(0, 10),
      limit: "365",
    })

    const detailRequest = viewMode === "cards" && !cachedDetail
      ? fetch(getApiUrl(`/api/collection/cards/${item.catalogId}/details`), { signal: controller.signal })
          .then(async response => {
            if (!response.ok) throw new Error(`Card details: HTTP ${response.status}`)
            const detail = await response.json() as CollectionCardDetail
            cardDetailCache.set(item.catalogId, detail)
            return detail
          })
      : Promise.resolve(cachedDetail)

    const historyRequest = !historyIsFresh
      ? fetch(getApiUrl(`/api/collection/prices/${item.catalogId}/history?${historyParams.toString()}`), { signal: controller.signal })
          .then(async response => {
            if (!response.ok) throw new Error(`Price history: HTTP ${response.status}`)
            const history = await response.json() as CollectionPriceHistorySnapshot
            priceHistoryCache.set(item.catalogId, history)
            return history
          })
      : Promise.resolve(cachedHistory)

    Promise.all([detailRequest, historyRequest])
      .then(([detail, history]) => setResolvedSelection(mergeSelection(detail, history)))
      .catch(requestError => {
        if (requestError instanceof Error && requestError.name === "AbortError") return
        setError(requestError instanceof Error ? requestError.message : String(requestError))
      })
      .finally(() => {
        if (!controller.signal.aborted) setLoading(false)
      })

    return () => controller.abort()
  }, [selection])

  return { selection: resolvedSelection, loading, error }
}
