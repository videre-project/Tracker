/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useEffect, useState } from "react"
import { getApiUrl } from "@/utils/api-config"
import { useClientState } from "./use-client-state"
import { useNDJSONStream } from "./use-ndjson-stream"
import type { PersonalActivityDTO } from "@/types/api"

export function usePersonalActivity(enabled = true) {
  const { isReady, loading: connecting } = useClientState()
  const [snapshot, setSnapshot] = useState<PersonalActivityDTO[] | null>(null)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    if (!enabled || !isReady) {
      setSnapshot(null)
      setError(null)
    }
  }, [enabled, isReady])
  useNDJSONStream<PersonalActivityDTO[]>({
    url: getApiUrl("/api/activity/watch"),
    enabled: enabled && isReady,
    onMessage: data => { setSnapshot(data); setError(null) },
    onError: () => { setSnapshot(null); setError("Could not load your activity. Reconnecting…") },
    onEnd: () => setSnapshot(null),
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })
  const items = enabled && isReady ? snapshot ?? [] : []
  return {
    active: items.filter(item => item.status === "active"),
    registered: items.filter(item => item.status === "registered"),
    loading: enabled && (connecting || (isReady && snapshot === null && !error)),
    error: enabled && isReady ? error : null,
    isReady,
  }
}
