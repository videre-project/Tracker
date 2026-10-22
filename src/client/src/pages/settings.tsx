/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useCallback, useEffect, useRef, useState } from "react"
import { Outlet, useLocation } from "react-router-dom"
import { Plug, Upload } from "lucide-react"
import {
  Badge, Button,
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Skeleton
} from "@videreproject/ui"
import type { PluginInstallation, PluginSettingsStatus, HostInstance, SettingsResponse } from "@/types/api"
import { settingsRequest } from "@/hooks/use-settings"
import { PluginConfigurationSheet } from "@/components/plugin-configuration"
import { SettingsOverview } from "@/components/settings-overview"
import { settingsPages } from "@/lib/settings-pages"
import { pluginStatus } from "@/lib/plugin-status"
import { useApplicationSettings } from "@/hooks/use-application-settings"
import { defaultPreferences as defaults, preferences, type Preferences, type Theme } from "@/lib/application-preferences"
import { UpdateSettings } from "@/components/update-settings"
import { StorageSettings } from "@/components/storage-settings"

type ToggleKey = { [K in keyof Preferences]: Preferences[K] extends boolean ? K : never }[keyof Preferences]
type ChoiceKey = "startupPage" | "historyRangeDays" | "historyPageSize" | "deckSortMode"

function Toggle({ title, description, checked, disabled, onChange }: {
  title: string; description: string; checked: boolean; disabled: boolean; onChange: (checked: boolean) => void
}) {
  return (
    <label className="flex cursor-pointer items-center justify-between gap-6 py-5">
      <span className="space-y-1">
        <span className="block text-sm font-medium">{title}</span>
        <span className="block text-sm leading-relaxed text-muted-foreground">{description}</span>
      </span>
      <input type="checkbox" checked={checked} disabled={disabled} onChange={event => onChange(event.target.checked)} className="h-4 w-4 shrink-0 accent-primary" />
    </label>
  )
}

