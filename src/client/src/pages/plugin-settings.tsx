/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useCallback, useEffect, useRef, useState } from "react"
import { Plug, RefreshCw, Upload } from "lucide-react"
import { Badge, Button, Skeleton } from "@videreproject/ui"
import { PluginConfigurationSheet } from "@/components/plugin-configuration"
import { settingsRequest } from "@/hooks/use-settings"
import type { HostInstance, PluginInfo, PluginSettingsStatus } from "@/types/api"

function status(plugin: PluginInfo, hostEnabled: boolean) {
  if (plugin.pendingRemoval) return "Removal pending"
  if (plugin.pendingUpdate) return plugin.loaded ? "Update pending" : "Install pending"
  if (plugin.error) return "Failed"
  if (!hostEnabled) return "Bypassed"
  if (plugin.restartRequired) return "Restart required"
  return plugin.loaded ? "Running" : "Disabled"
}

export default function PluginSettings() {
  const [plugins, setPlugins] = useState<PluginInfo[] | null>(null)
  const [host, setHost] = useState<PluginSettingsStatus | null>(null)
  const [error, setError] = useState("")
  const [busy, setBusy] = useState(false)
  const [restarting, setRestarting] = useState(false)
  const [selected, setSelected] = useState<PluginInfo | null>(null)
  const upload = useRef<HTMLInputElement>(null)
  const lifetime = useRef<AbortController | null>(null)

  const refresh = useCallback(async (signal?: AbortSignal) => {
    const [installed, state] = await Promise.all([
      settingsRequest<PluginInfo[]>("/plugins", { signal }),
      settingsRequest<PluginSettingsStatus>("/plugins/status", { signal }),
    ])
    if (signal?.aborted) return
    setPlugins(installed)
    setHost(state)
  }, [])

  useEffect(() => {
    const controller = new AbortController()
    lifetime.current = controller
    void refresh(controller.signal).catch(error => {
      if (!controller.signal.aborted) setError(error instanceof Error ? error.message : "Could not load plugins.")
    })
    return () => controller.abort()
  }, [refresh])

  async function action(request?: () => Promise<unknown>) {
    setBusy(true)
    setError("")
    try {
      await request?.()
      await refresh(lifetime.current?.signal)
      return true
    } catch (error) {
      if (!lifetime.current?.signal.aborted)
        setError(error instanceof Error ? error.message : "Could not update plugins.")
      return false
    } finally { setBusy(false) }
  }

  async function changeEnabled(plugin: PluginInfo, enabled: boolean) {
    const previous = plugins
    setPlugins(current => current?.map(item => item.id === plugin.id ? { ...item, enabled } : item) ?? null)
    const id = encodeURIComponent(plugin.id ?? "")
    if (!await action(() => settingsRequest(`/plugins/${id}/enabled`, {
      method: "PUT", body: JSON.stringify({ enabled }), signal: lifetime.current?.signal,
    }))) setPlugins(previous)
  }

  async function restart() {
    setRestarting(true)
    setError("")
    const signal = lifetime.current?.signal
    try {
      // A successful request means only that the replacement process was launched.
      // Wait for a different host instance, including when a pending change fails.
      const before = await settingsRequest<HostInstance>("/instance", { signal })
      await settingsRequest("/restart", { method: "POST", signal })
      for (let attempt = 0; attempt < 120 && !signal?.aborted; attempt++) {
        await new Promise(resolve => setTimeout(resolve, 500))
        try {
          const next = await settingsRequest<HostInstance>("/instance", { signal, cache: "no-store" })
          if (next.processId !== before.processId) {
            await refresh(signal)
            return
          }
        } catch { /* Wait for the replacement host to listen. */ }
      }
      if (!signal?.aborted) throw new Error("Tracker has not restarted yet. Close and reopen it to apply changes.")
    } catch (error) {
      if (!signal?.aborted) setError(error instanceof Error ? error.message : "Could not restart Tracker.")
    } finally { setRestarting(false) }
  }

  const disabled = busy || restarting || Boolean(host?.settingsError)
  const failures = host?.failures?.filter(failure => failure.message !== host.settingsError &&
    !plugins?.some(plugin => plugin.error && failure.path?.replace(/\\/g, "/").endsWith(`/${plugin.id}/plugin.json`))) ?? []

  return (
    <div className="mx-auto w-full max-w-5xl space-y-6 px-4 pb-8 pt-4 sm:px-8">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="space-y-2">
          <h1 className="text-xl font-semibold">Plugins</h1>
          <p className="text-sm text-muted-foreground">Manage installed plugins and their configuration.</p>
        </div>
        <div className="flex shrink-0 items-center gap-2">
          <Button variant="ghost" size="sm" disabled={busy || restarting} onClick={() => void action()}>
            <RefreshCw className="mr-2 h-4 w-4" />Refresh
          </Button>
          <Button variant="outline" size="sm" disabled={disabled || !host} onClick={() => upload.current?.click()}>
            <Upload className="mr-2 h-4 w-4" />Install plugin
          </Button>
          <input ref={upload} type="file" accept=".zip,application/zip" className="hidden" aria-label="Plugin package" onChange={event => {
            const file = event.target.files?.[0]
            if (file) void action(() => {
              const form = new FormData()
              form.append("package", file)
              return settingsRequest("/plugins", { method: "POST", body: form, signal: lifetime.current?.signal })
            })
            event.target.value = ""
          }} />
        </div>
      </div>
      {error && <div role="alert" className="space-y-2 rounded-md border border-destructive/40 p-4 text-sm text-red-400">
        <p className="break-words">{error}</p>
        {plugins === null && <Button variant="outline" size="sm" disabled={busy} onClick={() => void action()}>Try again</Button>}
      </div>}
      {host?.settingsError && <p role="alert" className="break-words text-sm text-red-400">{host.settingsError}</p>}
      {host && !host.hostEnabled && <p className="text-sm text-muted-foreground">Plugins are bypassed for this process. Relaunch Tracker without the Plugins:Enabled=false override to run them.</p>}
      {failures.length > 0 && <section aria-label="Plugin errors" className="space-y-3 rounded-md border border-destructive/40 p-4">
        {failures.map(failure => <div key={failure.path} className="space-y-1 text-sm">
          <p className="break-all font-mono text-xs text-muted-foreground">{failure.path}</p>
          <p className="break-words text-red-400">{failure.message}</p>
        </div>)}
      </section>}
      {restarting || host?.restartRequired || host?.settingsError ? <div role="status" className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-sidebar-border bg-muted/30 p-4">
        <p className="text-sm">{restarting ? "Restarting Tracker…" : host?.settingsError ? "Restore the settings file, then restart Tracker." : "Restart Tracker to apply plugin changes."}</p>
        <Button size="sm" disabled={busy || restarting} onClick={() => void restart()}>{restarting ? "Restarting…" : "Restart Tracker"}</Button>
      </div> : null}
      {plugins === null ? (!error && <Skeleton className="h-32" />) : plugins.length === 0 ? (
        <div className="space-y-2 rounded-lg border border-sidebar-border/60 p-6 text-sm">
          <h2 className="font-medium">No plugins installed</h2>
          <p className="text-muted-foreground">Install a published plugin ZIP to add an integration or tool.</p>
        </div>
      ) : <ul className="divide-y divide-sidebar-border/60 border-y border-sidebar-border/60">
        {plugins.map(plugin => {
          const id = encodeURIComponent(plugin.id ?? "")
          return <li key={plugin.id} className="space-y-4 py-5">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div className="min-w-0 flex-1 space-y-2">
                <div className="flex flex-wrap items-center gap-2">
                  <Plug className="h-4 w-4 shrink-0 text-muted-foreground" />
                  <h2 className="break-words text-sm font-medium">{plugin.name}</h2>
                  <span className="text-xs text-muted-foreground">{plugin.version}</span>
                </div>
                {plugin.description && <p className="text-sm text-muted-foreground">{plugin.description}</p>}
                {plugin.error && <p className="break-words text-sm text-red-400">{plugin.error}</p>}
              </div>
              <Badge variant={plugin.error ? "destructive" : "secondary"} className="text-xs font-normal">{status(plugin, host?.hostEnabled ?? true)}</Badge>
            </div>
            <div className="flex flex-wrap items-center justify-between gap-3">
              {plugin.pendingRemoval ? <p className="text-xs text-muted-foreground">Plugin data is kept after removal.</p> : <label className="flex items-center gap-2 text-sm">
                <input type="checkbox" checked={plugin.enabled ?? false} disabled={disabled} aria-label={`Enable ${plugin.name}`}
                  className="h-4 w-4 accent-primary" onChange={event => void changeEnabled(plugin, event.target.checked)} />Enabled
              </label>}
              <div className="flex items-center gap-2">
                {!plugin.pendingRemoval && <Button variant="outline" size="sm" disabled={disabled} onClick={() => setSelected(plugin)}>Configure</Button>}
                <Button variant="ghost" size="sm" disabled={disabled} onClick={() => void action(() => settingsRequest(`/plugins/${id}${plugin.pendingRemoval ? "/restore" : ""}`, {
                  method: plugin.pendingRemoval ? "POST" : "DELETE", signal: lifetime.current?.signal,
                }))}>{plugin.pendingRemoval ? "Undo removal" : "Remove"}</Button>
              </div>
            </div>
          </li>
        })}
      </ul>}
      <p className="text-xs leading-relaxed text-muted-foreground">Installations, updates, configuration, and enablement changes apply after restart. Install a newer ZIP to update a plugin. Removing a plugin keeps its data.</p>
      {selected && <PluginConfigurationSheet key={selected.id} plugin={selected} onClose={() => setSelected(null)} onSaved={() => { void action() }} />}
    </div>
  )
}
