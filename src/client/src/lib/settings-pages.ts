/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { Monitor, Plug, Settings2, History, SlidersHorizontal, Layers, Play, FolderOpen, Download, BookOpen, Bug, FlaskConical } from "lucide-react"

export const settingsPages = [
  { path: "/settings", title: "Overview", icon: Settings2,
    description: "Make Tracker work the way you play." },
  { path: "/settings/general", title: "General", icon: SlidersHorizontal,
    description: "Set your launch page, sidebar, and browsing defaults." },
  { path: "/settings/appearance", title: "Appearance", icon: Monitor,
    description: "Choose how Tracker looks and opens on your desktop." },
  { path: "/settings/tracking", title: "Tracking and synchronization", icon: History,
    description: "Choose what Tracker records and keeps in sync with MTGO." },
  { path: "/settings/decks", title: "Deck editor", icon: Layers,
    description: "Choose the starting layout for editing your decks." },
  { path: "/settings/replay", title: "Replays", icon: Play,
    description: "Adjust how you navigate and view recorded games." },
  { path: "/settings/storage", title: "Data and folders", icon: FolderOpen,
    description: "Find Tracker's recordings, preferences, and plugin files." },
  { path: "/settings/updates", title: "Updates", icon: Download,
    description: "Check for new Tracker releases and install downloaded updates." },
  { path: "/settings/plugins", title: "Plugins", icon: Plug,
    description: "Extend Tracker with plugins and manage their configuration." },
]

export const developerPages = [
  { path: "/settings/api-docs", title: "API reference", icon: BookOpen,
    description: "Explore Tracker's local API and plugin endpoints." },
  { path: "/settings/diagnostics", title: "Diagnostics", icon: Bug,
    description: "Inspect connection health, resource usage, and application logs." },
  ...(import.meta.env.DEV ? [
    { path: "/settings/development", title: "Development", icon: FlaskConical,
      description: "Preview application errors and test recovery." },
  ] : []),
]
