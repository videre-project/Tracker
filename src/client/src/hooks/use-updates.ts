/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { createContext, useContext } from "react"
import type { UpdateStatus } from "@/types/api"

export const UpdatesContext = createContext<{
  status: UpdateStatus | null
  error: string | null
  setStatus: (status: UpdateStatus) => void
} | null>(null)

export function useUpdates() {
  const context = useContext(UpdatesContext)
  if (!context) throw new Error("useUpdates requires an UpdatesProvider")
  return context
}
