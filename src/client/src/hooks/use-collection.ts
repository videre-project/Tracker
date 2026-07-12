import { useState, useEffect, useCallback, useRef } from "react"
import { getApiUrl } from "../utils/api-config"

export interface CollectionCardEntry {
  catalogId: number
  name: string
  quantity: number
  price?: number | null
  priceDate?: string | null
  priceSource?: string | null
}

export interface CollectionProductEntry {
  catalogId: number
  name: string
  quantity: number
  description?: string | null
  setCode?: string | null
  setName?: string | null
  objectType?: string | null
  imageUrl?: string | null
  isTradable?: boolean | null
  price?: number | null
  priceDate?: string | null
  priceSource?: string | null
}

export interface CollectionSnapshot {
  hash: string
  itemCount: number
  uniqueCount: number
  totalQuantity: number
  timestamp: string
  priceCacheExpiresAt: string
  elapsedMilliseconds: number
  cards: CollectionCardEntry[]
  products: CollectionProductEntry[]
}

let cachedCollectionSnapshot: CollectionSnapshot | null = null

function isCollectionSnapshotCacheFresh(snapshot: CollectionSnapshot | null) {
  if (!snapshot) return false
  const expiresAt = Date.parse(snapshot.priceCacheExpiresAt)
  return Number.isFinite(expiresAt) && Date.now() < expiresAt
}

export function useCollectionCards() {
  const initialSnapshot = isCollectionSnapshotCacheFresh(cachedCollectionSnapshot)
    ? cachedCollectionSnapshot
    : null
  const [snapshot, setSnapshot] = useState<CollectionSnapshot | null>(initialSnapshot)
  const [loading, setLoading] = useState(!initialSnapshot)
  const [error, setError] = useState<string | null>(null)
  const abortControllerRef = useRef<AbortController | null>(null)

  const refresh = useCallback(async (options?: { force?: boolean }) => {
    if (!options?.force && isCollectionSnapshotCacheFresh(cachedCollectionSnapshot)) {
      setSnapshot(cachedCollectionSnapshot)
      setLoading(false)
      setError(null)
      return
    }

    abortControllerRef.current?.abort()
    const abortController = new AbortController()
    abortControllerRef.current = abortController

    setLoading(true)
    setError(null)

    try {
      const response = await fetch(getApiUrl("/api/collection/cards"), {
        signal: abortController.signal,
      })

      if (!response.ok) {
        let message = `HTTP ${response.status}`
        try {
          const data = await response.json()
          if (data.error) {
            message = data.hint ? `${data.error} - ${data.hint}` : data.error
          }
        } catch {
          if (response.status === 503) {
            message = "MTGO client or collection is not ready yet."
          }
        }

        throw new Error(message)
      }

      const data = await response.json() as CollectionSnapshot
      cachedCollectionSnapshot = data
      setSnapshot(data)
    } catch (err) {
      if (err instanceof Error && err.name === "AbortError") return
      setError(err instanceof Error ? err.message : "Unknown error")
      if (!cachedCollectionSnapshot) {
        setSnapshot(null)
      }
    } finally {
      if (!abortController.signal.aborted) {
        setLoading(false)
      }
    }
  }, [])

  useEffect(() => {
    void refresh()

    return () => {
      abortControllerRef.current?.abort()
    }
  }, [refresh])

  return {
    snapshot,
    cards: snapshot?.cards ?? [],
    products: snapshot?.products ?? [],
    loading,
    error,
    refresh,
  }
}
