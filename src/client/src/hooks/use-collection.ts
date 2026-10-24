/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useEffect } from "react"
import { getApiUrl } from "@/utils/api-config"
import type { CollectionSnapshotDTO } from "@/types/api"
import { useClientState } from "./use-client-state"
import { useNDJSONStream } from "./use-ndjson-stream"
import { useSessionQuery } from "./use-session-query"

export function useCollectionCards() {
  const { queryCache } = useClientState()
  const { data: snapshot, loading, error, refresh, isReady } = useSessionQuery<CollectionSnapshotDTO>(
    "/api/collection/cards", { cacheForSession: true },
  )
  useNDJSONStream<number>({
    url: getApiUrl("/api/collection/watch"),
    enabled: isReady,
    restartKey: queryCache,
    onMessage: refresh,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })
  useEffect(() => {
    window.addEventListener("focus", refresh)
    return () => window.removeEventListener("focus", refresh)
  }, [refresh])

  return { snapshot, cards: snapshot?.cards ?? [], products: snapshot?.products ?? [], loading, error, refresh }
}
