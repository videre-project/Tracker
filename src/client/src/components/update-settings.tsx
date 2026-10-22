/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useState } from "react"
import { Button, Skeleton } from "@videreproject/ui"
import type { UpdateStatus } from "@/types/api"
import { useUpdates } from "@/hooks/use-updates"
import { settingsRequest } from "@/hooks/use-settings"

export function UpdateSettings() {
  const { status, error: statusError, setStatus } = useUpdates()
  const [error, setError] = useState("")
  const [action, setAction] = useState<string | null>(null)

  async function run(path: string, method = "POST", body?: boolean) {
    setAction(path)
    setError("")
    try {
      const value = await settingsRequest<UpdateStatus>(`/updates/${path}`, {
        method, body: body === undefined ? undefined : JSON.stringify(body),
      })
      setStatus(value)
    } catch (error) { setError(error instanceof Error ? error.message : "The update could not be completed.") }
    finally { setAction(null) }
  }

  const busy = action != null || status?.busy || status?.applying
  const progress = status?.totalBytes ? Math.min(100, (status.downloadedBytes ?? 0) / status.totalBytes * 100) : 0

  return (
    <section aria-labelledby="release-heading" className="space-y-5">
      <div className="flex items-start justify-between gap-4">
        <div className="space-y-1">
          <h2 id="release-heading" className="text-sm font-medium">Application version</h2>
          {status && <p className="text-sm text-muted-foreground">Installed: {status.currentVersion}</p>}
        </div>
        <Button variant="outline" size="sm" disabled={busy} onClick={() => void run("check")}>
          {action === "check" ? "Checking…" : "Check for updates"}
        </Button>
      </div>
      {!status && !error && !statusError && <Skeleton className="h-24" />}
      {(error || statusError || status?.error) && <p role="alert" className="text-sm text-destructive">{error || statusError || status?.error}</p>}
      {status && (
        <>
          {status.customSource && <p className="text-xs text-muted-foreground">Using a custom release feed for local upgrade testing.</p>}
          {status.availableVersion || status.stagedVersion ? (
            <div className="space-y-4 rounded-lg border border-sidebar-border/60 p-4">
              <div className="space-y-1">
                <h3 className="text-sm font-medium">{status.stagedVersion ? `${status.stagedVersion} is ready to install` : `${status.availableVersion} is available`}</h3>
                <p className="text-sm text-muted-foreground">
                  {status.applying ? "Installing the update and restarting Tracker…" : status.scheduled
                    ? "Tracker will install this update and restart after MTGO closes."
                    : "Close MTGO before installing. Tracker will restart and keep your recordings, preferences, and plugins."}
                </p>
              </div>
              {(action === "download" || status.busy && (status.downloadedBytes ?? 0) > 0) && (
                <div className="space-y-2">
                  <div role="progressbar" aria-label="Update download" aria-valuenow={Math.round(progress)} aria-valuemin={0} aria-valuemax={100} className="h-1.5 overflow-hidden rounded-full bg-muted">
                    <div className="h-full bg-primary" style={{ width: `${progress}%` }} />
                  </div>
                  <p className="text-xs text-muted-foreground">Downloaded {((status.downloadedBytes ?? 0) / 1048576).toFixed(1)} MiB{status.totalBytes ? ` of ${(status.totalBytes / 1048576).toFixed(1)} MiB` : ""}</p>
                </div>
              )}
              <div className="flex items-center gap-2">
                {!status.stagedVersion ? (
                  <Button size="sm" disabled={busy} onClick={() => void run("download")}>{action === "download" ? "Downloading…" : "Download update"}</Button>
                ) : status.scheduled ? (
                  <Button variant="outline" size="sm" disabled={busy} onClick={() => void run("schedule", "PUT", false)}>Cancel scheduled update</Button>
                ) : (
                  <Button size="sm" disabled={busy} onClick={() => void run(status.mtgoRunning ? "schedule" : "apply", status.mtgoRunning ? "PUT" : "POST", status.mtgoRunning ? true : undefined)}>
                    {status.applying ? "Updating…" : status.mtgoRunning ? "Install after MTGO closes" : "Update and restart"}
                  </Button>
                )}
                {status.releaseUrl && <Button asChild variant="ghost" size="sm"><a href={status.releaseUrl} target="_blank" rel="noreferrer">Release notes</a></Button>}
              </div>
            </div>
          ) : status.lastChecked && !status.busy ? <p className="text-sm text-muted-foreground">No newer release is available.</p> : null}
          {status.releaseNotes && <div className="whitespace-pre-wrap break-words border-t border-sidebar-border/60 pt-5 text-sm leading-relaxed text-muted-foreground">{status.releaseNotes}</div>}
          {status.lastChecked && <p className="text-xs text-muted-foreground">Last checked: {new Date(status.lastChecked).toLocaleString()}</p>}
        </>
      )}
    </section>
  )
}
