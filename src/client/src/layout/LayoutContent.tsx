import React, { Suspense } from "react"
import { ExternalLink } from "lucide-react"
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

import { routes } from "@/router";
import { EventsProvider } from "@/hooks/use-events";
import { getApiUrl } from "@/utils/api-config"

interface RouteFragment {
  title: string
  url: string
  noLink?: boolean
  kind?: "collection" | "format" | "page"
}

interface DeckRouteState {
  deckName?: string
  deckFormat?: string
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

function RouteBreadcrumb({
  currentHref,
  items,
}: {
  currentHref?: string
  items: RouteFragment[]
}) {
  return (
    <Breadcrumb>
      <BreadcrumbList>
        {items.map(({ title, url, noLink }: RouteFragment, index) => (
          index < items.length - 1
            ? (<React.Fragment key={`React.Fragment-${index}`}>
              <BreadcrumbItem className="hidden md:block"
                key={`BreadcrumbItem-${index}`}>
                {noLink ? (
                  <span className="text-sm font-medium text-muted-foreground">{title}</span>
                ) : (
                  <BreadcrumbLink asChild><Link to={url}>{title}</Link></BreadcrumbLink>
                )}
              </BreadcrumbItem>
              <BreadcrumbSeparator className="hidden md:block"
                key={`BreadcrumbSeparator-${index}`} />
            </React.Fragment>)
            : (<BreadcrumbItem key={`BreadcrumbItem-${index}`}>
              {currentHref ? (
                <BreadcrumbLink
                  aria-current="page"
                  className="inline-flex items-center gap-1.5 text-foreground"
                  href={currentHref}
                  target="_blank"
                  rel="noreferrer"
                >
                  {title}
                  <ExternalLink className="h-3.5 w-3.5 translate-y-px" />
                </BreadcrumbLink>
              ) : (
                <BreadcrumbPage>{title}</BreadcrumbPage>
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
  const routeState = location.state as DeckRouteState | null;
  const [deckBreadcrumbNames, setDeckBreadcrumbNames] = React.useState<Record<string, string>>({});
  const [deckBreadcrumbFormats, setDeckBreadcrumbFormats] = React.useState<Record<string, string>>({});
  const currentDeckBreadcrumbName = deckRevisionId
    ? routeState?.deckName ?? deckBreadcrumbNames[deckRevisionId]
    : undefined;
  const currentDeckBreadcrumbFormat = deckRevisionId
    ? routeState?.deckFormat ?? deckBreadcrumbFormats[deckRevisionId]
    : undefined;

  React.useEffect(() => {
    if (!deckRevisionId) return;

    if (routeState?.deckName) {
      setDeckBreadcrumbNames(current => (
        current[deckRevisionId] === routeState.deckName
          ? current
          : { ...current, [deckRevisionId]: routeState.deckName! }
      ));
    }

    if (routeState?.deckFormat) {
      setDeckBreadcrumbFormats(current => (
        current[deckRevisionId] === routeState.deckFormat
          ? current
          : { ...current, [deckRevisionId]: routeState.deckFormat! }
      ));
    }

    if (routeState?.deckName && routeState?.deckFormat) {
      return;
    }

    if (deckBreadcrumbNames[deckRevisionId] &&
        deckBreadcrumbFormats[deckRevisionId]) return;

    const abortController = new AbortController();

    fetch(getApiUrl("/api/decks"), { signal: abortController.signal })
      .then(response => {
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        return response.json() as Promise<Record<string, {
          revisionId: number
          name: string
          format: string
        }[]>>;
      })
      .then(decksByFormat => {
        const flattenedDecks = Object.values(decksByFormat).flat();
        const deck = flattenedDecks.find(
          candidate => candidate.revisionId.toString() === deckRevisionId
        );
        if (!deck) throw new Error("Deck not found in deck index");

        if (deck.name) {
          setDeckBreadcrumbNames(current => ({
            ...current,
            [deckRevisionId]: deck.name,
          }));
        }
        if (deck.format) {
          setDeckBreadcrumbFormats(current => ({
            ...current,
            [deckRevisionId]: deck.format,
          }));
        }
      })
      .catch(error => {
        if (error instanceof Error && error.name === "AbortError") return;
        fetch(getApiUrl("/api/decks/identifiers"), { signal: abortController.signal })
          .then(response => {
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            return response.json() as Promise<Array<{
              revisionId: number
              name: string
              format: string
            }>>;
          })
          .then(decks => {
            const deck = decks.find(
              item => item.revisionId.toString() === deckRevisionId
            );
            if (!deck) return;

            setDeckBreadcrumbNames(current => ({
              ...current,
              [deckRevisionId]: deck.name,
            }));
            if (deck.format) {
              setDeckBreadcrumbFormats(current => ({
                ...current,
                [deckRevisionId]: deck.format,
              }));
            }
          })
          .catch(_ => { });
      });

    return () => abortController.abort();
  }, [deckBreadcrumbNames, deckBreadcrumbFormats, deckRevisionId, routeState?.deckName, routeState?.deckFormat]);

  // Routes are now nested as children under main routes
  const allRoutes = routes.flatMap(r => r.children || []);

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
      kind: "page",
    });

    return breadcrumbs;
  }, [deckRevisionId, currentDeckBreadcrumbFormat, currentDeckBreadcrumbName]);

  const replayBreadcrumb = React.useMemo<RouteFragment | null>(() => {
    const match = matchPath("/history/:matchId/game/:gameId/replay", location.pathname);
    if (!match?.params.gameId) return null;

    return {
      title: `Game #${match.params.gameId}`,
      url: location.pathname,
      kind: "page",
    };
  }, [location.pathname]);

  // Extract the breadcrumbs for each segment of the path, building
  // cumulative paths so that /settings/diagnostics resolves correctly.
  const routesBySpecificity = React.useMemo(() => (
    [...allRoutes].sort((a, b) => (b.path?.length ?? 0) - (a.path?.length ?? 0))
  ), [allRoutes])

  const breadcrumbs = deckRevisionId ? deckBreadcrumbs : fragments.reduce((acc, fragment) => {
    const cumulativePath = (acc.at(-1)?.url ?? "") + fragment;
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
          const normalizedFormat = normalizeDeckFormat(currentDeckBreadcrumbFormat);
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
  const breadcrumbsWithReplay = replayBreadcrumb
    ? [...breadcrumbs, replayBreadcrumb]
    : breadcrumbs;
  const displayBreadcrumbs = breadcrumbs.map(item =>
    item.url === "/decks" && item.kind === "collection"
      ? { ...item, title: "Decks" }
      : item
  );
  const displayBreadcrumbsWithReplay = breadcrumbsWithReplay.map(item =>
    item.url === "/decks" && item.kind === "collection"
      ? { ...item, title: "Decks" }
      : item
  );
  const isApiDocsPage = location.pathname === "/settings/api-docs";
  const currentBreadcrumbHref = isApiDocsPage ? getApiUrl("/docs") : undefined;

  return (
    <ThemeProvider defaultTheme="dark" storageKey="vite-ui-theme">
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
              <div className="inline-flex items-center rounded-sm">
                <RouteBreadcrumb currentHref={currentBreadcrumbHref} items={displayBreadcrumbsWithReplay} />
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
