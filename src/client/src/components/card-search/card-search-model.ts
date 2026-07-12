/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

export const CARD_COLORS = ["W", "U", "B", "R", "G", "C"] as const

export const CARD_SEARCH_TEXT_MODES = [
  { value: "smart", label: "Names and rules" },
  { value: "name", label: "Name only" },
  { value: "exact", label: "Exact name" },
  { value: "oracle", label: "Rules text" },
  { value: "flavor", label: "Flavor text" },
] as const

export const CARD_COLOR_MODES = [
  { value: "any", label: "Any colors" },
  { value: "includes", label: "Includes" },
  { value: "exact", label: "Exactly" },
  { value: "atMost", label: "At most" },
  { value: "identity", label: "Identity" },
] as const

export const CARD_TYPE_FILTERS = [
  "creature",
  "instant",
  "sorcery",
  "artifact",
  "enchantment",
  "planeswalker",
  "land",
  "battle",
] as const

export const CARD_RARITIES = ["common", "uncommon", "rare", "mythic"] as const
export const CARD_FORMATS = [
  "standard",
  "pioneer",
  "modern",
  "legacy",
  "vintage",
  "pauper",
  "premodern",
] as const
export const CARD_LEGALITIES = [
  "legal",
  "not_legal",
  "banned",
  "restricted",
  "suspended",
] as const

export type CardColor = (typeof CARD_COLORS)[number]
export type CardSearchTextMode = (typeof CARD_SEARCH_TEXT_MODES)[number]["value"]
export type CardColorMode = (typeof CARD_COLOR_MODES)[number]["value"]
export type CardTypeFilter = (typeof CARD_TYPE_FILTERS)[number]
export type CardTypeFilterState = "off" | "include" | "exclude"
export type CardComparisonOperator = "any" | "<" | "<=" | "=" | ">" | ">="
export type CardRarityFilter = "any" | (typeof CARD_RARITIES)[number]
export type CardFormatFilter = "any" | (typeof CARD_FORMATS)[number]
export type CardLegalityFilter = (typeof CARD_LEGALITIES)[number]
export type CardBooleanMode = "any" | "only" | "exclude"
export type CardTokenFilterMode = "default" | "only"

export type CardFilterState = {
  textMode: CardSearchTextMode
  colorMode: CardColorMode
  colors: CardColor[]
  typeStates: Partial<Record<CardTypeFilter, CardTypeFilterState>>
  manaValueOperator: CardComparisonOperator
  manaValue: string
  manaCost: string
  rarityOperator: CardComparisonOperator
  rarity: CardRarityFilter
  format: CardFormatFilter
  legality: CardLegalityFilter
  tokenMode: CardTokenFilterMode
  promoMode: CardBooleanMode
  multifaceMode: CardBooleanMode
  splitMode: CardBooleanMode
  setCode: string
  collectorNumber: string
  catalogId: string
  artId: string
  frameStyle: string
  promoLabel: string
  artist: string
  flavor: string
}

export const DEFAULT_CARD_FILTERS: CardFilterState = {
  textMode: "smart",
  colorMode: "any",
  colors: [],
  typeStates: {},
  manaValueOperator: "any",
  manaValue: "",
  manaCost: "",
  rarityOperator: "=",
  rarity: "any",
  format: "any",
  legality: "legal",
  tokenMode: "default",
  promoMode: "any",
  multifaceMode: "any",
  splitMode: "any",
  setCode: "",
  collectorNumber: "",
  catalogId: "",
  artId: "",
  frameStyle: "",
  promoLabel: "",
  artist: "",
  flavor: "",
}

function quoteSearchValue(value: string) {
  const trimmed = value.trim()
  if (!trimmed) return ""
  if (/^[a-z0-9_./-]+$/i.test(trimmed)) return trimmed
  return `"${trimmed.replace(/"/g, '\\"')}"`
}

function formatQueryTerm(label: string, value: string) {
  const formatted = quoteSearchValue(value)
  return formatted ? `${label}:${formatted}` : ""
}

