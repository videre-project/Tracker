/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import type { DeckSortMode } from "@videreproject/ui"
import type { ApplicationSettings } from "@/types/api"

export type Theme = "dark" | "light" | "system"

export type Preferences = Omit<Required<ApplicationSettings>, "theme" | "deckSortMode"> & {
  theme: Theme
  deckSortMode: DeckSortMode
}

export const defaultPreferences: Preferences = {
  checkForUpdates: true, theme: "dark", useCustomTitleBar: true, startMaximized: false,
  startupPage: "/", showActiveEvents: true, showUpcomingEvents: true,
  historyRangeDays: 30, historyPageSize: 50,
  deckSortMode: "cmc", showSideboard: false, showDeckTools: true,
  replayFollowLive: true, replayKeyboardShortcuts: true, replayExpandOpponentZones: true,
  recordReplays: true, recordTrades: true, synchronizeDecks: true,
}

export function preferences(value?: ApplicationSettings): Preferences {
  return { ...defaultPreferences, ...value } as Preferences
}

export function initialDateRange(days: number) {
  if (days === 0) return undefined
  const to = new Date()
  const from = new Date(to)
  from.setDate(to.getDate() - days)
  return { from, to }
}
