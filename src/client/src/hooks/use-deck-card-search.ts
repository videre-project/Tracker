/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useEffect, useState } from "react"

import { getApiUrl } from "@/utils/api-config"

export type CardSearchResult = {
  id: string
  mtgoId: number
  setCode: string
  name: string
  type: string
  text: string
  colors: string[]
  imageUrl: string
  power?: string | null
  toughness?: string | null
  loyalty?: string | null
  defense?: string | null
}

export function useDeckCardSearch(query: string, limit = 24) {
  const [results, setResults] = useState<CardSearchResult[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!query) {
      setResults([])
      setError(null)
      setLoading(false)
      return
    }

    const abortController = new AbortController()
    const timeout = window.setTimeout(async () => {
      setLoading(true)
      setError(null)

      try {
        const params = new URLSearchParams({ q: query, limit: String(limit) })
        const response = await fetch(
          getApiUrl(`/api/decks/search-cards?${params.toString()}`),
          { signal: abortController.signal }
        )
        if (!response.ok) throw new Error(`HTTP ${response.status}`)
        setResults(await response.json() as CardSearchResult[])
      } catch (requestError) {
        if (requestError instanceof Error && requestError.name === "AbortError") return
        setResults([])
        setError(requestError instanceof Error ? requestError.message : "Card search failed")
      } finally {
        if (!abortController.signal.aborted) setLoading(false)
      }
    }, 250)

    return () => {
      window.clearTimeout(timeout)
      abortController.abort()
    }
  }, [limit, query])

  return { results, loading, error }
}
