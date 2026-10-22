/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import type { DeckReferenceData, WinLossRecord } from "@/types/api"

/** Preserve deck selection when switching between deck and archetype grouping. */
export type DashboardDeckSelection = { id?: number; archetype: string; format?: string } | null

export function matchupDeckKey(deck: DeckReferenceData): string {
  return deck.id != null ? `deck:${deck.id}`
    : JSON.stringify([deck.format, deck.archetype?.trim().toLocaleLowerCase()])
}

export function combineRecords(
  records: (WinLossRecord | undefined)[]
): WinLossRecord {
  const wins = records.reduce((sum, record) => sum + (record?.wins ?? 0), 0)
  const losses = records.reduce((sum, record) => sum + (record?.losses ?? 0), 0)
  const draws = records.reduce((sum, record) => sum + (record?.draws ?? 0), 0)
  const total = wins + losses + draws
  return {
    wins,
    losses,
    draws,
    total,
    winrate: total ? Math.round((1000 * wins) / total) / 10 : null
  }
}

export function recordText(record?: WinLossRecord) {
  return `${record?.wins ?? 0}–${record?.losses ?? 0}${record?.draws ? `–${record.draws}` : ""}`
}

export function rateText(record?: WinLossRecord) {
  return record?.winrate == null ? "—" : `${record.winrate.toFixed(1)}%`
}
