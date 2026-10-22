/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { ArrowLeft } from "lucide-react"
import { Link, NavLink, useLocation } from "react-router-dom"
import {
  SidebarContent, SidebarGroup, SidebarGroupLabel, SidebarMenu,
  SidebarMenuButton, SidebarMenuItem
} from "@videreproject/ui"

import { developerPages, settingsPages } from "@/lib/settings-pages"

export function SettingsNavigation() {
  const { pathname } = useLocation()
  return (
    <SidebarContent>
      <SidebarGroup>
        <SidebarMenu>
          <SidebarMenuItem>
            <SidebarMenuButton asChild tooltip="Back to Tracker">
              <Link to="/"><ArrowLeft /><span>Back to Tracker</span></Link>
            </SidebarMenuButton>
          </SidebarMenuItem>
        </SidebarMenu>
      </SidebarGroup>
      {[
        { title: "Settings", pages: settingsPages },
        { title: "Developer", pages: developerPages },
      ].map(group => (
        <SidebarGroup key={group.title}>
          <SidebarGroupLabel>{group.title}</SidebarGroupLabel>
          <SidebarMenu>
            {group.pages.map(({ path, title, icon: Icon }) => (
              <SidebarMenuItem key={path}>
                <SidebarMenuButton asChild isActive={pathname === path} tooltip={title}>
                  <NavLink to={path} end><Icon /><span>{title}</span></NavLink>
                </SidebarMenuButton>
              </SidebarMenuItem>
            ))}
          </SidebarMenu>
        </SidebarGroup>
      ))}
    </SidebarContent>
  )
}
