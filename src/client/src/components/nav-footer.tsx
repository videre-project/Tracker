import * as React from "react";
import { NavLink, useLocation } from "react-router-dom";
import {
  SidebarGroup,
  SidebarGroupContent,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  useSidebar,
} from "@/components/ui/sidebar";
import { type NavMenuItem } from "./nav-main";

export function NavFooter(props: {
  items: NavMenuItem[]
} & React.ComponentPropsWithoutRef<typeof SidebarGroup>) {
  const { items, ...rest } = props;
  const location = useLocation();
  const { state } = useSidebar();

  return (
    <SidebarGroup {...rest} className="-p-2 mt-0">
      <SidebarGroupContent>
        <SidebarMenu>
          {items.map((item) => (
            <SidebarMenuItem key={item.title}>
              <SidebarMenuButton asChild
                tooltip={item.title} isActive={location.pathname === item.url}>
                <NavLink to={item.url}>
                  {item.icon && <item.icon />}
                  <span className={state === "collapsed" ? "sr-only" : undefined}>{item.title}</span>
                </NavLink>
              </SidebarMenuButton>
            </SidebarMenuItem>
          ))}
        </SidebarMenu>
      </SidebarGroupContent>
    </SidebarGroup>
  );
}
