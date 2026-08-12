/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useState, useCallback, type ReactNode } from "react"
import { useClientState } from "@/hooks/use-client-state"
import { CardArtContext, type CardArtContextValue } from "@/hooks/use-card-art"
import { getApiUrl } from "@/utils/api-config"

// Global cache shared across all contexts
const globalCardArtCache = new Map<string, string>()
const pendingFetches = new Map<string, Promise<string | null>>()

export function CardArtProvider({ children }: { children: ReactNode }) {
  const { isReady: clientReady, loading: clientLoading } = useClientState()
  const [, setCacheVersion] = useState(0)

  const getArtUrl = useCallback((cardName: string): string | null => {
    return globalCardArtCache.get(cardName) ?? null
  }, [])

  const fetchSingleCard = useCallback(async (cardName: string): Promise<string | null> => {
    // Check cache first
    const cached = globalCardArtCache.get(cardName)
    if (cached) return cached

    // Check pending
    const pending = pendingFetches.get(cardName)
    if (pending) return pending

    const fetchPromise = (async () => {
      try {
        const response = await fetch(
          getApiUrl(`/api/collection/cards/${encodeURIComponent(cardName)}/art`)
        )

        if (!response.ok) return null

        const blob = await response.blob()
        const objectUrl = URL.createObjectURL(blob)
        globalCardArtCache.set(cardName, objectUrl)
        return objectUrl
      } catch {
        return null
      } finally {
        pendingFetches.delete(cardName)
      }
    })()

    pendingFetches.set(cardName, fetchPromise)
    return fetchPromise
  }, [])

  const prefetchCards = useCallback(async (cardNames: string[]): Promise<void> => {
    if (!clientReady) return

    const uncached = cardNames.filter(name => name && !globalCardArtCache.has(name))
    if (uncached.length === 0) return

    await Promise.all(uncached.map(fetchSingleCard))

    // Trigger re-render of all consumers
    setCacheVersion(v => v + 1)
  }, [clientReady, fetchSingleCard])

  const value: CardArtContextValue = {
    getArtUrl,
    prefetchCards,
    isReady: clientReady && !clientLoading
  }

  return (
    <CardArtContext.Provider value={value}>
      {children}
    </CardArtContext.Provider>
  )
}
