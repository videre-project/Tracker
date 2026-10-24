/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useCallback, useEffect, useRef, useState, type ReactNode } from "react"
import { ApplicationSettingsContext } from "@/hooks/use-application-settings"
import { settingsRequest } from "@/hooks/use-settings"
import { preferences as normalize } from "@/lib/application-preferences"
import type { SettingsResponse } from "@/types/api"

export function ApplicationSettingsProvider({ children }: { children: ReactNode }) {
  const [settings, updateSettings] = useState<SettingsResponse | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  // A late refresh must not replace settings saved while it was in flight.
  const revision = useRef(0)
  const setSettings = useCallback((value: SettingsResponse) => {
    revision.current++
    updateSettings(value)
    setError(null)
  }, [])
  const refresh = useCallback(async (signal?: AbortSignal) => {
    const request = ++revision.current
    try {
      const response = await settingsRequest<SettingsResponse>("", { signal })
      if (!signal?.aborted && request === revision.current) { updateSettings(response); setError(null) }
      return response
    } catch (error) {
      if (!signal?.aborted && request === revision.current) setError(error instanceof Error ? error.message : "Could not load settings.")
      throw error
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    void refresh(controller.signal)
      .catch(() => { /* Defaults keep the UI available when the host cannot be reached. */ })
      .finally(() => { if (!controller.signal.aborted) setLoading(false) })
    return () => controller.abort()
  }, [refresh])

  return (
    <ApplicationSettingsContext.Provider value={{ preferences: normalize(settings?.application), settings, setSettings, refresh, error: error ?? settings?.error ?? null }}>
      {loading ? <div role="status" className="flex items-center justify-center text-sm text-muted-foreground">Loading preferences…</div> : children}
    </ApplicationSettingsContext.Provider>
  )
}
