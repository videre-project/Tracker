/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useEffect, useState } from "react"
import type { CollectionSelection, CollectionSelectionKey } from "@videreproject/ui"
import type { CollectionCardDetailDTO, CollectionPriceHistoryDTO, CollectionSnapshotDTO } from "@/types/api"
import { getApiUrl } from "@/utils/api-config"

const cardDetailCache = new Map<number, CollectionCardDetailDTO>()
const priceHistoryCache = new Map<number, CollectionPriceHistoryDTO>()

function normalizeText(text?: string | null) {
  return text
    ?.replace(/\r\n/g, "\n")
    .replace(/\\n/g, "\n")
    .replace(/\r/g, "\n")
    .replace(/(^|\n)\*\*\s*/g, "$1• ")
    .trim() ?? text
}

function isHistoryFresh(snapshot?: CollectionPriceHistoryDTO): snapshot is CollectionPriceHistoryDTO {
  if (!snapshot) return false
  const expiresAt = Date.parse(snapshot.priceCacheExpiresAt)
  return Number.isFinite(expiresAt) && Date.now() < expiresAt
}

export function useCollectionSelection(selection: CollectionSelectionKey | null, snapshot: CollectionSnapshotDTO | null) {
  const catalogId = selection?.catalogId
  const viewMode = selection?.viewMode
  const items = viewMode === "products" ? snapshot?.products : snapshot?.cards
  const item = items?.find(item => item.catalogId === catalogId)
  const [result, setResult] = useState<{
    catalogId: number
    viewMode: CollectionSelectionKey["viewMode"]
    detail: CollectionCardDetailDTO | null
    history: CollectionPriceHistoryDTO | null
    loading: boolean
    error: string | null
  } | null>(null)

  useEffect(() => {
    if (catalogId == null || !viewMode) {
      setResult(null)
      return
    }

    const cachedDetail = viewMode === "cards" ? cardDetailCache.get(catalogId) ?? null : null
    const cachedHistory = priceHistoryCache.get(catalogId)
    const history = isHistoryFresh(cachedHistory) ? cachedHistory : null
    const loading = (viewMode === "cards" && !cachedDetail) || !history
    setResult({ catalogId, viewMode, detail: cachedDetail, history, loading, error: null })
    if (!loading) return

    const controller = new AbortController()
    const to = new Date()
    const from = new Date(to)
    from.setUTCDate(from.getUTCDate() - 365)
    const historyParams = new URLSearchParams({
      from: from.toISOString().slice(0, 10),
      to: to.toISOString().slice(0, 10),
      limit: "365",
    })
    const detailRequest = viewMode === "cards" && !cachedDetail
      ? fetch(getApiUrl(`/api/collection/cards/${catalogId}/details`), { signal: controller.signal })
          .then(async response => {
            if (!response.ok) throw new Error(`Card details: HTTP ${response.status}`)
            const detail = await response.json() as CollectionCardDetailDTO
            cardDetailCache.set(catalogId, detail)
            return detail
          })
      : Promise.resolve(cachedDetail)
    const historyRequest = !history
      ? fetch(getApiUrl(`/api/collection/prices/${catalogId}/history?${historyParams.toString()}`), { signal: controller.signal })
          .then(async response => {
            if (!response.ok) throw new Error(`Price history: HTTP ${response.status}`)
            const history = await response.json() as CollectionPriceHistoryDTO
            priceHistoryCache.set(catalogId, history)
            return history
          })
      : Promise.resolve(history)

    Promise.all([detailRequest, historyRequest])
      .then(([detail, history]) => {
        if (!controller.signal.aborted)
          setResult({ catalogId, viewMode, detail, history, loading: false, error: null })
      })
      .catch(requestError => {
        if (controller.signal.aborted) return
        setResult(current => current && {
          ...current,
          loading: false,
          error: requestError instanceof Error ? requestError.message : String(requestError),
        })
      })
    return () => controller.abort()
  }, [catalogId, viewMode, item?.priceDate])

  const current = result?.catalogId === catalogId && result?.viewMode === viewMode ? result : null
  const detail = current?.detail ?? (viewMode === "cards" && catalogId != null ? cardDetailCache.get(catalogId) : null)
  const cachedHistory = catalogId == null ? undefined : priceHistoryCache.get(catalogId)
  const history = current?.history ?? (isHistoryFresh(cachedHistory) ? cachedHistory : null)
  // Wish-list cards may have catalogue metadata without any owned copies.
  const selectedItem = snapshot ? item ?? (detail ? { ...detail, quantity: 0 } : null) : null
  const resolvedSelection: CollectionSelection | null = selection && selectedItem ? {
    viewMode: selection.viewMode,
    item: {
      ...selectedItem,
      ...(detail ?? {}),
      oracleText: normalizeText(detail?.oracleText),
      flavorText: normalizeText(detail?.flavorText),
      prices: history?.prices ?? [],
    },
  } : null

  return { selection: resolvedSelection, loading: selection != null && (current?.loading ?? true), error: current?.error ?? null }
}
