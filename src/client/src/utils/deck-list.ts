/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import type { DeckCardEntry, DeckEditorCard } from "@videreproject/ui"
import type { components } from "@/types/videre.g"

export function toDeckCards(cards?: components["schemas"]["EventDeck"]["mainboard"]): DeckCardEntry[] {
  return (cards ?? []).filter(card => card.id > 0 && card.quantity > 0)
    .map(card => ({ catalogId: card.id, name: card.name, quantity: card.quantity }))
}

export function toEditorCards(deck?: { mainboard: DeckCardEntry[]; sideboard: DeckCardEntry[] } | null): DeckEditorCard[] {
  if (!deck) return []
  return [...deck.mainboard.map(card => ({ card, zone: "Mainboard" as const })),
    ...deck.sideboard.map(card => ({ card, zone: "Sideboard" as const }))]
    .map(({ card, zone }, index) => ({
      ...card, index, originalIndex: index, zone,
      cmc: card.cmc ?? 0, colors: card.colors ?? [], types: card.types ?? [], rarity: card.rarity ?? "common",
    }))
}

type DeckListCard = { name: string; quantity: number }
type DeckListDetail = { mainboard: DeckListCard[]; sideboard: DeckListCard[] }

function buildDeckListSection(cards: DeckListCard[]) {
  const totalsByName = new Map<string, number>()
  cards.forEach(card => {
    totalsByName.set(card.name, (totalsByName.get(card.name) ?? 0) + card.quantity)
  })
  return Array.from(totalsByName.entries()).map(([name, quantity]) => `${quantity} ${name}`)
}

export function buildDeckListText(detail?: DeckListDetail | null) {
  if (!detail) return ""
  const mainboard = buildDeckListSection(detail.mainboard)
  const sideboard = buildDeckListSection(detail.sideboard)
  return sideboard.length > 0
    ? [...mainboard, "", ...sideboard].join("\n")
    : mainboard.join("\n")
}

export function getDeckFileName(deckName: string) {
  const stem = deckName
    .trim()
    .replace(/[^a-z0-9]+/gi, "-")
    .replace(/^-+|-+$/g, "")
    .toLowerCase()
  return `${stem || "deck"}.txt`
}
