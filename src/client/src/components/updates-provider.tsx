/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useCallback, useEffect, useRef, useState, type ReactNode } from "react"
import { useLocation } from "react-router-dom"
import { UpdatesContext } from "@/hooks/use-updates"
import { settingsRequest } from "@/hooks/use-settings"
import type { UpdateStatus } from "@/types/api"

export function UpdatesProvider({ children }: { children: ReactNode }) {
  const { pathname } = useLocation()
  const [status, updateStatus] = useState<UpdateStatus | null>(null)
  const [error, setError] = useState<string | null>(null)
  // Actions supersede polling requests that were already in flight.
  const revision = useRef(0)
  const installing = useRef<string | null>(null)
  const setStatus = useCallback((value: UpdateStatus) => {
    revision.current++
    updateStatus(value)
    setError(null)
  }, [])
  const interval = status?.applying || status?.busy ? 1000
    : status?.scheduled || pathname === "/settings/updates" ? 2000 : 60000

  useEffect(() => {
    const controller = new AbortController()
    let timer: ReturnType<typeof setTimeout>
    let loading = false
    const update = async () => {
      if (loading) return
      clearTimeout(timer)
      loading = true
      const request = ++revision.current
      try {
        if (!document.hidden) {
          const value = await settingsRequest<UpdateStatus>("/updates", { signal: controller.signal })
          if (!controller.signal.aborted && request === revision.current) {
            updateStatus(value)
            setError(null)
          }
        }
      } catch (error) {
        if (!controller.signal.aborted && request === revision.current)
          setError(error instanceof Error ? error.message : "Could not load update status.")
      }
      finally {
        loading = false
        if (!controller.signal.aborted) timer = setTimeout(() => void update(), interval)
      }
    }
    const wake = () => { void update() }
    wake()
    window.addEventListener("focus", wake)
    document.addEventListener("visibilitychange", wake)
    return () => {
      controller.abort()
      clearTimeout(timer)
      window.removeEventListener("focus", wake)
      document.removeEventListener("visibilitychange", wake)
    }
  }, [interval])

  useEffect(() => {
    if (status?.applying && status.stagedVersion) installing.current = status.stagedVersion.replace(/^v/i, "")
    if (installing.current && status?.currentVersion?.replace(/^v/i, "") === installing.current)
      window.location.reload()
  }, [status])

  return <UpdatesContext.Provider value={{ status, error, setStatus }}>{children}</UpdatesContext.Provider>
}