function formatSearchTextTerm(value: string, mode: CardSearchTextMode) {
  const trimmed = value.trim()
  if (!trimmed) return ""
  if (mode === "smart") return trimmed
  if (mode === "exact") return `!"${trimmed.replace(/"/g, '\\"')}"`
  if (mode === "name") return formatQueryTerm("name", trimmed)
  if (mode === "oracle") return formatQueryTerm("oracle", trimmed)
  return formatQueryTerm("flavor", trimmed)
}

export function buildCardSearchQuery(baseQuery: string, filters: CardFilterState) {
  const terms: string[] = []
  const searchText = formatSearchTextTerm(baseQuery, filters.textMode)
  if (searchText) terms.push(searchText)

  if (filters.colorMode !== "any" && filters.colors.length > 0) {
    const colors = filters.colors.join("")
    if (filters.colorMode === "includes") terms.push(`c>=${colors}`)
    if (filters.colorMode === "exact") terms.push(`c=${colors}`)
    if (filters.colorMode === "atMost") terms.push(`c<=${colors}`)
    if (filters.colorMode === "identity") terms.push(`id<=${colors}`)
  }

  CARD_TYPE_FILTERS.forEach(type => {
    const state = filters.typeStates[type]
    if (state === "include") terms.push(`t:${type}`)
    if (state === "exclude") terms.push(`-t:${type}`)
  })

  const manaValue = Number.parseInt(filters.manaValue, 10)
  if (filters.manaValueOperator !== "any" && Number.isFinite(manaValue) && manaValue >= 0) {
    terms.push(`mv${filters.manaValueOperator}${manaValue}`)
  }

  const manaCost = formatQueryTerm("m", filters.manaCost)
  if (manaCost) terms.push(manaCost)

  if (filters.rarity !== "any") {
    terms.push(filters.rarityOperator === "any" || filters.rarityOperator === "="
      ? `r:${filters.rarity}`
      : `r${filters.rarityOperator}${filters.rarity}`)
  }

  if (filters.format !== "any") {
    if (filters.legality === "legal") terms.push(`format:${filters.format}`)
    else if (filters.legality === "not_legal") terms.push(`legality:${filters.format}:${filters.legality}`)
    else terms.push(`${filters.legality}:${filters.format}`)
  }

  if (filters.tokenMode === "only") terms.push("is:token")
  if (filters.promoMode === "only") terms.push("is:promo")
  if (filters.promoMode === "exclude") terms.push("-is:promo")
  if (filters.multifaceMode === "only") terms.push("is:multiface")
  if (filters.multifaceMode === "exclude") terms.push("-is:multiface")
  if (filters.splitMode === "only") terms.push("is:split")
  if (filters.splitMode === "exclude") terms.push("-is:split")

  const fields: Array<[string, string]> = [
    ["set", filters.setCode],
    ["number", filters.collectorNumber],
    ["cid", filters.catalogId],
    ["artid", filters.artId],
    ["frame", filters.frameStyle],
    ["promo", filters.promoLabel],
    ["artist", filters.artist],
    ["flavor", filters.flavor],
  ]
  fields.forEach(([label, value]) => {
    const term = formatQueryTerm(label, value)
    if (term) terms.push(term)
  })

  return terms.join(" ").trim()
}

export function getActiveCardFilterCount(filters: CardFilterState) {
  let count = 0
  if (filters.textMode !== "smart") count++
  if (filters.colorMode !== "any" && filters.colors.length > 0) count++
  count += Object.values(filters.typeStates).filter(state => state && state !== "off").length
  if (filters.manaValueOperator !== "any" && filters.manaValue.trim()) count++
  if (filters.manaCost.trim()) count++
  if (filters.rarity !== "any") count++
  if (filters.format !== "any") count++
  if (filters.tokenMode !== "default") count++
  if (filters.promoMode !== "any") count++
  if (filters.multifaceMode !== "any") count++
  if (filters.splitMode !== "any") count++
  if (filters.setCode.trim()) count++
  if (filters.collectorNumber.trim()) count++
  if (filters.catalogId.trim()) count++
  if (filters.artId.trim()) count++
  if (filters.frameStyle.trim()) count++
  if (filters.promoLabel.trim()) count++
  if (filters.artist.trim()) count++
  if (filters.flavor.trim()) count++
  return count
}

export function titleCaseCardFilter(value: string) {
  return value
    .split("_")
    .map(part => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ")
}