export default function Settings() {
  const { settings, preferences: savedPreferences, setSettings, refresh, error: settingsError } = useApplicationSettings()
  const saved = settings ? savedPreferences : null
  const [draft, setDraft] = useState<Preferences>(savedPreferences)
  const [plugins, setPlugins] = useState<PluginInstallation[]>([])
  const [pluginHost, setPluginHost] = useState<PluginSettingsStatus | null>(null)
  const restartRequired = settings?.restartRequired ?? false
  const [pluginError, setPluginError] = useState("")
  const [error, setError] = useState("")
  const [saving, setSaving] = useState(false)
  const [restarting, setRestarting] = useState(false)
  const [busy, setBusy] = useState<string | null>(null)
  const [selected, setSelected] = useState<PluginInstallation | null>(null)
  const upload = useRef<HTMLInputElement>(null)
  const { pathname } = useLocation()
  const page = settingsPages.find(page => page.path === pathname)
  const section = pathname.split("/")[2] ?? "overview"
  const pluginsDisabled = busy != null || restarting || !pluginHost || Boolean(pluginHost.settingsError) || Boolean(pluginError)
  const pluginFailures = pluginHost?.failures?.filter(failure => failure.message !== pluginHost.settingsError &&
    !plugins.some(plugin => plugin.error && failure.path?.replace(/\\/g, "/").endsWith(`/${plugin.id}/plugin.json`))) ?? []
  const dirty = saved != null && Object.keys(defaults).some(key => draft[key as keyof Preferences] !== saved[key as keyof Preferences])

  async function load() {
    try { setDraft(preferences((await refresh()).application)) }
    catch { /* The shared settings owner reports loading failures. */ }
  }

  const loadPlugins = useCallback(async (signal?: AbortSignal) => {
    setPluginError("")
    try {
      const [installed, host] = await Promise.all([
        settingsRequest<PluginInstallation[]>("/plugins", { signal }),
        settingsRequest<PluginSettingsStatus>("/plugins/status", { signal })
      ])
      if (!signal?.aborted) { setPlugins(installed); setPluginHost(host) }
    } catch (error) {
      if (!signal?.aborted) setPluginError(error instanceof Error ? error.message : "Could not load plugins.")
    }
  }, [])
  const needsPlugins = section === "overview" || section === "plugins"
  useEffect(() => {
    if (!needsPlugins) return
    const controller = new AbortController()
    void loadPlugins(controller.signal)
    return () => controller.abort()
  }, [loadPlugins, needsPlugins])

  async function refreshPlugins() {
    await Promise.all([loadPlugins(), refresh()])
  }

  async function save(event: React.FormEvent) {
    event.preventDefault()
    setSaving(true)
    setError("")
    try {
      const response = await settingsRequest<SettingsResponse>("", { method: "PUT", body: JSON.stringify(draft) })
      const value = preferences(response.application)
      setDraft(value)
      setSettings(response)
    } catch (error) { setError(error instanceof Error ? error.message : "Could not save settings.") }
    finally { setSaving(false) }
  }

  async function pluginAction(key: string, action: () => Promise<unknown>) {
    setBusy(key)
    setError("")
    try { await action(); await refreshPlugins(); return true }
    catch (error) { setError(error instanceof Error ? error.message : "Could not update plugin."); return false }
    finally { setBusy(null) }
  }

  async function changeEnabled(plugin: PluginInstallation, enabled: boolean) {
    const previous = plugins
    setPlugins(current => current.map(item => item.id === plugin.id ? { ...item, enabled } : item))
    const id = encodeURIComponent(plugin.id ?? "")
    if (!await pluginAction(id, () => settingsRequest(`/plugins/${id}/enabled`, {
      method: "PUT", body: JSON.stringify({ enabled })
    }))) setPlugins(previous)
  }

  async function restart() {
    setRestarting(true)
    setError("")
    try {
      const previous = await settingsRequest<HostInstance>("/instance")
      await settingsRequest("/restart", { method: "POST" })
      for (let attempt = 0; attempt < 120; attempt++) {
        await new Promise(resolve => setTimeout(resolve, 500))
        try {
          const response = await settingsRequest<HostInstance>("/instance")
          if (response.processId != null && response.processId !== previous.processId) { window.location.reload(); return }
        } catch { /* Wait for the new host to listen. */ }
      }
      throw new Error("Tracker has not restarted yet. Close and reopen it to finish applying changes.")
    } catch (error) { setError(error instanceof Error ? error.message : "Could not restart Tracker.") }
    finally { setRestarting(false) }
  }

  function toggle(key: ToggleKey, title: string, description: string) {
    return <Toggle title={title} description={description} checked={draft[key]} disabled={saving || restarting} onChange={checked => setDraft(current => ({ ...current, [key]: checked }))} />
  }

  function choose(key: ChoiceKey, title: string, description: string, choices: [string, string][]) {
    return (
      <div className="flex items-center justify-between gap-6 py-5">
        <div className="space-y-1">
          <label htmlFor={key} className="text-sm font-medium">{title}</label>
          <p id={`${key}-description`} className="text-sm leading-relaxed text-muted-foreground">{description}</p>
        </div>
        <Select disabled={saving || restarting} value={String(draft[key])} onValueChange={value => setDraft(current => ({
          ...current, [key]: typeof defaults[key] === "number" ? Number(value) : value,
        }))}>
          <SelectTrigger id={key} aria-describedby={`${key}-description`} className="h-8 w-44 shrink-0"><SelectValue /></SelectTrigger>
          <SelectContent>{choices.map(([value, label]) => <SelectItem key={value} value={value}>{label}</SelectItem>)}</SelectContent>
        </Select>
      </div>
    )
  }

  return (
    <div className={page ? "flex w-full flex-col gap-6 px-4 py-6" : "pb-4"}>
      {page && (
        <header className="space-y-2 border-b border-sidebar-border/60 pb-6">
          <h1 className="text-xl font-semibold">{section === "overview" ? "Settings" : page.title}</h1>
          <p className="text-sm text-muted-foreground">{page.description}</p>
        </header>
      )}
      {(error || settingsError || needsPlugins && pluginError) && (
        <div role="alert" className="flex items-center justify-between gap-3 rounded-md border border-destructive/40 p-3 text-sm">
          <span>{error || settingsError || pluginError}</span>
          {(!saved || needsPlugins && pluginError) && <Button variant="outline" size="sm"
            onClick={() => { void (!saved ? load() : loadPlugins()) }}>Try again</Button>}
        </div>
      )}
      {restartRequired && (
        <div role="status" className="flex items-center justify-between gap-4 rounded-md border border-sidebar-border/60 bg-muted/30 px-4 py-3 text-sm">
          <div className="space-y-1">
            <p className="font-medium">Restart to apply changes</p>
            <p className="text-xs text-muted-foreground">Saved launch, window, tracking, and plugin changes take effect when Tracker restarts.</p>
          </div>
          <Button variant="outline" size="sm" disabled={restarting || busy != null || saving || dirty} onClick={() => void restart()}>
            {restarting ? "Restarting…" : "Restart now"}
          </Button>
        </div>
      )}
      {!saved && !settingsError && <Skeleton className="h-64" />}
      {saved && section === "overview" && <SettingsOverview plugins={pluginHost ? plugins : null} hostEnabled={pluginHost?.hostEnabled ?? true} />}
      {saved && section === "general" && (
        <>
          <section aria-labelledby="launch-heading">
            <h2 id="launch-heading" className="text-sm font-medium">Launch</h2>
            {choose("startupPage", "Open on launch", "Choose the page shown when Tracker starts. Applies after restart.", [
              ["/", "Dashboard"], ["/collection", "Collection"], ["/decks", "Decks"],
              ["/events", "Events"], ["/history", "History"], ["/trades", "Trades"],
            ])}
          </section>
          <section aria-labelledby="sidebar-heading" className="border-t border-sidebar-border/60 pt-6">
            <h2 id="sidebar-heading" className="text-sm font-medium">Sidebar</h2>
            <div className="divide-y divide-sidebar-border/60">
              {toggle("showActiveEvents", "Show active matches and events", "Keep your matches, league runs, and tournaments in the sidebar.")}
              {toggle("showUpcomingEvents", "Show registered events", "Show events and queues you've entered that are waiting to start.")}
            </div>
          </section>
          <section aria-labelledby="history-heading" className="border-t border-sidebar-border/60 pt-6">
            <h2 id="history-heading" className="text-sm font-medium">Dashboard and history</h2>
            <div className="divide-y divide-sidebar-border/60">
              {choose("historyRangeDays", "Initial date range", "Start the Dashboard and History with this range. You can still adjust it on either page.", [
                ["7", "Last 7 days"], ["30", "Last 30 days"], ["90", "Last 90 days"], ["0", "All time"],
              ])}
              {choose("historyPageSize", "Matches per page", "Choose how many entries appear on each History page.", [
                ["25", "25 matches"], ["50", "50 matches"], ["100", "100 matches"],
              ])}
            </div>
          </section>
        </>
      )}
      {saved && section === "decks" && (
        <section aria-labelledby="deck-layout-heading">
          <h2 id="deck-layout-heading" className="text-sm font-medium">Editor defaults</h2>
          <p className="mt-1 text-xs text-muted-foreground">Used when you open a deck. You can change the layout while editing.</p>
          <div className="divide-y divide-sidebar-border/60">
            {choose("deckSortMode", "Group cards by", "Choose how cards are arranged in the deck editor.", [
              ["cmc", "Mana value"], ["colors", "Color"], ["types", "Card type"], ["rarity", "Rarity"],
            ])}
            {toggle("showSideboard", "Expand sideboard", "Open decks with sideboard cards grouped into multiple columns.")}
            {toggle("showDeckTools", "Show editor sidebar", "Open the card search, statistics, and revision history sidebar when editing a deck.")}
          </div>
        </section>
      )}
      {saved && section === "replay" && (
        <section aria-labelledby="replay-controls-heading">
          <h2 id="replay-controls-heading" className="text-sm font-medium">Viewing replays</h2>
          <div className="divide-y divide-sidebar-border/60">
            {toggle("replayFollowLive", "Follow live games", "Advance to new states while viewing the latest state of an ongoing game. Stepping back pauses following until you return to the end.")}
            {toggle("replayExpandOpponentZones", "Expand opponent zones on hover", "Reveal the upper player's hand and zone panels when you hover over them.")}
            {toggle("replayKeyboardShortcuts", "Keyboard navigation", "Use Left/Right or H/L to step through states, and Home/End to jump to the start or end.")}
          </div>
        </section>
      )}
      {saved && section === "updates" && (
        <>
          <UpdateSettings />
          <section className="border-t border-sidebar-border/60 pt-4">
            {toggle("checkForUpdates", "Check for updates automatically", "Check GitHub when Tracker starts and every six hours while it is running. Updates are downloaded and installed only when you choose.")}
          </section>
        </>
      )}
      {saved && section === "storage" && <StorageSettings />}
      {saved && section === "appearance" && (
        <>
          <section aria-labelledby="theme-heading">
            <h2 id="theme-heading" className="text-sm font-medium">Theme</h2>
            <div className="flex items-center justify-between gap-6 py-5">
              <div className="space-y-1">
                <label htmlFor="theme" className="text-sm font-medium">Color theme</label>
                <p className="text-sm leading-relaxed text-muted-foreground">Use a light or dark appearance, or follow your system. Applies when saved.</p>
              </div>
              <Select disabled={saving || restarting} value={draft.theme} onValueChange={theme => setDraft(current => ({ ...current, theme: theme as Theme }))}>
                <SelectTrigger id="theme" className="h-8 w-40 shrink-0"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="dark">Dark</SelectItem>
                  <SelectItem value="light">Light</SelectItem>
                  <SelectItem value="system">System</SelectItem>
                </SelectContent>
              </Select>
            </div>
          </section>
          <section aria-labelledby="window-heading" className="border-t border-sidebar-border/60 pt-6">
            <h2 id="window-heading" className="text-sm font-medium">Window</h2>
            <p className="mt-1 text-xs text-muted-foreground">Changes apply after restarting Tracker.</p>
            <div className="divide-y divide-sidebar-border/60">
              {toggle("useCustomTitleBar", "Custom title bar", "Use Tracker's title bar and window controls.")}
              {toggle("startMaximized", "Start maximized", "Open Tracker using the full desktop workspace.")}
            </div>
          </section>
        </>
      )}
      {saved && section === "tracking" && (
        <>
          <section aria-labelledby="recording-heading">
            <h2 id="recording-heading" className="text-sm font-medium">Recording</h2>
            <p className="mt-1 text-xs text-muted-foreground">Changes apply after restarting Tracker. Existing recordings are kept.</p>
            <div className="divide-y divide-sidebar-border/60">
              {toggle("recordReplays", "Record game replays", "Save game states to watch games back in History. Match and game results are still recorded when this is off.")}
              {toggle("recordTrades", "Record trade history", "Save trades and product openings so you can review collection changes and trade chat.")}
            </div>
          </section>
          <section aria-labelledby="sync-heading" className="border-t border-sidebar-border/60 pt-6">
            <h2 id="sync-heading" className="text-sm font-medium">Deck synchronization</h2>
            {toggle("synchronizeDecks", "Synchronize deck edits", "Send deck edits, imports, and deletions to MTGO. Turning this off leaves queued changes waiting until you enable it again and restart.")}
          </section>
        </>
      )}
      {saved && section === "plugins" && (
        <section aria-labelledby="installed-heading" className="space-y-4">
          {pluginHost && !pluginHost.hostEnabled && <p className="text-sm text-muted-foreground">Plugins are bypassed for this process. Relaunch Tracker without the Plugins:Enabled=false override to run them.</p>}
          {pluginFailures.length > 0 && <section aria-label="Plugin errors" className="space-y-3 rounded-md border border-destructive/40 p-4">
            {pluginFailures.map(failure => <div key={failure.path} className="space-y-1 text-sm">
              <p className="break-all font-mono text-xs text-muted-foreground">{failure.path}</p>
              <p className="break-words text-destructive">{failure.message}</p>
            </div>)}
          </section>}
          <div className="flex items-center justify-between gap-4">
            <div className="space-y-1">
              <h2 id="installed-heading" className="text-sm font-medium">Installed plugins</h2>
              <p className="text-xs text-muted-foreground">{plugins.length} installed, {plugins.filter(plugin => plugin.loaded).length} running</p>
            </div>
            <Button variant="outline" size="sm" className="shrink-0 gap-2" disabled={pluginsDisabled} onClick={() => upload.current?.click()}>
              <Upload className="h-3.5 w-3.5" />{busy === "install" ? "Installing…" : "Install plugin"}
            </Button>
            <input ref={upload} type="file" accept=".zip,application/zip" className="hidden" aria-label="Plugin package" onChange={event => {
              const file = event.target.files?.[0]
              if (file) void pluginAction("install", () => {
                const form = new FormData()
                form.append("package", file)
                return settingsRequest("/plugins", { method: "POST", body: form })
              })
              event.target.value = ""
            }} />
          </div>
          {!pluginHost ? <p className="text-sm text-muted-foreground">{pluginError ? "Plugin status is unavailable." : "Loading plugins…"}</p> : !plugins.length ? (
            <div className="rounded-lg border border-sidebar-border/60 p-5 text-sm">
              <p className="font-medium">Extend your Tracker</p>
              <p className="mt-2 text-muted-foreground">Install a published plugin ZIP to add an integration or tool. Its settings will appear here.</p>
            </div>
          ) : (
            <ul className="divide-y divide-sidebar-border/60 border-y border-sidebar-border/60">
              {plugins.map(plugin => {
                const id = encodeURIComponent(plugin.id ?? "")
                return (
                  <li key={plugin.id} className="space-y-4 py-5">
                    <div className="flex items-start justify-between gap-4">
                      <div className="min-w-0 space-y-2">
                        <div className="flex flex-wrap items-center gap-2">
                          <Plug className="h-4 w-4 text-muted-foreground" />
                          <h3 className="text-sm font-medium">{plugin.name}</h3>
                          <span className="text-xs text-muted-foreground">{plugin.version}</span>
                        </div>
                        {plugin.description && <p className="text-sm text-muted-foreground">{plugin.description}</p>}
                        {plugin.error && <p className="text-xs text-destructive">{plugin.error}</p>}
                      </div>
                      <Badge variant={plugin.error ? "destructive" : "secondary"} className="shrink-0 text-xs font-normal">{pluginStatus(plugin, pluginHost?.hostEnabled ?? true)}</Badge>
                    </div>
                    <div className="flex items-center justify-between gap-4">
                      {!plugin.pendingRemoval ? (
                        <label className="flex items-center gap-2 text-sm">
                          <input type="checkbox" aria-label={`Enable ${plugin.name}`} checked={plugin.enabled ?? false} disabled={pluginsDisabled}
                            className="h-4 w-4 accent-primary" onChange={event => void changeEnabled(plugin, event.target.checked)} />
                          Enabled
                        </label>
                      ) : <p className="text-xs text-muted-foreground">Plugin data is kept after removal.</p>}
                      <div className="flex items-center gap-2">
                        {!plugin.pendingRemoval && <Button variant="outline" size="sm" disabled={pluginsDisabled} onClick={() => setSelected(plugin)}>Configure</Button>}
                        <Button variant="ghost" size="sm" disabled={pluginsDisabled} onClick={() => void pluginAction(id, () => settingsRequest(`/plugins/${id}${plugin.pendingRemoval ? "/restore" : ""}`, { method: plugin.pendingRemoval ? "POST" : "DELETE" }))}>
                          {plugin.pendingRemoval ? "Undo removal" : "Remove"}
                        </Button>
                      </div>
                    </div>
                  </li>
                )
              })}
            </ul>
          )}
          <p className="text-xs leading-relaxed text-muted-foreground">Installations, updates, configuration, and enable or disable changes apply after restart. Install a newer ZIP to update a plugin. Removing a plugin keeps its data.</p>
        </section>
      )}
      {!page && <Outlet context={{ canReload: !dirty && !saving && !restarting }} />}
      {dirty && (
        <form onSubmit={save} className="sticky bottom-4 z-10 flex items-center justify-between gap-4 rounded-lg border border-sidebar-border bg-background px-4 py-3 shadow-sm">
          <p role="status" className="text-sm">You have unsaved preferences.</p>
          <div className="flex gap-2">
            <Button type="button" size="sm" variant="outline" disabled={saving} onClick={() => setDraft(saved!)}>Discard</Button>
            <Button size="sm" disabled={saving}>{saving ? "Saving…" : "Save changes"}</Button>
          </div>
        </form>
      )}
      {selected && <PluginConfigurationSheet key={selected.id} plugin={selected} onClose={() => setSelected(null)} onSaved={() => { void refreshPlugins().catch(error => setError(error.message)) }} />}
    </div>
  )
}
