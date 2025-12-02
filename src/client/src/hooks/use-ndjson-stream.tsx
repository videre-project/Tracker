import { useEffect, useRef, useCallback } from "react"

export interface NDJSONStreamOptions<T> {
  /**
   * The URL to fetch the NDJSON stream from
   */
  url: string

  /**
   * Callback invoked for each parsed line
   */
  onMessage: (data: T) => void

  /**
   * Callback invoked when the stream ends (not an error)
   */
  onEnd?: () => void

  /**
   * Callback invoked when an error occurs
   */
  onError?: (error: Error) => void

  /**
   * Whether to automatically reconnect when the stream closes
   * @default true
   */
  autoReconnect?: boolean

  /**
   * Delay in milliseconds before attempting to reconnect
   * @default 2000
   */
  reconnectDelay?: number

  /**
   * Maximum delay for exponential backoff (in milliseconds)
   * @default 30000 (30 seconds)
   */
  maxReconnectDelay?: number

  /**
   * Maximum number of reconnection attempts (0 = infinite)
   * @default 10
   */
  maxReconnectAttempts?: number

  /**
   * Whether the stream should be active (allows external control)
   * @default true
   */
  enabled?: boolean

  /**
   * Use constant retry delay instead of exponential backoff
   * Useful for critical streams that need frequent checks (e.g., client state monitoring)
   * @default false
   */
  useConstantRetry?: boolean
}

/**
 * Hook for consuming NDJSON (Newline Delimited JSON) streams with automatic reconnection.
 *
 * This hook provides robust handling of server-sent NDJSON streams, including:
 * - Automatic reconnection when streams close unexpectedly
 * - Handling of 503 Service Unavailable responses (e.g., when MTGO client disconnects)
 * - Exponential backoff to prevent connection spam
 * - Configurable reconnection delays and retry limits
 * - Proper cleanup on unmount
 * - React StrictMode compatibility
 *
 * @example
 * ```tsx
 * // Simple usage
 * useNDJSONStream({
 *   url: '/api/Events/WatchTournamentUpdates',
 *   onMessage: (update) => console.log('Update:', update)
 * })
 *
 * // Critical stream that should never give up (e.g., client state monitoring)
 * useNDJSONStream({
 *   url: '/api/Client/WatchState',
 *   onMessage: (state) => setState(state),
 *   autoReconnect: true,
 *   reconnectDelay: 3000, // Check every 3 seconds
 *   maxReconnectAttempts: 0, // Infinite retries - keep trying forever
 *   useConstantRetry: true // No exponential backoff - keep checking frequently
 * })
 *
 * // Non-critical stream with retry limit
 * useNDJSONStream({
 *   url: '/api/Events/WatchPlayerCount',
 *   onMessage: (update) => handleUpdate(update),
 *   onError: (err) => console.error('Stream error:', err),
 *   onEnd: () => console.log('Stream ended'),
 *   autoReconnect: true,
 *   reconnectDelay: 2000,
 *   maxReconnectDelay: 30000,
 *   maxReconnectAttempts: 10 // Give up after 10 attempts
 * })
 *
 * // Conditional streaming
 * useNDJSONStream({
 *   url: '/api/Events/WatchPlayerCount',
 *   onMessage: handleUpdate,
 *   enabled: isClientReady // Only stream when client is ready
 * })
 * ```
 */
