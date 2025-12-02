import * as React from "react";
import { type RouteObject, createBrowserRouter } from "react-router-dom";

import {
  type LucideProps,
  CalendarClock,
  Settings,
  LibraryBig,
  BookCopy,
  ChartArea,
  History,
} from "lucide-react";

import Layout from "@/layout/LayoutContent";
import Events from "@/pages/events";
import Home from "@/pages/home";
import SettingsPage from "@/pages/settings";

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
        element: <DummyComponent />
      },
      {
        path: "/decks",
        name: "Decks",
        icon: BookCopy,
        type: NavType.Primary,
        element: <DummyComponent />
      },
      {
        path: "/events",
        name: "Events",
        icon: CalendarClock,
        type: NavType.Primary,
        element: <Events />
      },
      {
        path: "/history",
        name: "History",
        icon: History,
        type: NavType.Primary,
        element: <DummyComponent />
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