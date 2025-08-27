import * as React from "react"
import { Frame, Map, PieChart } from "lucide-react"

import { NavMain } from "@/components/nav-main"
import { NavFooter } from "@/components/nav-footer"
import { NavUser } from "@/components/nav-user"
import { NavLogo } from "./nav-logo"
import { ActiveGames, UpcomingGames } from "@/components/game-list"
import {
  Sidebar,
  SidebarFooter,
  SidebarHeader,
  SidebarRail,
} from "@/components/ui/sidebar"
import { routes, NavType } from "@/router"

// Static bits for header/footer
const data = {
  label: {
    title: "Videre Tracker",
    subtitle: `v${__APP_VERSION__}`,
    logoUrl: "/logo.png",
  },
}

export function AppSidebar({ ...props }: React.ComponentProps<typeof Sidebar>) {
  // Build sidebar items from route config (Primary nav only)
  const navMain = React.useMemo(
    () =>
      routes
        .filter((r) => r.type === NavType.Primary && !!r.path && !!r.name)
        .map((r) => ({
          title: r.name as string,
          url: r.path as string,
          icon: r.icon,
        })),
    []
  )

  const navFooter = React.useMemo(
    () =>
      routes
        .filter((r) => r.type === NavType.Footer && !!r.path && !!r.name)
        .map((r) => ({
          title: r.name as string,
          url: r.path as string,
          icon: r.icon,
        })),
    []
  )

  return (
    <Sidebar collapsible="icon" {...props}>
      <SidebarHeader className="p-0">
        <div className="p-2 pb-0">
          <NavLogo label={data.label} />
        </div>
        <NavMain items={navMain} />
      </SidebarHeader>
      <div className="flex flex-col flex-1 overflow-y-hidden gap-[2px]">
        <ActiveGames />
        <UpcomingGames />
      </div>
      <SidebarFooter>
        <NavFooter items={navFooter} />
      </SidebarFooter>
      <SidebarRail />
    </Sidebar>
  )
}
