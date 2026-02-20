import { useState, useCallback, useRef } from "react"
import { getApiUrl } from "../utils/api-config"

export interface SlotInfo {
  index: number
  cardId: number
  name: string
  quantity: number
}

export interface DeckSheetMetadata {
  columns: number
  cardWidth: number
  cardHeight: number
  imageWidth: number
  imageHeight: number
  slotCount: number
  slots: SlotInfo[]
}

export interface DeckSheetData extends DeckSheetMetadata {
  imageUrl: string
}

/**
 * Hook to fetch a deck rendered as a single sheet image.
 * The sheet contains all cards in a grid, which can be cropped on the frontend.
 * Also includes slot metadata for sorting/grouping (parsed from X-Slots header).
 */
export function useDeckSheet() {
  const [data, setData] = useState<DeckSheetData | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const loadingRef = useRef(false) // Ref for stale-closure-safe loading check

  const fetchSheet = useCallback(async (
    deckName: string = "Plains",
    deckId: string | null = null,
    columns: number = 5,
    cardHeight: number = 300
  ) => {
    // Skip if already fetching (prevents duplicate calls)
    if (loadingRef.current) {
      console.log("[useDeckSheet] Already loading, skipping duplicate fetch")
      return
    }
    loadingRef.current = true
    setLoading(true)
    setError(null)

    try {
      const params = new URLSearchParams()
      if (deckId) params.append("id", deckId)
      if (deckName) params.append("name", deckName)
      params.append("columns", columns.toString())
      params.append("cardHeight", cardHeight.toString())

      const response = await fetch(getApiUrl(`/api/decks/sheet?${params}`))

      if (!response.ok) {
        let errorMsg = `HTTP ${response.status}`
        try {
          const errorData = await response.json()
          if (errorData.error) {
            errorMsg = errorData.error
          }
        } catch {
          if (response.status === 404) {
            errorMsg = `Deck '${deckName}' not found`
          }
        }
        throw new Error(errorMsg)
      }

      // Parse slot metadata from X-Slots header
      let slots: SlotInfo[] = []
      const slotsHeader = response.headers.get("X-Slots")
      if (slotsHeader) {
        try {
          const rawSlots = JSON.parse(slotsHeader)
          slots = rawSlots.map((s: { Index: number; CardId: number; Name: string; Quantity: number }) => ({
            index: s.Index,
            cardId: s.CardId,
            name: s.Name,
            quantity: s.Quantity
          }))
        } catch (e) {
          console.warn("[useDeckSheet] Failed to parse X-Slots header:", e)
        }
      }

      // Extract metadata from response headers
      const metadata: DeckSheetMetadata = {
        columns: parseInt(response.headers.get("X-Grid-Columns") || columns.toString()),
        cardWidth: parseInt(response.headers.get("X-Card-Width") || "214"),
        cardHeight: parseInt(response.headers.get("X-Card-Height") || cardHeight.toString()),
        imageWidth: parseInt(response.headers.get("X-Image-Width") || "0"),
        imageHeight: parseInt(response.headers.get("X-Image-Height") || "0"),
        slotCount: parseInt(response.headers.get("X-Slot-Count") || "0"),
        slots
      }

      // Get image as blob and create object URL
      const blob = await response.blob()
      const imageUrl = URL.createObjectURL(blob)

      setData({
        ...metadata,
        imageUrl
      })
    } catch (err) {
      console.error("[useDeckSheet] Error:", err)
      setError(err instanceof Error ? err.message : "Unknown error")
    } finally {
      loadingRef.current = false
      setLoading(false)
    }
  }, [])

  const reset = useCallback(() => {
    if (data?.imageUrl) {
      URL.revokeObjectURL(data.imageUrl)
    }
    setData(null)
    setError(null)
  }, [data])

  return {
    data,
    loading,
    error,
    fetchSheet,
    reset
  }
}

/**
 * Calculate the CSS background position for a card at a given index
 */
export function getCardCropPosition(
  index: number,
  columns: number,
  cardWidth: number,
  cardHeight: number
): { x: number; y: number } {
  const col = index % columns
  const row = Math.floor(index / columns)
  return {
    x: col * cardWidth,
    y: row * cardHeight
  }
}
