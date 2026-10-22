/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import * as React from "react"
import { Link } from "react-router-dom"
import { useUpdates } from "@/hooks/use-updates"
import {
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
  Tooltip, TooltipContent, TooltipTrigger,
} from "@videreproject/ui"

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
  const { status } = useUpdates();
  const version = status?.currentVersion ? `v${status.currentVersion.replace(/^v/i, "")}` : label.subtitle;
  const update = status?.stagedVersion || status?.availableVersion;
  const description = status?.scheduled
    ? `${update} will install after MTGO closes`
    : status?.stagedVersion ? `${update} is ready to install`
    : update ? `${update} is available` : "Check for application updates";
  return (
    <SidebarMenu>
      <SidebarMenuItem>
        <SidebarMenuButton
          asChild
          size="lg"
          className="text-sidebar-foreground cursor-default hover:bg-transparent active:bg-transparent pl-0 pr-2 group-data-[collapsible=icon]:!h-12 select-none"
        >
          <div>
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
              <Tooltip>
                <TooltipTrigger asChild>
                  <Link to="/settings/updates" aria-label={`${version}: ${description}`} className="flex w-fit items-center gap-1.5 rounded-sm text-xs text-sidebar-foreground/70 hover:text-sidebar-foreground focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring">
                    <span>{version}</span>
                    {update && <span aria-hidden="true" data-update-indicator className="h-1.5 w-1.5 shrink-0 rounded-full bg-sky-400" />}
                  </Link>
                </TooltipTrigger>
                <TooltipContent side="right">{description}</TooltipContent>
              </Tooltip>
            </div>
          </div>
        </SidebarMenuButton>
      </SidebarMenuItem>
    </SidebarMenu>
  );
}
