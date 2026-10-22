/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useEffect, useState } from "react"
import { Copy, FolderOpen } from "lucide-react"
import { Button, Skeleton } from "@videreproject/ui"
import { settingsRequest } from "@/hooks/use-settings"
import type { StorageLocation } from "@/types/api"

export function StorageSettings() {
  const [locations, setLocations] = useState<StorageLocation[] | null>(null)
  const [error, setError] = useState("")
  const [busy, setBusy] = useState<string | null>(null)
  const [copied, setCopied] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    settingsRequest<StorageLocation[]>("/storage", { signal: controller.signal })
      .then(setLocations)
      .catch(error => { if (!controller.signal.aborted) setError(error.message) })
    return () => controller.abort()
  }, [])

  async function open(location: StorageLocation) {
    setBusy(location.id ?? null)
    setError("")
    try { await settingsRequest(`/storage/${location.id}/open`, { method: "POST" }) }
    catch (error) { setError(error instanceof Error ? error.message : "Could not open folder.") }
    finally { setBusy(null) }
  }

  async function copy(location: StorageLocation) {
    setError("")
    try {
      await navigator.clipboard.writeText(location.path ?? "")
      setCopied(location.id ?? null)
    } catch { setError("Could not copy the path. Select the path below to copy it manually.") }
  }

  return (
    <section aria-labelledby="folders-heading" className="space-y-4">
      <h2 id="folders-heading" className="text-sm font-medium">Local folders</h2>
      <p className="text-sm text-muted-foreground">Tracker stores recordings and preferences on this computer. Plugin data is kept separately from installed packages.</p>
      {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
      {!locations && !error && <Skeleton className="h-48" />}
      <ul className="divide-y divide-sidebar-border/60">
        {locations?.map(location => (
          <li key={location.id} className="space-y-3 py-5">
            <div className="flex items-start justify-between gap-4">
              <div className="space-y-1">
                <h3 className="text-sm font-medium">{location.name}</h3>
                <p className="text-sm text-muted-foreground">{location.description}</p>
              </div>
              <div className="flex shrink-0 items-center gap-2">
                <Button type="button" variant="ghost" size="sm" onClick={() => void copy(location)}>
                  <Copy className="mr-2 h-3.5 w-3.5" />{copied === location.id ? "Copied" : "Copy path"}
                </Button>
                <Button type="button" variant="outline" size="sm" disabled={busy != null} onClick={() => void open(location)}>
                  <FolderOpen className="mr-2 h-3.5 w-3.5" />{busy === location.id ? "Opening…" : "Open folder"}
                </Button>
              </div>
            </div>
            <p className="select-text break-all rounded-md border border-sidebar-border/60 bg-muted/20 px-3 py-2 font-mono text-xs">{location.path}</p>
          </li>
        ))}
      </ul>
    </section>
  )
}
