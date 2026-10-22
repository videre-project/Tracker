/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { Link } from "react-router-dom"
import { ChevronRight, Plug, Download } from "lucide-react"
import { Button, Card, CardDescription, CardHeader, CardTitle } from "@videreproject/ui"
import type { PluginInstallation } from "@/types/api"

import { pluginStatus } from "@/lib/plugin-status"
import { developerPages, settingsPages } from "@/lib/settings-pages"

export function SettingsOverview({ plugins, hostEnabled = true }: {
  plugins: PluginInstallation[] | null
  hostEnabled?: boolean
}) {
  return (
    <>
      <section aria-labelledby="preferences-heading" className="space-y-4">
        <h2 id="preferences-heading" className="text-sm font-medium">Preferences</h2>
        <div className="grid grid-cols-[repeat(auto-fit,minmax(min(100%,25rem),1fr))] gap-4">
          {settingsPages.filter(page => page.path !== "/settings" && page.path !== "/settings/plugins" && page.path !== "/settings/updates").map(({ title, icon: Icon, path, description }) => (
            <Link key={path} to={path} className="group rounded-lg focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring">
              <Card className="h-full border-sidebar-border/60 transition-colors group-hover:bg-muted/30">
                <CardHeader className="space-y-3 p-4 pb-3">
                  <CardTitle className="flex items-center gap-2 text-sm font-medium">
                    <Icon className="h-4 w-4 text-muted-foreground" />{title}
                    <ChevronRight className="ml-auto h-4 w-4 text-muted-foreground" />
                  </CardTitle>
                  <CardDescription className="text-sm leading-relaxed">{description}</CardDescription>
                </CardHeader>
              </Card>
            </Link>
          ))}
        </div>
      </section>
      <Link to="/settings/updates" className="flex items-center gap-3 rounded-md border border-sidebar-border/60 px-4 py-3 text-sm hover:bg-muted/30">
        <Download className="h-4 w-4 text-muted-foreground" />
        <span>Application updates</span>
        <ChevronRight className="ml-auto h-4 w-4 text-muted-foreground" />
      </Link>
      <section aria-labelledby="plugins-heading" className="space-y-4 border-b border-sidebar-border/60 pb-6">
        <div className="flex items-center justify-between gap-4">
          <h2 id="plugins-heading" className="text-sm font-medium">Plugins</h2>
          <Button asChild variant="ghost" size="sm">
            <Link to="/settings/plugins">Manage plugins<ChevronRight className="ml-2 h-4 w-4" /></Link>
          </Button>
        </div>
        <p className="text-sm text-muted-foreground">Add integrations and tools to Tracker. Install a plugin package, then configure it for your setup.</p>
        {plugins == null ? <p className="text-sm text-muted-foreground">Open Plugins to check installed packages.</p> : plugins.length ? (
          <ul className="divide-y divide-sidebar-border/60">
            {plugins.map(plugin => (
              <li key={plugin.id}>
                <Link to="/settings/plugins" className="flex items-center justify-between gap-4 rounded-md px-2 py-3 hover:bg-muted/30">
                  <div className="flex min-w-0 items-center gap-3">
                    <Plug className="h-4 w-4 shrink-0 text-muted-foreground" />
                    <div className="space-y-1">
                      <p className="text-sm font-medium">{plugin.name}</p>
                      {plugin.description && <p className="text-xs text-muted-foreground">{plugin.description}</p>}
                    </div>
                  </div>
                  <span className="shrink-0 text-xs text-muted-foreground">{pluginStatus(plugin, hostEnabled)}</span>
                </Link>
              </li>
            ))}
          </ul>
        ) : <p className="text-sm text-muted-foreground">No plugins installed. Open Plugins to install your first package.</p>}
      </section>
      <section aria-labelledby="developer-heading" className="space-y-4">
        <h2 id="developer-heading" className="text-sm font-medium">Developer tools</h2>
        <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          {developerPages.map(({ title, path, icon: Icon, description }) => (
            <Link key={path} to={path} className="flex items-start gap-3 rounded-md p-2 hover:bg-muted/30">
              <Icon className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" />
              <div className="space-y-1">
                <p className="text-sm font-medium">{title}</p>
                <p className="text-xs leading-relaxed text-muted-foreground">{description}</p>
              </div>
              <ChevronRight className="ml-auto mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" />
            </Link>
          ))}
        </div>
      </section>
    </>
  )
}
