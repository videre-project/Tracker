import { useState } from "react"
import { useNDJSONStream } from "./use-ndjson-stream"

export interface LogEntry {
  timestamp: string
  source: "SDK" | "Tracker" | "Diver"
  level: string
  logger: string
  message: string
  messageId?: number | null
}

const MAX_HISTORY = 5000

export function useLogsStream() {
  const [logs, setLogs] = useState<LogEntry[]>([])

  useNDJSONStream<LogEntry>({
    url: "/api/Diagnostics/WatchLogs",
    onMessage: (entry) => {
      setLogs((prev) => {
        const entryTime = new Date(entry.timestamp).getTime()

        // Fast path: entry is newer than the last — just append
        if (prev.length === 0 || entryTime >= new Date(prev[prev.length - 1].timestamp).getTime()) {
          const next = [...prev, entry]
          return next.length > MAX_HISTORY ? next.slice(-MAX_HISTORY) : next
        }

        // Backdated entry: binary-search for the correct position
        let lo = 0, hi = prev.length
        while (lo < hi) {
          const mid = (lo + hi) >>> 1
          if (new Date(prev[mid].timestamp).getTime() <= entryTime) lo = mid + 1
          else hi = mid
        }
        const next = [...prev.slice(0, lo), entry, ...prev.slice(lo)]
        return next.length > MAX_HISTORY ? next.slice(-MAX_HISTORY) : next
      })
    },
    onEnd: () => {
      // Clear on disconnect so reconnection doesn't duplicate the buffer
      setLogs([])
    },
    autoReconnect: true,
    reconnectDelay: 2000,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })

  return { logs }
}
