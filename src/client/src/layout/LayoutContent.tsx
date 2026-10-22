/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import React, { Suspense } from "react"
import { ExternalLink } from "lucide-react"
import { Link, Outlet, useLocation, matchPath } from "react-router-dom";

import { cn } from "@/lib/utils"
import { useDeckDetail } from "@/hooks/use-decks"
import { DeckManaSymbols } from "@videreproject/ui"
import { UpdatesProvider } from "@/components/updates-provider"
import { AppSidebar } from "@/components/app-sidebar"
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
  Separator,
  SidebarInset,
  SidebarProvider,
  SidebarTrigger,
  Skeleton,
} from "@videreproject/ui"

import { routes } from "@/router";
import { EventsProvider } from "@/hooks/use-events";
import { getApiUrl } from "@/utils/api-config"

interface RouteFragment {
  title: string
  url: string
  noLink?: boolean
  contextId?: string
  colors?: string[]
  kind?: "collection" | "format" | "page"
}

function normalizeDeckFormat(value?: string) {
  const trimmed = value?.trim()
  if (!trimmed) return undefined
  if (!/[a-z]/.test(trimmed)) return trimmed
  return trimmed
    .toLowerCase()
    .split(/\s+/)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ")
}

function BreadcrumbLabel({ title, contextId, colors }: Pick<RouteFragment, "title" | "contextId" | "colors">) {
  return <span className="inline-flex items-center gap-2.5">{title}
    {colors && <DeckManaSymbols colors={colors} className="translate-y-[2px]" />}
    {contextId && <span id={contextId} className="inline-flex items-center empty:hidden" />}
  </span>
}

function RouteBreadcrumb({
  currentHref,
  items,
}: {
  currentHref?: string
  items: RouteFragment[]
}) {
  const { pathname } = useLocation()
  return (
    <Breadcrumb>
      <BreadcrumbList>
        {items.map(({ title, url, noLink, contextId, colors }: RouteFragment, index) => (
          index < items.length - 1
            ? (<React.Fragment key={`React.Fragment-${contextId ?? index}`}>
              <BreadcrumbItem className="hidden md:block"
                key={`BreadcrumbItem-${contextId ?? index}`}>
                {noLink ? (
                  <span className="text-sm font-medium text-muted-foreground"><BreadcrumbLabel title={title} contextId={contextId} colors={colors} /></span>
                ) : (
                  <BreadcrumbLink asChild><Link to={url}><BreadcrumbLabel title={title} contextId={contextId} colors={colors} /></Link></BreadcrumbLink>
                )}
              </BreadcrumbItem>
              <BreadcrumbSeparator className="hidden md:block"
                key={`BreadcrumbSeparator-${index}`} />
            </React.Fragment>)
            : (<BreadcrumbItem key={`BreadcrumbItem-${contextId ?? index}`}>
              {currentHref ? (
                <BreadcrumbLink
                  aria-current="page"
                  className="inline-flex items-center gap-1.5 text-foreground"
                  href={currentHref}
                  target="_blank"
                  rel="noreferrer"
                >
                  <BreadcrumbLabel title={title} contextId={contextId} colors={colors} />
                  <ExternalLink className="h-3.5 w-3.5 translate-y-px" />
                </BreadcrumbLink>
              ) : url !== pathname ? (
                <BreadcrumbLink asChild><Link to={url}><BreadcrumbLabel title={title} contextId={contextId} colors={colors} /></Link></BreadcrumbLink>
              ) : (
                <BreadcrumbPage><BreadcrumbLabel title={title} contextId={contextId} colors={colors} /></BreadcrumbPage>
              )}
            </BreadcrumbItem>)
        ))}
        <BreadcrumbItem className="hidden md:block">
          <div id="page-header-context" className="inline-flex items-center self-center pt-px" />
        </BreadcrumbItem>
      </BreadcrumbList>
    </Breadcrumb>
  )
}

