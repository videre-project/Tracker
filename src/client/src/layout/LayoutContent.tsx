import React, { Suspense } from "react"
import { Outlet, useLocation } from "react-router-dom";

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
                <BreadcrumbLink href={url}>{title}</BreadcrumbLink>
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

  // Extract the breadcrumbs for each segment of the path
  const breadcrumbs = fragments.reduce((acc, fragment) => {
    const route = allRoutes.find(({ path }) => path === fragment);
    if (route) {
      acc.push({ title: route.name ?? "", url: route.path! });
      if (route.children) {
        const child = route.children.find(({ index }) => index) as RouteEntry;
        if (child && child.path) {
          acc.push({ title: child.name ?? "", url: child.path! });
        }
      }
    }
    return acc;
  }, [] as RouteFragment[]);

  return (
    <ThemeProvider defaultTheme="system" storageKey="vite-ui-theme">
      <EventsProvider>
        <SidebarProvider>
          <AppSidebar />
          <SidebarInset>
            <header className="flex h-14 shrink-0 items-center gap-2 transition-[width,height] ease-linear">
              <div className="flex items-center gap-2 px-4">
                <SidebarTrigger className="-ml-1" />
                <Separator orientation="vertical" className="mr-2 h-4" />
                <RouteBreadcrumb items={breadcrumbs} />
              </div>
            </header>
            <div className="flex-1 overflow-y-auto">
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
