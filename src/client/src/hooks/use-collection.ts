import { useState, useEffect, useCallback, useRef } from "react"
import { getApiUrl } from "../utils/api-config"

export interface CardImage {
  index: number
  name: string
  imageData: string // base64-encoded PNG
}

/**
 * Hook to stream rendered card images from the server.
 * This is the only API call needed - it streams card images with their names.
 * No need to call a separate endpoint for card list.
 */
export function useCardImageStream() {
  const [cards, setCards] = useState<CardImage[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const abortControllerRef = useRef<AbortController | null>(null)

  const startStream = useCallback(async () => {
    // Abort any existing stream first
    if (abortControllerRef.current) {
      abortControllerRef.current.abort()
    }

    abortControllerRef.current = new AbortController()
    const signal = abortControllerRef.current.signal

    setLoading(true)
    setError(null)
    setCards([])

    console.log("[useCardImageStream] Starting stream...")

    try {
      const response = await fetch(getApiUrl("/api/collection/cards/stream"), { signal })

      console.log("[useCardImageStream] Response status:", response.status)

      if (!response.ok) {
        // Try to get error details from JSON response
        let errorMsg = `HTTP ${response.status}`
        try {
          const errorData = await response.json()
          if (errorData.error) {
            errorMsg = errorData.error
            if (errorData.hint) {
              errorMsg += ` - ${errorData.hint}`
            }
          }
        } catch {
          // Couldn't parse JSON, use status-based message
          if (response.status === 503) {
            errorMsg = "MTGO client not ready. Please wait for it to initialize."
          } else if (response.status === 400) {
            errorMsg = "No cards could be rendered"
          }
        }
        throw new Error(errorMsg)
      }

      if (!response.body) {
        throw new Error("No response body")
      }

      const reader = response.body.getReader()
      const decoder = new TextDecoder()
      let buffer = ""

      while (true) {
        const { done, value } = await reader.read()

        if (done) {
          console.log("[useCardImageStream] Stream completed")
          break
        }

        buffer += decoder.decode(value, { stream: true })

        // Process complete NDJSON lines
        const lines = buffer.split("\n")
        buffer = lines.pop() || "" // Keep incomplete line in buffer

        for (const line of lines) {
          if (line.trim()) {
            try {
              const cardImage: CardImage = JSON.parse(line)
              console.log("[useCardImageStream] Received card:", cardImage.name)
              // Append each card as it arrives - this updates UI immediately
              setCards(prev => [...prev, cardImage])
            } catch (e) {
              console.error("Failed to parse card image:", e, "Line:", line)
            }
          }
        }
      }

      // Process any remaining buffer content
      if (buffer.trim()) {
        try {
          const cardImage: CardImage = JSON.parse(buffer)
          console.log("[useCardImageStream] Received final card:", cardImage.name)
          setCards(prev => [...prev, cardImage])
        } catch (e) {
          console.error("Failed to parse final card image:", e)
        }
      }
    } catch (err) {
      if (err instanceof Error && err.name === "AbortError") {
        console.log("[useCardImageStream] Stream aborted")
        // Stream was cancelled, not an error
        return
      }
      console.error("[useCardImageStream] Error:", err)
      setError(err instanceof Error ? err.message : "Unknown error")
    } finally {
      setLoading(false)
    }
  }, [])

  const stopStream = useCallback(() => {
    if (abortControllerRef.current) {
      abortControllerRef.current.abort()
      abortControllerRef.current = null
    }
    setLoading(false)
  }, [])

  const reset = useCallback(() => {
    stopStream()
    setCards([])
    setError(null)
  }, [stopStream])

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      if (abortControllerRef.current) {
        abortControllerRef.current.abort()
      }
    }
  }, [])

  return {
    cards,
    loading,
    error,
    startStream,
    stopStream,
    reset
  }
}

/**
 * Get a direct URL to a card image
 */
export function getCardImageUrl(cardName: string): string {
  return getApiUrl(`/api/collection/cards/${encodeURIComponent(cardName)}/image`)
}
