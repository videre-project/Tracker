/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { createContext } from "react"

export type ClientStatus = "disconnected" | "connecting" | "ready"

export interface ClientState {
  isConnected: boolean
  isInitialized: boolean
  processId?: number
  status: ClientStatus
  memoryUsage?: number
  workingSet?: number
  virtualMemory?: number
}

export interface ClientStateContextValue {
  state: ClientState
  loading: boolean
  isReady: boolean
}

export const ClientStateContext = createContext<ClientStateContextValue | undefined>(undefined)
