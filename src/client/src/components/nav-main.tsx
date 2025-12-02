"use client"

import { ChevronRight, type LucideIcon } from "lucide-react"
import { NavLink, useLocation } from "react-router-dom"

import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from "@/components/ui/collapsible"
import {
  SidebarGroup,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  SidebarMenuSub,
  SidebarMenuSubButton,
  SidebarMenuSubItem,
} from "@/components/ui/sidebar"

export type NavMenuItem = {
  title: string
  url: string
  icon?: LucideIcon
  isActive?: boolean
  disabled?: boolean
  items?: {
    title: string
    url: string
  }[]
}

export function NavMain({
  items,
}: {
  items: NavMenuItem[]
}) {
  const location = useLocation()

  const isPathActive = (baseUrl?: string) => {
    if (!baseUrl) return false
    // Exact match or path starts with base (for nested routes)
    // with special case for root
    if (baseUrl === "/") return location.pathname === "/"
    return (
      location.pathname === baseUrl ||
      location.pathname.startsWith(baseUrl + "/")
    )
  }

  return (
    <SidebarGroup>
      <SidebarMenu>
        {items.map((item) => {
          const hasChildren = (item.items?.length ?? 0) > 0
          const activeInGroup = hasChildren
            ? item.items!.some((s) => location.pathname === s.url)
            : isPathActive(item.url)

          if (!hasChildren) {
            return (
              <SidebarMenuItem key={item.title}>
                <SidebarMenuButton asChild isActive={activeInGroup} tooltip={item.title} aria-disabled={item.disabled} disabled={item.disabled}>
                  <NavLink to={item.url}>
                    {item.icon && <item.icon />}
                    <span>{item.title}</span>
                  </NavLink>
                </SidebarMenuButton>
              </SidebarMenuItem>
            )
          }

          return (
            <Collapsible
              key={item.title}
              asChild
              defaultOpen={activeInGroup}
              className="group/collapsible"
            >
              <SidebarMenuItem>
                <CollapsibleTrigger asChild>
                  <SidebarMenuButton tooltip={item.title}>
                    {item.icon && <item.icon />}
                    <span>{item.title}</span>
                    <ChevronRight className="ml-auto transition-transform duration-200 group-data-[state=open]/collapsible:rotate-90" />
                  </SidebarMenuButton>
                </CollapsibleTrigger>
                <CollapsibleContent>
                  <SidebarMenuSub>
                    {item.items?.map((subItem) => (
                      <SidebarMenuSubItem key={subItem.title}>
                        <SidebarMenuSubButton asChild isActive={location.pathname === subItem.url}>
                          <NavLink to={subItem.url}>
                            <span>{subItem.title}</span>
                          </NavLink>
                        </SidebarMenuSubButton>
                      </SidebarMenuSubItem>
                    ))}
                  </SidebarMenuSub>
                </CollapsibleContent>
              </SidebarMenuItem>
            </Collapsible>
          )
        })}
      </SidebarMenu>
    </SidebarGroup>
  )
}