export function useNDJSONStream<T = any>(options: NDJSONStreamOptions<T>) {
  const {
    url,
    onMessage,
    onEnd,
    onError,
    autoReconnect = true,
    reconnectDelay = 2000,
    maxReconnectDelay = 30000,
    maxReconnectAttempts = 10,
    enabled = true,
    useConstantRetry = false
  } = options

  const reconnectAttemptsRef = useRef(0)
  const abortControllerRef = useRef<AbortController | null>(null)
  const reconnectTimeoutRef = useRef<NodeJS.Timeout | null>(null)
  const initializedRef = useRef(false)
  const isConnectingRef = useRef(false) // Prevent concurrent connection attempts
  const connectFnRef = useRef<() => void>()

  // Store callbacks in refs to avoid recreating connect function
  const onMessageRef = useRef(onMessage)
  const onEndRef = useRef(onEnd)
  const onErrorRef = useRef(onError)

  // Update refs when callbacks change
  useEffect(() => {
    onMessageRef.current = onMessage
    onEndRef.current = onEnd
    onErrorRef.current = onError
  }, [onMessage, onEnd, onError])

  const scheduleReconnect = useCallback(() => {
    // Check if we've exceeded max reconnect attempts
    if (maxReconnectAttempts > 0 && reconnectAttemptsRef.current >= maxReconnectAttempts) {
      console.error(`Max reconnect attempts (${maxReconnectAttempts}) reached for ${url}`)
      onErrorRef.current?.(new Error(`Failed to connect after ${maxReconnectAttempts} attempts`))
      return
    }

    reconnectAttemptsRef.current++

    // Calculate delay: use constant retry or exponential backoff
    const delay = useConstantRetry
      ? reconnectDelay // Constant delay for frequent checks
      : Math.min(
          reconnectDelay * Math.pow(2, reconnectAttemptsRef.current - 1),
          maxReconnectDelay
        )

    const retryMode = useConstantRetry ? 'constant' : 'exponential backoff'
    console.log(
      `Scheduling reconnect (${retryMode}) attempt ${reconnectAttemptsRef.current}/${maxReconnectAttempts || '∞'} in ${delay}ms for ${url}`
    )

    // Clear any existing reconnect timeout
    if (reconnectTimeoutRef.current) {
      clearTimeout(reconnectTimeoutRef.current)
    }

    reconnectTimeoutRef.current = setTimeout(() => {
      connectFnRef.current?.()
    }, delay)
  }, [reconnectDelay, maxReconnectDelay, maxReconnectAttempts, url, useConstantRetry])

  const connectToStream = useCallback(() => {
    if (!enabled) return

    // Prevent concurrent connection attempts
    if (isConnectingRef.current) {
      console.log(`Already connecting to ${url}, skipping duplicate attempt`)
      return
    }

    isConnectingRef.current = true

    // Clean up any existing connection
    if (abortControllerRef.current) {
      abortControllerRef.current.abort()
    }

    const controller = new AbortController()
    abortControllerRef.current = controller

    console.log(`Connecting to NDJSON stream: ${url}`)

    fetch(url, {
      signal: controller.signal,
      headers: { Accept: "application/x-ndjson" }
    })
      .then(res => {
        // Handle 503 Service Unavailable (client not ready)
        if (res.status === 503) {
          console.warn(`Stream not available (503), scheduling retry`)
          isConnectingRef.current = false
          scheduleReconnect()
          return
        }

        if (!res.ok) {
          isConnectingRef.current = false
          throw new Error(`Stream request failed: ${res.status} ${res.statusText}`)
        }

        console.log(`Connected to NDJSON stream: ${url}`)

        // Reset reconnect attempts on successful connection
        reconnectAttemptsRef.current = 0
        isConnectingRef.current = false

        const reader = res.body?.getReader()
        if (!reader) {
          throw new Error("No stream reader available")
        }

        let buffer = ""

        function readStream(): Promise<void> {
          return reader.read().then(({ done, value }) => {
            if (done) {
              console.log(`Stream ended: ${url}`)
              onEndRef.current?.()

              // Reconnect if enabled and not manually aborted
              if (autoReconnect && !controller.signal.aborted) {
                scheduleReconnect()
              }
              return
            }

            buffer += new TextDecoder().decode(value)
            let lines = buffer.split("\n")
            buffer = lines.pop() || ""

            for (const line of lines) {
              if (!line.trim()) continue
              try {
                const data = JSON.parse(line) as T
                onMessageRef.current(data)
              } catch (e) {
                console.error("Failed to parse NDJSON line:", e, line)
              }
            }

            return readStream()
          })
        }

        return readStream()
      })
      .catch(e => {
        isConnectingRef.current = false

        if (e.name === 'AbortError') {
          console.log(`Stream aborted: ${url}`)
          return
        }

        console.error(`Stream error: ${url}`, e)
        onErrorRef.current?.(e)

        // Reconnect if enabled
        if (autoReconnect && !controller.signal.aborted) {
          scheduleReconnect()
        }
      })
  }, [url, autoReconnect, enabled, scheduleReconnect])

  // Update the connect function ref whenever connectToStream changes
  useEffect(() => {
    connectFnRef.current = connectToStream
  }, [connectToStream])

  useEffect(() => {
    if (!enabled) {
      // Clean up when disabled
      isConnectingRef.current = false

      if (abortControllerRef.current) {
        abortControllerRef.current.abort()
        abortControllerRef.current = null
      }

      if (reconnectTimeoutRef.current) {
        clearTimeout(reconnectTimeoutRef.current)
        reconnectTimeoutRef.current = null
      }

      return
    }

    // Only connect once per enable cycle
    if (initializedRef.current) return
    initializedRef.current = true

    connectFnRef.current?.()

    return () => {
      initializedRef.current = false
      isConnectingRef.current = false

      // Clean up connection
      if (abortControllerRef.current) {
        abortControllerRef.current.abort()
        abortControllerRef.current = null
      }

      // Clear reconnect timeout
      if (reconnectTimeoutRef.current) {
        clearTimeout(reconnectTimeoutRef.current)
        reconnectTimeoutRef.current = null
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [enabled]) // Only re-run when `enabled` state changes

  return {
    reconnect: connectToStream
  }
}
