import { useState, useCallback, useRef } from "react"
import {
  COLORLESS_CARD_COLOR,
  getDisplayCardColors,
  VIDERE_CARD_COLORS,
} from "@/utils/card-colors"
import {
  CARD_RARITIES_BY_DISPLAY_ORDER,
  formatCardRarity,
  normalizeCardRarity,
} from "@/utils/card-rarity"
import { getApiUrl } from "../utils/api-config"

const UNKNOWN_GROUP = "Unknown"
const RARITY_COLUMNS = CARD_RARITIES_BY_DISPLAY_ORDER.map(formatCardRarity)

function getRarityGroup(rarity: string): string {
  const normalizedRarity = normalizeCardRarity(rarity)
  return normalizedRarity ? formatCardRarity(normalizedRarity) : UNKNOWN_GROUP
}

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
        keys = [...getDisplayCardColors(card.colors)]
        break
      case 'types':
        // Group by primary type (first type in list)
        keys = card.types.length > 0 ? [card.types[0]] : ['Unknown']
        break
      case 'rarity':
        // Group by rarity
        keys = [getRarityGroup(card.rarity)]
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
export function getSortModeColumns(
  mode: SortMode,
  cards: SortableCardEntry[] = []
): string[] {
  const groups = groupCardsBySortMode(cards, mode)

  switch (mode) {
    case 'cmc':
      return ['0', '1', '2', '3', '4', '5', '6+']
        .filter(column => groups.has(column))
    case 'colors':
      return [...VIDERE_CARD_COLORS, COLORLESS_CARD_COLOR]
        .filter(column => groups.has(column))
    case 'types': {
      const types = new Set(cards
        .map(card => card.types[0]?.trim())
        .filter((type): type is string => Boolean(type)))
      const columns = [...types].sort((left, right) => left.localeCompare(right))
      return cards.some(card => !card.types[0]?.trim())
        ? [...columns, UNKNOWN_GROUP]
        : columns
    }
    case 'rarity':
      return [...RARITY_COLUMNS, UNKNOWN_GROUP]
        .filter(column => groups.has(column))
  }
}

/**
 * Flatten the selected sort groups into a single ordered list. Cards that
 * belong to more than one group, such as multicolored cards, remain a single
 * entry in the collapsed view and use their first matching group.
 */
export function sortCardsBySortMode(
  cards: SortableCardEntry[],
  mode: SortMode,
): SortableCardEntry[] {
  const groups = groupCardsBySortMode(cards, mode)
  const ordered: SortableCardEntry[] = []
  const seen = new Set<number>()

  const appendGroup = (group: SortableCardEntry[] | undefined) => {
    group?.forEach(card => {
      if (!seen.has(card.index)) {
        seen.add(card.index)
        ordered.push(card)
      }
    })
  }

  getSortModeColumns(mode, cards).forEach(column => appendGroup(groups.get(column)))
  groups.forEach((group, key) => {
    if (!getSortModeColumns(mode, cards).includes(key)) appendGroup(group)
  })

  return ordered
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
