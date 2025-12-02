import * as React from "react"

import { NavMain } from "@/components/nav-main"
import { NavFooter } from "@/components/nav-footer"
import { NavLogo } from "./nav-logo"
import { ActiveGames, UpcomingGames } from "@/components/game-list"
import { useEvents } from "@/hooks/use-events"
import {
  Sidebar,
  SidebarFooter,
  SidebarHeader,
  SidebarRail,
} from "@/components/ui/sidebar"
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
        }));
    },
    []
  )

  const { activeGames, upcomingGames } = useEvents();
  const activeEmpty = activeGames.length === 0;
  const upcomingEmpty = upcomingGames.length === 0;

  return (
    <Sidebar collapsible="icon" {...props}>
      <SidebarHeader className="p-0">
        <div className="p-2 pb-0">
          <NavLogo label={data.label} />
        </div>
        <div className="-mt-2">
          <NavMain items={navMain} />
        </div>
      </SidebarHeader>
      <div className="flex flex-col flex-1 overflow-y-hidden gap-0 -mt-2 mb-2">
        <ActiveGames games={activeGames} otherListEmpty={upcomingEmpty} />
        <UpcomingGames games={upcomingGames} className="-mt-1" otherListEmpty={activeEmpty} />
      </div>
      <SidebarFooter className="pt-1 pb-4">
        <NavFooter items={navFooter} />
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>
  )
}
