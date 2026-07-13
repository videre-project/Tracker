/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

interface CardStats {
  type: string
  power?: string | null
  toughness?: string | null
  loyalty?: string | null
  defense?: string | null
}

export function getCardStatText(card: CardStats | null): string | null {
  if (!card) return null
  const normalizedType = card.type.toLowerCase()

  if (normalizedType.includes("creature")) {
    const power = card.power?.trim()
    const toughness = card.toughness?.trim()
    return power && toughness ? `${power}/${toughness}` : null
  }

  if (normalizedType.includes("planeswalker")) {
    const loyalty = card.loyalty?.trim()
    return loyalty ? `Loyalty ${loyalty}` : null
  }

  if (normalizedType.includes("battle")) {
    const defense = card.defense?.trim()
    return defense ? `Defense ${defense}` : null
  }

  return null
}
