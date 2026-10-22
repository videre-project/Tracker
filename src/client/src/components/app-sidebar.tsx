/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


import * as React from "react"
import { useApplicationSettings } from "@/hooks/use-application-settings"
import { useLocation } from "react-router-dom"
import { SettingsNavigation } from "@/components/settings-navigation"

import { NavMain } from "@/components/nav-main"
import { NavFooter } from "@/components/nav-footer"
import { NavLogo } from "./nav-logo"
import { ActivityList } from "@/components/game-list"
import { usePersonalActivity } from "@/hooks/use-personal-activity"
import {
  Sidebar,
  SidebarFooter,
  SidebarHeader,
  SidebarRail,
} from "@videreproject/ui"
import { routes, NavType } from "@/router"

const data = {
  label: {
    title: "Videre Tracker",
    subtitle: `v${__APP_VERSION__}`,
    logoUrl: "/logo.png",
  },
}

export function AppSidebar({ ...props }: React.ComponentProps<typeof Sidebar>) {
  const navMain = React.useMemo(
    () => {
      const allRoutes = routes.flatMap(r => r.children || []);
      return allRoutes
        .filter((r) => r.type === NavType.Primary && !!r.path && !!r.name)
        .map((r) => ({
          title: r.name as string,
          url: r.path as string,
          icon: r.icon,
          disabled: false,
        }));
    },
    []
  )

  const navFooter = React.useMemo(
    () => {
      const allRoutes = routes.flatMap(r => r.children || []);
      return allRoutes
        .filter((r) => r.type === NavType.Footer && !!r.path && !!r.name)
        .map((r) => ({
          title: r.name as string,
          url: r.path as string,
          icon: r.icon,
          disabled: false,
        }));
    },
    []
  )

  const { preferences } = useApplicationSettings();
  const { pathname } = useLocation();
  const settingsOpen = pathname === "/settings" || pathname.startsWith("/settings/");
  const activity = usePersonalActivity(!settingsOpen && (preferences.showActiveEvents || preferences.showUpcomingEvents));

  return (
    <Sidebar collapsible="icon" {...props}>
      <SidebarHeader className="p-0">
        <div className="p-2 pb-0">
          <NavLogo label={data.label} />
        </div>
        {!settingsOpen && (
          <div className="-mt-2">
            <NavMain items={navMain} />
          </div>
        )}
      </SidebarHeader>
      {settingsOpen ? <SettingsNavigation /> : (
        <>
          <div className="flex flex-col flex-1 overflow-y-hidden gap-0 -mt-2 mb-2">
            {preferences.showActiveEvents && <ActivityList label="Active" items={activity.active}
              loading={activity.loading} error={activity.error} connected={activity.isReady}
              emptyMessage="No active matches or events." />}
            {preferences.showUpcomingEvents && <ActivityList label="Registered" items={activity.registered}
              loading={activity.loading} error={activity.error} connected={activity.isReady}
              emptyMessage="No registered events." />}
          </div>
          <SidebarFooter className="pt-1 pb-4">
            <NavFooter items={navFooter} />
          </SidebarFooter>
        </>
      )}
      <SidebarRail />
    </Sidebar>
  )
}
