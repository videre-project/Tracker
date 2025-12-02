import { useState } from "react"
import { useNDJSONStream } from "./use-ndjson-stream"

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

/**
 * Watch the MTGO client connection state in real-time
 * Returns the current state and whether we're ready to make API calls
 */
export function useClientState() {
  const [state, setState] = useState<ClientState>({
    isConnected: false,
    isInitialized: false,
    status: "disconnected"
  })
  const [loading, setLoading] = useState(true)

  useNDJSONStream<ClientState>({
    url: "/api/Client/WatchState",
    onMessage: (update) => {
      setState({
        isConnected: update.isConnected,
        isInitialized: update.isInitialized,
        processId: update.processId,
        status: update.status,
        memoryUsage: update.memoryUsage,
        workingSet: update.workingSet,
        virtualMemory: update.virtualMemory
      })
      // Stop loading once we get any state (even while disconnected)
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
    useConstantRetry: true
  })

  return {
    state,
    loading,
    isReady: state.isInitialized && state.status === "ready"
  }
}
