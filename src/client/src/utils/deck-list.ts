/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

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
