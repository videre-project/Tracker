/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import type {
  CollectionPriceHistorySnapshot,
  CollectionSortDirection,
} from "./collection-types"

export function formatCollectionPrice(price?: number | null) {
  if (typeof price !== "number" || !Number.isFinite(price)) return null
  if (price >= 100) return price.toFixed(0)
  if (price >= 10) return price.toFixed(1)
  if (price >= 1) return price.toFixed(2)
  return price.toFixed(3).replace(/0+$/, "").replace(/\.$/, "")
}

export function getCollectionHistoryPrecision(values: Array<number | null | undefined>) {
  const finiteValues = values.filter((value): value is number => (
    typeof value === "number" && Number.isFinite(value)
  ))
  if (finiteValues.length === 0) return 2

  const minDecimals = finiteValues.some(value => Math.abs(value) < 100) ? 2 : 0
  for (let decimals = minDecimals; decimals <= 3; decimals += 1) {
    if (finiteValues.every(value => Math.abs(value - Number(value.toFixed(decimals))) < 0.0005)) {
      return decimals
    }
  }
  return 3
}

export function formatCollectionHistoryPrice(
  price?: number | null,
  decimals = getCollectionHistoryPrecision([price])
) {
  if (typeof price !== "number" || !Number.isFinite(price)) return null
  return price.toFixed(decimals)
}

export function isPriceHistoryCacheFresh(snapshot: CollectionPriceHistorySnapshot | undefined) {
  if (!snapshot) return false
  const expiresAt = Date.parse(snapshot.priceCacheExpiresAt)
  return Number.isFinite(expiresAt) && Date.now() < expiresAt
}

export function formatPriceDelta(
  delta: number | null,
  decimals = getCollectionHistoryPrecision([delta])
) {
  if (delta === null || !Number.isFinite(delta)) return null
  if (Math.abs(delta) < 0.0005) return `+${(0).toFixed(decimals)}`
  const label = formatCollectionHistoryPrice(Math.abs(delta), decimals) ?? Math.abs(delta).toFixed(decimals)
  return `${delta >= 0 ? "+" : "-"}${label}`
}

export function formatPriceDeltaPercent(percent: number | null) {
  if (percent === null || !Number.isFinite(percent)) return null
  if (Math.abs(percent) < 0.05) return "+0.0%"
  return `${percent >= 0 ? "+" : ""}${percent.toFixed(1)}%`
}

export function formatTrendPercent(percent: number | null) {
  if (percent === null || !Number.isFinite(percent) || Math.abs(percent) < 0.05) return null
  return `${percent >= 0 ? "+" : ""}${percent.toFixed(1)}%`
}

export function compareCollectionNumbers(
  a: number | null | undefined,
  b: number | null | undefined,
  direction: CollectionSortDirection
) {
  const aValue = typeof a === "number" && Number.isFinite(a) ? a : null
  const bValue = typeof b === "number" && Number.isFinite(b) ? b : null
  if (aValue === null && bValue === null) return 0
  if (aValue === null) return 1
  if (bValue === null) return -1
  return (aValue - bValue) * (direction === "asc" ? 1 : -1)
}
