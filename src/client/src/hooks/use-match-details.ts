/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useEffect, useState } from "react"

import { useClientState } from "@/hooks/use-client-state"
import type { MatchDetailsDTO } from "@/types/api"
import { getApiUrl } from "@/utils/api-config"

export function useMatchDetails(matchId: number | null) {
  const [data, setData] = useState<MatchDetailsDTO | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<Error | null>(null)
  const { isReady: clientReady, loading: clientLoading } = useClientState()

  useEffect(() => {
    if (clientLoading || !matchId) return
    if (!clientReady) {
      setLoading(false)
      return
    }

    setLoading(true)
    setError(null)
    fetch(getApiUrl(`/api/games/match/${matchId}`))
      .then(response => {
        if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`)
        return response.json() as Promise<MatchDetailsDTO>
      })
      .then(responseData => setData(responseData))
      .catch(requestError => {
        console.error("Failed to fetch match details:", requestError)
        setError(requestError instanceof Error ? requestError : new Error(String(requestError)))
      })
      .finally(() => setLoading(false))
  }, [clientLoading, clientReady, matchId])

  return { data, loading, error }
}
