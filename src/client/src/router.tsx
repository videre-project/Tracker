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
import Events from "@/pages/events";
import EventDetails from "@/pages/event-details";
import Home from "@/pages/home";
import SettingsPage from "@/pages/settings";
import ApiDocs from "@/pages/api-docs";
import Diagnostics from "@/pages/diagnostics";
import Decks from "@/pages/decks";
import DeckEditor from "@/pages/deck-editor";
import Collection from "@/pages/collection";
import History from "@/pages/history";
import MatchDetails from "@/pages/match-details";
import GameLog from "@/pages/game-log";
import GameReplay from "@/pages/game-replay";
import Trades from "@/pages/trades";

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

export function DummyComponent() {
  return (
    <div className="flex flex-1 flex-col gap-4 p-4 pt-0">
      <div className="grid auto-rows-min gap-4 md:grid-cols-3">
        <div className="aspect-video rounded-xl bg-muted/50" />
        <div className="aspect-video rounded-xl bg-muted/50" />
        <div className="aspect-video rounded-xl bg-muted/50" />
      </div>
      <div className="min-h-[100vh] flex-1 rounded-xl bg-muted/50
                      md:min-h-min" />
    </div>
  )
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
