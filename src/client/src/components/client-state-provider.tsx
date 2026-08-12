/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useState, type ReactNode } from "react"
import { ClientStateContext, type ClientState } from "@/hooks/client-state-context"
import { useNDJSONStream } from "@/hooks/use-ndjson-stream"
import { getApiUrl } from "@/utils/api-config"

export function ClientStateProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<ClientState>({
    isConnected: false,
    isInitialized: false,
    status: "disconnected",
  })
  const [loading, setLoading] = useState(true)

  useNDJSONStream<ClientState>({
    url: getApiUrl("/api/Client/WatchState"),
    onMessage: (update) => {
      setState(update)
      setLoading(false)
    },
    onError: (error) => {
      console.error("Client state stream error:", error)
      setLoading(false)
    },
    onEnd: () => {
      console.log("Client state stream ended - connection lost, will reconnect")
    },
    autoReconnect: true,
    reconnectDelay: 500,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })

  return (
    <ClientStateContext.Provider value={{
      state,
      loading,
      isReady: state.isInitialized && state.status === "ready",
    }}>
      {children}
    </ClientStateContext.Provider>
  )
}
