import * as React from "react"
import {
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/components/ui/sidebar"

type NavLogoLabel = {
  title: string;
  subtitle: string;
  logo?: React.ElementType;
  logoUrl?: string;
};

interface NavLogoProps {
  label: NavLogoLabel;
}

export function NavLogo({ label }: NavLogoProps) {
  if (!label) return null;
  return (
    <SidebarMenu>
      <SidebarMenuItem>
        <SidebarMenuButton
          size="lg"
          className="text-sidebar-foreground cursor-default pointer-events-none focus:outline-none focus:ring-0 pl-0 pr-2 group-data-[collapsible=icon]:!h-12 select-none"
          tabIndex={-1}
        >
          <div className="flex aspect-square size-8 items-center justify-center rounded-lg border border-sidebar-border/60 mr-1 select-none" style={{ backgroundColor: 'color-mix(in srgb, hsl(var(--sidebar-background)) 50%, black)' }}>
            {label.logoUrl ? (
              <img
                src={label.logoUrl}
                alt={label.title}
                className="size-6 rounded object-contain mt-px select-none pointer-events-none"
              />
            ) : label.logo ? (
              <label.logo className="size-4 mt-px select-none" />
            ) : null}
          </div>
          <div className="grid flex-1 text-left text-sm leading-tight select-none">
            <span className="truncate font-semibold text-sidebar-foreground select-none">
              {label.title}
            </span>
            <span className="truncate text-xs text-sidebar-foreground/70 select-none">{label.subtitle}</span>
          </div>
        </SidebarMenuButton>
      </SidebarMenuItem>
    </SidebarMenu>
  );
}
