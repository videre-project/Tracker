/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import type { CollectionCardEntry, CollectionProductEntry } from "@/hooks/use-collection"

export type CollectionSortMode = "name" | "quantity" | "price"
export type CollectionSortDirection = "asc" | "desc"
export type CollectionViewMode = "cards" | "products"
export type CollectionGridItem = CollectionCardEntry | CollectionProductEntry

export type SelectedCollectionItem = {
  item: CollectionGridItem
  viewMode: CollectionViewMode
}

export type RenderedCollectionPanel = SelectedCollectionItem & { open: boolean }

export type CollectionPriceHistoryPoint = {
  date: string
  price: number
  source?: string | null
}

export type CollectionPriceHistorySnapshot = {
  catalogId: number
  priceCacheExpiresAt: string
  prices: CollectionPriceHistoryPoint[]
}

export type CollectionSearchResponse = {
  query: string
  catalogIds: number[]
}

export type CollectionPriceChartPoint = {
  date: string
  price: number
  label: string
  delta: number | null
  deltaLabel: string | null
  deltaPercent: number | null
  deltaPercentLabel: string | null
  deltaPositive: boolean | null
}

export type CollectionCardDetail = {
  catalogId: number
  name: string
  canonicalName: string
  printedName?: string | null
  setCode: string
  setName?: string | null
  collectorNumber?: string | null
  rarity?: string | null
  manaCost?: string | null
  manaValue?: number | null
  typeLine: string
  oracleText: string
  flavorText?: string | null
  colors: string[]
  imageUrl: string
  power?: string | null
  toughness?: string | null
  loyalty?: string | null
  defense?: string | null
  artist?: string | null
  promoLabel?: string | null
  otherFaceCatalogId?: number | null
}
