/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { createContext, useContext } from "react"
import type { ActiveGame } from "./use-events"

export interface EventsContextValue {
  activeGames: ActiveGame[]
  upcomingGames: ActiveGame[]
  completedGames: ActiveGame[]
  loading: boolean
  error: string | null
  hoveredEventId: string | null
  setHoveredEventId: (id: string | null) => void
  selectedEventId: string | null
  setSelectedEventId: (id: string | null) => void
}

export const EventsContext = createContext<EventsContextValue | null>(null)

export function useEvents(): EventsContextValue {
  const context = useContext(EventsContext)
  if (!context) {
    throw new Error("useEvents must be used within an EventsProvider")
  }
  return context
}
