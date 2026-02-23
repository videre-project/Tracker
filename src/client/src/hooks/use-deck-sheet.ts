import { useState, useCallback, useRef } from "react"
import { getApiUrl } from "../utils/api-config"


export interface DeckSheetData {
  columns: number
  cardWidth: number
  cardHeight: number
  total: number
  /**
   * Per-slot card image data URIs, indexed by originalIndex.
   * `null` until that card's batch has been received from the server.
   */
  cardImageUrls: (string | null)[]
}

type SheetMeta  = { type: "meta";  columns: number; cardWidth: number; cardHeight: number; total: number }
type SheetCards = { type: "cards"; startIndex: number; cards: string[] }
type SheetMsg   = SheetMeta | SheetCards

/**
 * Hook to fetch a deck as a streaming NDJSON sequence of per-card PNG images.
 * Each card arrives as a `data:image/png;base64,…` URI stored by slot index.
 * No canvas stitching, no JPEG re-encoding, no crop arithmetic on the client.
 */
export function useDeckSheet() {
  const [data, setData]       = useState<DeckSheetData | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError]     = useState<string | null>(null)
  const loadingRef             = useRef(false)
  // Mutable accumulator so functional setData updates always see the latest URLs.
  const cardUrlsRef            = useRef<(string | null)[]>([])

  const fetchSheet = useCallback(async (
    deckName: string = "Plains",
    deckId: string | null = null,
    columns: number = 5,
    cardHeight: number = 300
  ) => {
    if (loadingRef.current) {
      console.log("[useDeckSheet] Already loading, skipping duplicate fetch")
      return
    }
    loadingRef.current = true
    setLoading(true)
    setError(null)

    try {
      const params = new URLSearchParams()
      if (deckId)   params.append("id",        deckId)
      if (deckName) params.append("name",      deckName)
      params.append("columns",    columns.toString())
      params.append("cardHeight", cardHeight.toString())

      const response = await fetch(getApiUrl(`/api/decks/sheet?${params}`))

      if (!response.ok) {
        let errorMsg = `HTTP ${response.status}`
        try {
          const errorData = await response.json()
          if (errorData.error) errorMsg = errorData.error
        } catch {
          if (response.status === 404) errorMsg = `Deck '${deckName}' not found`
        }
        throw new Error(errorMsg)
      }

      const reader  = response.body!.getReader()
      const decoder = new TextDecoder()
      let buffer    = ""

      while (true) {
        const { done, value } = await reader.read()
        if (done) break

        buffer += decoder.decode(value, { stream: true })
        const lines = buffer.split("\n")
        buffer = lines.pop() ?? ""

        for (const line of lines) {
          const trimmed = line.trim()
          if (!trimmed) continue
          const msg: SheetMsg = JSON.parse(trimmed)

          if (msg.type === "meta") {
            // Initialise the URL array with nulls — cards show as skeletons
            // until their individual PNG arrives in a subsequent batch.
            cardUrlsRef.current = new Array(msg.total).fill(null)
            setData({
              columns:       msg.columns,
              cardWidth:     msg.cardWidth,
              cardHeight:    msg.cardHeight,
              total:         msg.total,
              cardImageUrls: [...cardUrlsRef.current],
            })
          } else if (msg.type === "cards") {
            msg.cards.forEach((base64, i) => {
              if (base64)
                cardUrlsRef.current[msg.startIndex + i] = `data:image/png;base64,${base64}`
            })
            // Spread to produce a new array reference so React re-renders.
            setData(prev => prev ? { ...prev, cardImageUrls: [...cardUrlsRef.current] } : null)
          }
        }
      }
    } catch (err) {
      console.error("[useDeckSheet] Error:", err)
      setError(err instanceof Error ? err.message : "Unknown error")
    } finally {
      loadingRef.current = false
      setLoading(false)
    }
  }, [])

  const reset = useCallback(() => {
    cardUrlsRef.current = []
    setData(null)
    setError(null)
  }, [])

  return { data, loading, error, fetchSheet, reset }
}

