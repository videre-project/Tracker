/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { VIDERE_OPENAPI_ENUMS } from "@/types/videre.g"

const VIDERE_CARD_RARITIES =
  VIDERE_OPENAPI_ENUMS.components.schemas.Card.properties.rarity
export type VidereCardRarity = (typeof VIDERE_CARD_RARITIES)[number]

const RARITY_ALIASES: Record<string, VidereCardRarity> = {
  "basicland":   "basic land",
  "land":        "basic land",
  "mythic rare": "mythic",
}

export const CARD_RARITY_CLASSES = {
  "common":     "border-neutral-400/35  bg-neutral-400/10  text-neutral-200",
  "uncommon":   "border-slate-300/45    bg-slate-300/10    text-slate-200",
  "rare":       "border-amber-300/45    bg-amber-500/10    text-amber-200",
  "mythic":     "border-orange-400/45   bg-orange-500/10   text-orange-300",
  "basic land": "border-neutral-400/35  bg-neutral-400/10  text-neutral-200",
  "bonus":      "border-amber-300/45    bg-amber-500/10    text-amber-200",
  "promo":      "border-amber-300/45    bg-amber-500/10    text-amber-200",
  "token":      "border-neutral-400/35  bg-neutral-400/10  text-neutral-200",
} satisfies Record<VidereCardRarity, string>

export const CARD_RARITIES_BY_DISPLAY_ORDER =
  Object.keys(CARD_RARITY_CLASSES) as VidereCardRarity[]

export function normalizeCardRarity(value?: string | null): VidereCardRarity | null {
  const normalized = value?.trim().toLowerCase().replace(/[_-]+/g, " ") ?? ""
  if (!normalized) return null

  const alias = RARITY_ALIASES[normalized]
  if (alias) return alias

  return VIDERE_CARD_RARITIES.find(rarity => rarity === normalized) ?? null
}

export function formatCardRarity(rarity: VidereCardRarity): string {
  return rarity.replace(/\b\w/g, letter => letter.toUpperCase())
}
