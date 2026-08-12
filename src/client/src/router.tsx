/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import * as React from "react";
import { type RouteObject, createBrowserRouter } from "react-router-dom";

import {
  type LucideProps,
  Bug,
  CalendarClock,
  FileJson,
  Settings,
  LibraryBig,
  BookCopy,
  ChartArea,
  History as HistoryIcon,
  Package,
} from "lucide-react";

import Layout from "@/layout/LayoutContent";

const Events = React.lazy(() => import('@/pages/events'))
const EventDetails = React.lazy(() => import('@/pages/event-details'))
const Home = React.lazy(() => import('@/pages/home'))
const SettingsPage = React.lazy(() => import('@/pages/settings'))
const ApiDocs = React.lazy(() => import('@/pages/api-docs'))
const Diagnostics = React.lazy(() => import('@/pages/diagnostics'))
const Decks = React.lazy(() => import('@/pages/decks'))
const DeckEditor = React.lazy(() => import('@/pages/deck-editor'))
const Collection = React.lazy(() => import('@/pages/collection'))
const History = React.lazy(() => import('@/pages/history'))
const MatchDetails = React.lazy(() => import('@/pages/match-details'))
const GameLog = React.lazy(() => import('@/pages/game-log'))
const GameReplay = React.lazy(() => import('@/pages/game-replay'))
const Trades = React.lazy(() => import('@/pages/trades'))

export enum NavType {
  Primary,
  Secondary,
  Footer
}

type LucideIcon = React.ForwardRefExoticComponent<Omit<LucideProps, "ref"> &
  React.RefAttributes<SVGSVGElement>>;

export type RouteEntry = RouteObject & {
  name?: string;
  icon?: LucideIcon;
  type?: NavType;
  children?: RouteEntry[];
}

export const routes: RouteEntry[] = [
  {
    path: "/",
    element: <Layout />,
    children: [
      {
        path: "/",
        name: "Dashboard",
        icon: ChartArea,
        type: NavType.Primary,
        index: true,
        element: <Home />
      },
      {
        path: "/collection",
        name: "Collection",
        icon: LibraryBig,
        type: NavType.Primary,
        element: <Collection />
      },
      {
        path: '/trades',
        name: 'Trades',
        icon: Package,
        type: NavType.Primary,
        element: <Trades />
      },
      {
        path: "/decks",
        name: "Decks",
        icon: BookCopy,
        type: NavType.Primary,
        element: <Decks />
      },
      {
        path: "/decks/:deckRevisionId",
        name: "Deck",
        type: NavType.Secondary,
        element: <DeckEditor />
      },
      {
        path: "/events",
        name: "Events",
        icon: CalendarClock,
        type: NavType.Primary,
        element: <Events />
      },
      {
        path: "/events/:eventId",
        name: "Tournament",
        type: NavType.Secondary,
        element: <EventDetails />
      },
      {
        path: "/history",
        name: "History",
        icon: HistoryIcon,
        type: NavType.Primary,
        element: <History />
      },
      {
        path: "/history/:matchId",
        name: "Match",
        type: NavType.Secondary,
        element: <MatchDetails />
      },
      {
        path: "/history/:matchId/watch",
        name: "Game Log",
        type: NavType.Secondary,
        element: <GameLog />
      },
      {
        path: "/history/:matchId/game/:gameId/replay",
        name: "Game Replay",
        type: NavType.Secondary,
        element: <GameReplay />
      },
      {
        path: "/settings/api-docs",
        name: "API Docs",
        icon: FileJson,
        type: NavType.Secondary,
        element: <ApiDocs />
      },
      {
        path: "/settings/diagnostics",
        name: "Diagnostics",
        icon: Bug,
        type: NavType.Secondary,
        element: <Diagnostics />
      },
      {
        path: "/settings",
        name: "Settings",
        icon: Settings,
        type: NavType.Footer,
        element: <SettingsPage />
      }
    ]
  }
];

const router = createBrowserRouter(routes);

export default router;
