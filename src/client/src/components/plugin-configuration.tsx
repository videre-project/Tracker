/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useEffect, useState } from "react"
import {
  Button, Sheet, SheetContent, SheetDescription, SheetHeader, SheetTitle,
  Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Skeleton
} from "@videreproject/ui"
import type { ConfigurationField, PluginConfiguration, PluginInfo } from "@/types/api"
import { settingsRequest } from "@/hooks/use-settings"

type Values = Record<string, unknown>
const supportedTypes = new Set(["string", "boolean", "number", "integer"])

export function PluginConfigurationSheet({ plugin, onClose, onSaved }: {
  plugin: PluginInfo
  onClose: () => void
  onSaved: () => void
}) {
  const [configuration, setConfiguration] = useState<PluginConfiguration | null>(null)
  const [values, setValues] = useState<Values>({})
  const [json, setJson] = useState("")
  const [error, setError] = useState("")
  const [saving, setSaving] = useState(false)
  const fields = configuration?.fields ?? []
  const useFields = fields.length > 0 && fields.every(field => supportedTypes.has(field.type ?? ""))
  const url = `/plugins/${encodeURIComponent(plugin.id ?? "")}/configuration`

  useEffect(() => {
    const controller = new AbortController()
    settingsRequest<PluginConfiguration>(url, { signal: controller.signal })
      .then(data => {
        const initial = { ...data.values }
        setConfiguration(data)
        setValues(initial)
        setJson(JSON.stringify(initial, null, 2))
      })
      .catch(error => { if (!controller.signal.aborted) setError(error.message) })
    return () => controller.abort()
  }, [url])

  function update(field: ConfigurationField, value: unknown) {
    setValues(current => {
      const next = { ...current }
      if (value === undefined) delete next[field.key ?? ""]
      else next[field.key ?? ""] = value
      return next
    })
  }

  async function save(event: React.FormEvent) {
    event.preventDefault()
    setError("")
    setSaving(true)
    try {
      const body = useFields ? values : JSON.parse(json)
      if (body == null || Array.isArray(body) || typeof body !== "object")
        throw new Error("Configuration must be a JSON object.")
      await settingsRequest(url, { method: "PUT", body: JSON.stringify(body) })
      onSaved()
      onClose()
    } catch (error) {
      setError(error instanceof Error ? error.message : "Could not save configuration.")
    } finally { setSaving(false) }
  }

  return (
    <Sheet open onOpenChange={open => { if (!open && !saving) onClose() }}>
      <SheetContent className="flex flex-col gap-5 overflow-y-auto sm:max-w-xl">
        <SheetHeader>
          <SheetTitle>Configure {plugin.name}</SheetTitle>
          <SheetDescription>Configuration changes apply after restarting Tracker.</SheetDescription>
        </SheetHeader>
        <form onSubmit={save}>
          <fieldset disabled={saving} className="space-y-4">
            {error && <p role="alert" className="text-sm text-red-400">{error}</p>}
            {!configuration ? (!error && <Skeleton className="h-24" />) : useFields ? fields.map(field => {
              const id = `plugin-setting-${field.key}`
              const value = values[field.key ?? ""]
              const choices = field.choices ?? []
              return (
                <div key={field.key} className="space-y-1.5">
                  <label htmlFor={id} className="flex items-center justify-between gap-4 text-sm font-medium">
                    {field.label}
                    {field.type === "boolean" && <input id={id} aria-describedby={field.description ? `${id}-description` : undefined} type="checkbox" checked={value === true} onChange={event => update(field, event.target.checked)} className="h-4 w-4 accent-primary" />}
                  </label>
                  {field.description && <p id={`${id}-description`} className="text-xs text-muted-foreground">{field.description}</p>}
                  {field.type !== "boolean" && (choices.length ? (
                    <Select value={value === undefined ? "" : JSON.stringify(value)} onValueChange={choice => update(field, JSON.parse(choice))}>
                      <SelectTrigger id={id} aria-describedby={field.description ? `${id}-description` : undefined} className="h-8"><SelectValue placeholder="Select a value" /></SelectTrigger>
                      <SelectContent>{choices.map(choice => <SelectItem key={JSON.stringify(choice)} value={JSON.stringify(choice)}>{String(choice)}</SelectItem>)}</SelectContent>
                    </Select>
                  ) : (
                    <Input id={id} aria-describedby={field.description ? `${id}-description` : undefined} required={field.required}
                      type={field.secret ? "password" : field.type === "string" ? "text" : "number"}
                      step={field.type === "integer" ? 1 : "any"}
                      autoComplete={field.secret ? "new-password" : "off"}
                      value={typeof value === "string" || typeof value === "number" ? value : ""}
                      onChange={event => update(field, event.target.value === "" ? undefined : field.type === "string" ? event.target.value : Number(event.target.value))}
                      className="h-8 text-sm" />
                  ))}
                </div>
              )
            }) : (
              <div className="space-y-2">
                <label htmlFor="plugin-json" className="text-sm font-medium">Configuration</label>
                <p className="text-xs text-muted-foreground">This plugin accepts a JSON configuration object. Refer to its documentation for supported options.</p>
                <textarea id="plugin-json" value={json} onChange={event => setJson(event.target.value)} spellCheck={false}
                  className="min-h-48 w-full rounded-md border border-input bg-transparent p-3 font-mono text-xs focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring" />
              </div>
            )}
            <div className="flex justify-end gap-2">
              <Button type="button" variant="outline" size="sm" onClick={onClose} disabled={saving}>Cancel</Button>
              <Button size="sm" disabled={saving || !configuration}>{saving ? "Saving…" : "Save configuration"}</Button>
            </div>
          </fieldset>
        </form>
      </SheetContent>
    </Sheet>
  )
}
