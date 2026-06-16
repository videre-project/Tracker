import { useState, useRef } from "react"
import { useNDJSONStream } from "./use-ndjson-stream"
import { useClientState } from "./use-client-state"

export interface EndpointMetric {
  count: number
  active?: number
  avgMs: number
  lastMs: number
}

export interface StaThreadStats {
  totalOps: number
  dispatcherOps: number
  pendingOps: number
  avgDispatchMs: number
  timeouts: number
}

export interface SdkMetrics {
  syncThreadActive: number
  syncThreadQueued: number
  syncThreadMax: number
  inFlightRequests: number
  totalRequests: number
  endpoints: Record<string, EndpointMetric>
  callbacksReceived: number
  lastCallbackLatencyMs: number
  avgCallbackLatencyMs: number
  peakCallbackLatencyMs: number
}

export interface HostProcessStats {
  gcGen0Collections: number
  gcGen1Collections: number
  gcGen2Collections: number
  gcTotalMemory: number
  pinnedObjects: number
  dispatcherResponsivenessMs: number
  gen0HeapSize: number
  gen1HeapSize: number
  gen2HeapSize: number
  lohSize: number
}

export interface DiverMetrics {
  syncThreadActive: number
  syncThreadQueued: number
  syncThreadMax: number
  activeHooks: number
  activeEventSubscriptions: number
  connectedClients: number
  endpoints: Record<string, EndpointMetric>
  callbacksSent: number
  lastCallbackQueueDelayMs: number
  staThread: StaThreadStats
  hostProcess: HostProcessStats | null
}

export interface TrackerMetrics {
  endpoints: Record<string, EndpointMetric>
  streams?: Record<string, {
    active: number
    opened: number
    closed: number
    dropped: number
    coalesced: number
  }>
}

export interface DiagnosticsSnapshot {
  timestamp: string
  sdk: SdkMetrics | null
  diver: DiverMetrics | null
  tracker: TrackerMetrics | null
}

export interface EndpointDelta {
  endpoint: string
  count: number    // requests in this interval
  avgMs: number    // current cumulative avg (best we have)
}

export interface RateSnapshot {
  timestamp: string
  requestsPerSec: number
  callbacksPerSec: number
  diverCallbacksPerSec: number
  /** Top SDK endpoints by request count in this interval */
  topEndpoints: EndpointDelta[]
  sdk: SdkMetrics | null
  diver: DiverMetrics | null
  tracker: TrackerMetrics | null
}

const MAX_HISTORY = 120 // 60s at 500ms interval
const POLL_INTERVAL_SEC = 0.5

export function useDiagnostics() {
  const { isReady } = useClientState()
  const [current, setCurrent] = useState<DiagnosticsSnapshot | null>(null)
  const [history, setHistory] = useState<RateSnapshot[]>([])
  const prevRef = useRef<DiagnosticsSnapshot | null>(null)

  useNDJSONStream<DiagnosticsSnapshot>({
    url: "/api/Diagnostics/WatchMetrics",
    onMessage: (snapshot) => {
      setCurrent(snapshot)

      // Compute per-interval rates from consecutive snapshot deltas
      const prev = prevRef.current
      let requestsPerSec = 0
      let callbacksPerSec = 0
      let diverCallbacksPerSec = 0
      let topEndpoints: EndpointDelta[] = []
      if (prev?.sdk && snapshot.sdk) {
        const reqDelta = snapshot.sdk.totalRequests - prev.sdk.totalRequests
        const cbDelta =
          snapshot.sdk.callbacksReceived - prev.sdk.callbacksReceived
        requestsPerSec = Math.max(0, reqDelta / POLL_INTERVAL_SEC)
        callbacksPerSec = Math.max(0, cbDelta / POLL_INTERVAL_SEC)

        // Diff per-endpoint counts to find what's active right now
        const prevEps = prev.sdk.endpoints ?? {}
        const curEps = snapshot.sdk.endpoints ?? {}
        const deltas: EndpointDelta[] = []
        for (const [ep, cur] of Object.entries(curEps)) {
          if (ep.includes("diagnostics")) continue
          const prevCount = prevEps[ep]?.count ?? 0
          const delta = cur.count - prevCount
          if (delta > 0) {
            deltas.push({ endpoint: ep, count: delta, avgMs: cur.avgMs })
          }
        }
        topEndpoints = deltas
          .sort((a, b) => b.count - a.count)
          .slice(0, 5)
      }
      if (prev?.diver && snapshot.diver) {
        const diverCbDelta = snapshot.diver.callbacksSent - prev.diver.callbacksSent
        diverCallbacksPerSec = Math.max(0, diverCbDelta / POLL_INTERVAL_SEC)
      }
      prevRef.current = snapshot

      setHistory(prev => {
        const entry: RateSnapshot = {
          timestamp: snapshot.timestamp,
          requestsPerSec,
          callbacksPerSec,
          diverCallbacksPerSec,
          topEndpoints,
          sdk: snapshot.sdk,
          diver: snapshot.diver,
          tracker: snapshot.tracker,
        }
        const next = [...prev, entry]
        return next.length > MAX_HISTORY ? next.slice(-MAX_HISTORY) : next
      })
    },
    enabled: isReady,
    autoReconnect: true,
    reconnectDelay: 2000,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })

  return { current, history }
}
