/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { createContext, useContext } from "react"
import type { SettingsResponse } from "@/types/api"
import type { Preferences } from "@/lib/application-preferences"

export const ApplicationSettingsContext = createContext<{
  preferences: Preferences
  settings: SettingsResponse | null
  error: string | null
  setSettings: (value: SettingsResponse) => void
  refresh: (signal?: AbortSignal) => Promise<SettingsResponse>
} | null>(null)

export function useApplicationSettings() {
  const context = useContext(ApplicationSettingsContext)
  if (!context) throw new Error("useApplicationSettings requires an ApplicationSettingsProvider")
  return context
}