export default function Layout() {
  // Extract the current location and each segment of the path
  const location = useLocation();
  const fragments = location.pathname.split(/(?=\/)/);
  const deckRouteMatch = matchPath("/decks/:deckRevisionId", location.pathname);
  const routeDeckRevisionId = deckRouteMatch?.params.deckRevisionId;
  const inferredDeckRevisionId = routeDeckRevisionId
    ? routeDeckRevisionId
    : location.pathname.match(/^\/decks\/([^/?#]+)/)?.[1];

  const deckRevisionId = (() => {
    if (!inferredDeckRevisionId) return undefined;
    try {
      return decodeURIComponent(inferredDeckRevisionId);
    } catch {
      return inferredDeckRevisionId;
    }
  })();
  const deck = useDeckDetail(deckRevisionId ?? null);
  const currentDeckBreadcrumbName = deck.detail?.name;
  const currentDeckBreadcrumbFormat = deck.detail?.format;

  // Routes are now nested as children under main routes
  const allRoutes = routes.flatMap(r => r.children || [])
    .flatMap(route => [route, ...(route.children || [])]);

  const deckBreadcrumbs = React.useMemo<RouteFragment[]>(() => {
    if (!deckRevisionId) return [];

    const formatTitle = currentDeckBreadcrumbFormat
      ? normalizeDeckFormat(currentDeckBreadcrumbFormat)
      : undefined;

    const deckTitle = currentDeckBreadcrumbName || "Deck";

    const breadcrumbs: RouteFragment[] = [
      { title: "Decks", url: "/decks", kind: "collection" },
    ];

    if (formatTitle) {
      breadcrumbs.push({
        title: formatTitle,
        url: `/decks?format=${encodeURIComponent(formatTitle)}`,
        kind: "format",
      });
    }

    breadcrumbs.push({
      title: deckTitle,
      url: `/decks/${deckRevisionId}`,
      contextId: "deck-breadcrumb-context",
      colors: deck.detail?.colors ?? undefined,
      kind: "page",
    });

    return breadcrumbs;
  }, [deckRevisionId, currentDeckBreadcrumbFormat, currentDeckBreadcrumbName, deck.detail?.colors]);

  // Extract the breadcrumbs for each segment of the path, building
  // cumulative paths so that /settings/diagnostics resolves correctly.
  const routesBySpecificity = React.useMemo(() => (
    [...allRoutes].sort((a, b) => (b.path?.length ?? 0) - (a.path?.length ?? 0))
  ), [allRoutes])

  const breadcrumbs = deckRevisionId ? deckBreadcrumbs : fragments.reduce((acc, _fragment, index) => {
    const cumulativePath = fragments.slice(0, index + 1).join("");
    const route = routesBySpecificity.find(({ path }) => {
      if (!path) return false;
      return matchPath(path, cumulativePath) !== null;
    });
    if (route) {
      const match = route.path ? matchPath(route.path, cumulativePath) : null;
      if (route.path === "/decks") {
        acc.push({
          title: "Decks",
          url: cumulativePath,
          kind: "collection",
        });
      } else if (route.path === "/decks/:deckRevisionId" &&
                 match?.params.deckRevisionId) {
        const title = currentDeckBreadcrumbName ?? route.name ?? "";
        if (currentDeckBreadcrumbFormat) {
          const normalizedFormat = normalizeDeckFormat(currentDeckBreadcrumbFormat) ?? currentDeckBreadcrumbFormat;
          acc.push({
            title: normalizedFormat,
            url: `/decks?format=${encodeURIComponent(normalizedFormat)}`,
            kind: "format",
          });
        }
        acc.push({
          title,
          url: cumulativePath,
          kind: "page",
        });
      } else if (route.path === "/history/:matchId/game/:gameId/replay" && match?.params.gameId) {
        acc.push({
          title: `Game #${match.params.gameId}`,
          url: cumulativePath,
          kind: "page",
        });
      } else if (match?.params) {
        let title = route.name ?? "";
        const paramValue = Object.values(match.params)[0];
        if (paramValue) title = `${title} #${paramValue}`;
        acc.push({ title, url: cumulativePath, kind: "page" });
      } else if (route.name) {
        acc.push({
          title: route.name,
          url: cumulativePath,
          kind: "page",
        });
      }
    }
    return acc;
  }, [] as RouteFragment[]);
  const isMatchDetailsPage = Boolean(matchPath("/history/:matchId", location.pathname));
  const displayBreadcrumbs = breadcrumbs.map(item =>
    item.url === "/decks" && item.kind === "collection"
      ? { ...item, title: "Decks" }
      : item
  );
  const isTournamentPage = Boolean(matchPath("/events/:eventId", location.pathname));
  const isApiDocsPage = location.pathname === "/settings/api-docs";
  const currentBreadcrumbHref = isApiDocsPage ? getApiUrl("/docs") : undefined;

  return (
    <UpdatesProvider>
    <EventsProvider liveUpdates={Boolean(matchPath("/events/*", location.pathname))}>
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
              <div className="inline-flex items-center rounded-sm">
                <RouteBreadcrumb currentHref={currentBreadcrumbHref} items={displayBreadcrumbs} />
              </div>
              <div
                id="page-header-context"
                className="ml-5 hidden items-center self-center pt-px md:flex"
              />
            </div>
            <div
              id="page-header-end"
              className="pointer-events-auto absolute right-4 top-0 hidden h-full items-center md:flex"
            />
            </header>
            <div className={cn("flex-1 min-h-0 overflow-y-auto", (isTournamentPage || isMatchDetailsPage || location.pathname === "/collection" || location.pathname === "/history") && "flex flex-col overflow-hidden")}>
              <div className="h-14 shrink-0" />
              <Suspense fallback={<div role="status" aria-label="Loading page"
                className="flex min-h-0 flex-1 flex-col gap-4 px-4 pb-4 pt-1">
                <Skeleton className="h-8 w-64 shrink-0" />
                <Skeleton className="h-64 w-full" />
              </div>}>
                <Outlet context={deck} />
              </Suspense>
            </div>
          </SidebarInset>
      </SidebarProvider>
    </EventsProvider>
    </UpdatesProvider>
  )
}
