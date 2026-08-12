/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useContext } from "react"
import { ClientStateContext } from "./client-state-context"

export type { ClientState, ClientStatus } from "./client-state-context"

export function useClientState() {
  const context = useContext(ClientStateContext)
  if (context === undefined) {
    throw new Error("useClientState must be used within a ClientStateProvider")
  }
  return context
}
