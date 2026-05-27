import React, { Suspense } from "react"
import { Link, Outlet, useLocation, matchPath } from "react-router-dom";

import { cn } from "@/lib/utils"
import { AppSidebar } from "@/components/app-sidebar"
import { ThemeProvider } from "@/components/theme-provider"
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb"
import { Separator } from "@/components/ui/separator"
import {
  SidebarInset,
  SidebarProvider,
  SidebarTrigger,
} from "@/components/ui/sidebar"

import { RouteEntry, routes } from "@/router";
import { EventsProvider } from "@/hooks/use-events";

interface RouteFragment {
  title: string
  url: string
}

function RouteBreadcrumb({ items }: { items: RouteFragment[] }) {
  return (
    <Breadcrumb>
      <BreadcrumbList>
        {items.map(({ title, url }: RouteFragment, index) => (
          index < items.length - 1
            ? (<React.Fragment key={`React.Fragment-${index}`}>
              <BreadcrumbItem className="hidden md:block"
                key={`BreadcrumbItem-${index}`}>
                <BreadcrumbLink asChild><Link to={url}>{title}</Link></BreadcrumbLink>
              </BreadcrumbItem>
              <BreadcrumbSeparator className="hidden md:block"
                key={`BreadcrumbSeparator-${index}`} />
            </React.Fragment>)
            : (<BreadcrumbItem key={`BreadcrumbItem-${index}`}>
              <BreadcrumbPage>{title}</BreadcrumbPage>
            </BreadcrumbItem>)
        ))}
      </BreadcrumbList>
    </Breadcrumb>
  )
}

export default function Layout() {
  // Extract the current location and each segment of the path
  const location = useLocation();
  const fragments = location.pathname.split(/(?=\/)/);

  // Routes are now nested as children under main routes
  const allRoutes = routes.flatMap(r => r.children || []);

  // Extract the breadcrumbs for each segment of the path, building
  // cumulative paths so that /settings/diagnostics resolves correctly.
  const breadcrumbs = fragments.reduce((acc, fragment) => {
    const cumulativePath = (acc.at(-1)?.url ?? "") + fragment;
    const route = allRoutes.find(({ path }) => {
      if (!path) return false;
      return matchPath(path, cumulativePath) !== null;
    });
    if (route) {
      let title = route.name ?? "";
      const match = route.path ? matchPath(route.path, cumulativePath) : null;
      if (match?.params) {
        const paramValue = Object.values(match.params)[0];
        if (paramValue) title = `${title} #${paramValue}`;
      }
      acc.push({ title, url: cumulativePath });
    }
    return acc;
  }, [] as RouteFragment[]);

  return (
    <ThemeProvider defaultTheme="system" storageKey="vite-ui-theme">
      <EventsProvider>
        <SidebarProvider>
          <AppSidebar />
          <SidebarInset className="min-w-0 relative">
            <header className={cn("absolute top-0 left-0 right-0 z-30 h-14 pointer-events-none", location.pathname === "/events" ? "" : "bg-background")}>
              {location.pathname === "/events" && (
                <div
                  className="absolute top-3 left-0 h-8"
                  style={{
                    width: 200,
                    background: 'linear-gradient(to right, hsl(var(--background)) 120px, transparent)',
                  }}
                />
              )}
              <div className="relative h-full flex items-center gap-2 px-4 pointer-events-auto w-fit">
                <SidebarTrigger className="-ml-1" />
                <Separator orientation="vertical" className="mr-2 h-4" />
                <RouteBreadcrumb items={breadcrumbs} />
              </div>
            </header>
            <div className="flex-1 min-h-0 overflow-y-auto">
              <div className="h-14" />
              <Suspense>
                <Outlet />
              </Suspense>
            </div>
          </SidebarInset>
        </SidebarProvider>
      </EventsProvider>
    </ThemeProvider>
  )
}
