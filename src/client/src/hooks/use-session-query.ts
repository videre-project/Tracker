/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useCallback, useEffect, useState } from "react"
import { getApiUrl } from "@/utils/api-config"
import { useClientState } from "./use-client-state"

async function readResponse<T>(path: string, options?: RequestInit): Promise<T> {
  const response = await fetch(getApiUrl(path), options)
  if (response.status === 204) return undefined as T
  const data = await response.json().catch(() => null)
  if (!response.ok) {
    const message = data?.error ?? data?.title ?? `Request failed (${response.status}).`
    throw new Error(data?.hint ? `${message} - ${data.hint}` : message)
  }
  return data as T
}

export function useSessionQuery<T>(path: string | null, {
  retainDataKey, cacheForSession = false, refreshKey,
}: { retainDataKey?: string; cacheForSession?: boolean; refreshKey?: unknown } = {}) {
  const { isReady, loading: connecting, queryCache } = useClientState()
  const [result, setResult] = useState<{
    session: Map<string, unknown>
    path: string
    data: T | null
    error: string | null
    loading: boolean
  } | null>(null)
  // Keep the previous page or grouping visible only within the same filtered dataset.
  const [previous, setPrevious] = useState<{
    session: Map<string, unknown>
    key: string
    path: string
    data: T
  } | null>(null)
  const [revision, setRevision] = useState(0)
  const refresh = useCallback(() => setRevision(value => value + 1), [])

  useEffect(() => {
    if (!isReady || !path) {
      setResult(null)
      setPrevious(null)
      return
    }

    const controller = new AbortController()
    const cached = cacheForSession ? queryCache.get(path) as T | undefined : undefined
    setResult(current => {
      const sameRequest = current?.session === queryCache && current?.path === path
      const data = sameRequest ? current.data : cached ?? null
      return { session: queryCache, path, data, error: null, loading: !cacheForSession || data == null }
    })

    readResponse<T>(path, { signal: controller.signal })
      .then(data => {
        if (controller.signal.aborted) return
        if (cacheForSession) queryCache.set(path, data)
        setResult({ session: queryCache, path, data, error: null, loading: false })
        setPrevious(retainDataKey === undefined
          ? null
          : { session: queryCache, key: retainDataKey, path, data })
      })
      .catch(reason => {
        if (controller.signal.aborted) return
        setResult(current => {
          const sameRequest = current?.session === queryCache && current?.path === path
          return {
            session: queryCache,
            path,
            data: sameRequest ? current.data : null,
            loading: false,
            error: reason instanceof Error ? reason.message : "Could not load data.",
          }
        })
      })
    return () => controller.abort()
  }, [path, isReady, revision, retainDataKey, cacheForSession, queryCache, refreshKey])

  const current = path && isReady && result?.session === queryCache && result?.path === path
    ? result : null
  const retained = path && isReady && retainDataKey !== undefined &&
    previous?.session === queryCache && previous?.key === retainDataKey
    ? previous : null
  const cached = path && isReady && cacheForSession ? queryCache.get(path) as T | undefined : undefined

  let dataPath: string | null = null
  if (current?.data != null) dataPath = current.path
  else if (retained) dataPath = retained.path
  else if (cached != null) dataPath = path

  let loading = connecting
  if (!path) loading = false
  else if (isReady) loading = current?.loading ?? cached == null

  return {
    data: current?.data ?? retained?.data ?? cached ?? null,
    dataPath,
    error: current?.error ?? null,
    loading,
    refresh,
    isReady,
  }
}
