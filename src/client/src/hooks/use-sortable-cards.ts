import { useState, useCallback, useRef } from "react"
import { getApiUrl } from "../utils/api-config"

export interface SortableCardEntry {
  index: number
  originalIndex: number // Original index in the sheet (before unrolling)
  catalogId: number
  name: string
  quantity: number
  cmc: number
  colors: string[]
  types: string[]
  rarity: string
  zone: 'Mainboard' | 'Sideboard'
}

export type SortMode = 'cmc' | 'colors' | 'types' | 'rarity'

export function useSortableCards() {
  const [cards, setCards] = useState<SortableCardEntry[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const loadingRef = useRef(false) // Ref for stale-closure-safe loading check

  const fetchSortableCards = useCallback(async (deckName: string, deckId?: string) => {
    // Skip if already fetching (prevents duplicate calls)
    if (loadingRef.current) {
      console.log("[useSortableCards] Already loading, skipping duplicate fetch")
      return
    }
    loadingRef.current = true
    setLoading(true)
    setError(null)

    try {
      const params = new URLSearchParams()
      if (deckId) params.append("id", deckId)
      if (deckName) params.append("name", deckName)
      
      const res = await fetch(getApiUrl(`/api/decks/sortable?${params}`))
      if (!res.ok) {
        const errData = await res.json().catch(() => ({}))
        throw new Error(errData.error || `HTTP ${res.status}`)
      }
      const data = await res.json()
      setCards(data)
    } catch (err) {
      setError(err instanceof Error ? err.message : "Unknown error")
    } finally {
      loadingRef.current = false
      setLoading(false)
    }
  }, [])

  const reset = useCallback(() => {
    setCards([])
    setError(null)
  }, [])

  return { cards, loading, error, fetchSortableCards, reset }
}

// Helper: Group cards by sort mode
export function groupCardsBySortMode(
  cards: SortableCardEntry[],
  mode: SortMode
): Map<string, SortableCardEntry[]> {
  const groups = new Map<string, SortableCardEntry[]>()

  cards.forEach(card => {
    let keys: string[] = []

    switch (mode) {
      case 'cmc':
        // Group by CMC (0-5, 6+)
        keys = [card.cmc >= 6 ? '6+' : card.cmc.toString()]
        break
      case 'colors':
        // Group by each color (card can be in multiple groups)
        keys = card.colors.length > 0 ? card.colors : ['C'] // Colorless
        break
      case 'types':
        // Group by primary type (first type in list)
        keys = card.types.length > 0 ? [card.types[0]] : ['Unknown']
        break
      case 'rarity':
        // Group by rarity
        keys = [card.rarity || 'Common']
        break
    }

    keys.forEach(key => {
      if (!groups.has(key)) {
        groups.set(key, [])
      }
      groups.get(key)!.push(card)
    })
  })

  return groups
}

// Get column order for each sort mode
export function getSortModeColumns(mode: SortMode): string[] {
  switch (mode) {
    case 'cmc':
      return ['0', '1', '2', '3', '4', '5', '6+']
    case 'colors':
      return ['W', 'U', 'B', 'R', 'G', 'C']
    case 'types':
      return ['Creature', 'Instant', 'Sorcery', 'Artifact', 'Enchantment', 'Land', 'Planeswalker']
    case 'rarity':
      return ['Common', 'Uncommon', 'Rare', 'Mythic', 'Land']
  }
}

// Unroll cards based on quantity and assign sequential indices
// Preserves originalIndex to map back to the sheet image slot
export function unrollCards(cards: SortableCardEntry[]): SortableCardEntry[] {
  const unrolled: SortableCardEntry[] = []
  let newIndex = 0
  
  for (const card of cards) {
    for (let i = 0; i < card.quantity; i++) {
      unrolled.push({
        ...card,
        index: newIndex++,
        originalIndex: card.originalIndex, // Keep reference to sheet slot
        quantity: 1 // Each unrolled entry represents 1 copy
      })
    }
  }
  
  return unrolled
}
