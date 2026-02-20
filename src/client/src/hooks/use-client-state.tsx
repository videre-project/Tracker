import { useState, createContext, useContext, ReactNode } from "react"
import { useNDJSONStream } from "./use-ndjson-stream"
import { getApiUrl } from "../utils/api-config"

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

interface ClientStateContextType {
  state: ClientState
  loading: boolean
  isReady: boolean
}

const ClientStateContext = createContext<ClientStateContextType | undefined>(undefined)

/**
 * Provider to manage MTGO client state globally across navigations.
 */
export function ClientStateProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<ClientState>({
    isConnected: false,
    isInitialized: false,
    status: "disconnected"
  })
  const [loading, setLoading] = useState(true)

  useNDJSONStream<ClientState>({
    url: getApiUrl("/api/Client/WatchState"),
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

  return (
    <ClientStateContext.Provider value={{
      state,
      loading,
      isReady: state.isInitialized && state.status === "ready"
    }}>
      {children}
    </ClientStateContext.Provider>
  )
}

/**
 * Watch the MTGO client connection state in real-time
 * Consumes the global ClientStateProvider context.
 */
export function useClientState() {
  const context = useContext(ClientStateContext)
  if (context === undefined) {
    throw new Error("useClientState must be used within a ClientStateProvider")
  }
  return context
}
