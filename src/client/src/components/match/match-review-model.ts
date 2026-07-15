/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import type { GameLogDTO, SideboardChangeDTO, ZoneTransferData } from "@/types/api"
import type { GameAction } from "@/types/game-types"
type ReplayCardCatalog = {
  cards: Array<{ cardId: number; catalogId?: number | null }>
}
const TYPE_ORDER: Record<string, number> = {
  GameState: 0,
  GameAction: 1,
  ZoneChange: 2,
  Reveal: 2,
  CardChange: 3,
  PlayerChange: 4,
  LogMessage: 5,
  DamageAssignment: 6,
}
export type OpeningHandCard = {
  key: string
  name: string
  catalogId?: number | null
  bottomed: boolean
}

export type SideboardingCard = {
  key: string
  name: string
  quantity: number
  catalogId?: number | null
}

export type SideboardingDiff = {
  in: SideboardingCard[]
  out: SideboardingCard[]
  emptyMessage: string
}

function getLogTime(log: GameLogDTO) {
  const time = log.timestamp ? new Date(log.timestamp).getTime() : 0
  return Number.isFinite(time) ? time : 0
}

function sortGameLogs(logs: GameLogDTO[]) {
  return logs
    .map((log, index) => ({ log, index }))
    .sort((a, b) => {
      const timeDiff = getLogTime(a.log) - getLogTime(b.log)
      if (timeDiff !== 0) return timeDiff

      const aType = TYPE_ORDER[a.log.gameLogType] ?? 6
      const bType = TYPE_ORDER[b.log.gameLogType] ?? 6
      if (aType !== bType) return aType - bType

      return a.index - b.index
    })
}

function isOpeningKeepAction(log: GameLogDTO) {
  if (log.gameLogType !== "GameAction") return false

  try {
    const action = JSON.parse(log.data ?? "{}") as GameAction
    const name = action.name?.trim().toLowerCase()
    const response = typeof action.response === "string"
      ? action.response.trim().toLowerCase()
      : null
    return name === "keep" || response === "keep"
  } catch {
    return false
  }
}

function transferKeys(transfer: ZoneTransferData) {
  return [
    transfer.cardId != null ? `card:${transfer.cardId}` : null,
    transfer.sourceId != null ? `card:${transfer.sourceId}` : null,
  ].filter(Boolean) as string[]
}

function applyHandTransfer(
  hand: Map<string, OpeningHandCard>,
  transfer: ZoneTransferData,
  catalogIdByCardId: Map<number, number | null>
) {
  const cardName = transfer.cardName?.trim()
  if (!cardName) return

  const fromHand = transfer.fromZone === "Hand"
  const toHand = transfer.toZone === "Hand"

  if (fromHand && !toHand) {
    for (const key of transferKeys(transfer)) {
      hand.delete(key)
    }
    return
  }

  if (!toHand) return

  for (const key of transferKeys(transfer)) {
    hand.delete(key)
  }

  const key = transfer.cardId != null
    ? `card:${transfer.cardId}`
    : transfer.sourceId != null
      ? `card:${transfer.sourceId}`
      : `name:${cardName}:${hand.size}`

  hand.set(key, {
    key,
    name: cardName,
    bottomed: false,
    catalogId: transfer.cardId != null
      ? catalogIdByCardId.get(transfer.cardId) ?? null
      : transfer.sourceId != null
        ? catalogIdByCardId.get(transfer.sourceId) ?? null
        : null,
  })
}

function applyHandLog(
  hand: Map<string, OpeningHandCard>,
  log: GameLogDTO,
  catalogIdByCardId: Map<number, number | null>
) {
  if (log.gameLogType !== "ZoneChange" && log.gameLogType !== "Reveal") return

  try {
    const transfers = JSON.parse(log.data ?? "[]") as ZoneTransferData[]
    for (const transfer of transfers) {
      applyHandTransfer(hand, transfer, catalogIdByCardId)
    }
  } catch {
    // Ignore malformed historical log payloads.
  }
}

function isPregameState(log: GameLogDTO) {
  if (log.gameLogType !== "GameState") return true

  try {
    const state = JSON.parse(log.data ?? "{}") as { phase?: string }
    return state.phase?.toLowerCase().startsWith("pregame") ?? true
  } catch {
    return true
  }
}

function updateBottomedCards(
  hand: Map<string, OpeningHandCard>,
  log: GameLogDTO,
) {
  if (log.gameLogType !== "ZoneChange" && log.gameLogType !== "Reveal") return

  try {
    const transfers = JSON.parse(log.data ?? "[]") as ZoneTransferData[]
    for (const transfer of transfers) {
      const leftHand = transfer.fromZone === "Hand" && transfer.toZone == null
      const returnedToHand = transfer.toZone === "Hand"
      if (!leftHand && !returnedToHand) continue

      for (const key of transferKeys(transfer)) {
        const card = hand.get(key)
        if (card) card.bottomed = leftHand
      }
    }
  } catch {
    // Ignore malformed historical log payloads.
  }
}

export function getOpeningHandCards(logs: GameLogDTO[], catalogIdByCardId: Map<number, number | null>) {
  const sorted = sortGameLogs(logs)
  const keepIndex = sorted.findIndex(entry => isOpeningKeepAction(entry.log))
  const keepEntry = sorted[keepIndex]
  if (!keepEntry) return []

  const hand = new Map<string, OpeningHandCard>()
  for (let index = 0; index < keepIndex; index++) {
    applyHandLog(hand, sorted[index].log, catalogIdByCardId)
  }

  if (hand.size === 0) {
    const keepTime = getLogTime(keepEntry.log)
    for (const entry of sorted) {
      if (getLogTime(entry.log) > keepTime) break
      applyHandLog(hand, entry.log, catalogIdByCardId)
    }
  }

  for (let index = keepIndex + 1; index < sorted.length; index++) {
    const log = sorted[index].log
    if (!isPregameState(log)) break
    updateBottomedCards(hand, log)
  }

  return Array.from(hand.values())
}

export function getCatalogIdByCardId(replay?: ReplayCardCatalog | null) {
  const catalogIdByCardId = new Map<number, number | null>()
  if (!replay) return catalogIdByCardId

  for (const card of replay.cards) {
    if (!catalogIdByCardId.has(card.cardId)) {
      catalogIdByCardId.set(card.cardId, card.catalogId ?? null)
    }
  }

  return catalogIdByCardId
}

export function getSideboardingDiff(changes?: SideboardChangeDTO[] | null): SideboardingDiff {
  const added = new Map<number, SideboardingCard>()
  const removed = new Map<number, SideboardingCard>()

  for (const change of changes ?? []) {
    const quantity = change.quantity ?? 0
    const catalogId = change.catalogId ?? 0
    if (quantity === 0 || catalogId <= 0) continue

    const cards = quantity > 0 ? added : removed
    const direction = quantity > 0 ? "in" : "out"
    const amount = Math.abs(quantity)
    const existing = cards.get(catalogId)
    if (existing) {
      existing.quantity += amount
      continue
    }

    cards.set(catalogId, {
      key: `${direction}:${catalogId}`,
      name: change.name?.trim() || `Card ID #${catalogId}`,
      quantity: amount,
      catalogId,
    })
  }

  const byName = (a: SideboardingCard, b: SideboardingCard) =>
    a.name.localeCompare(b.name) || (a.catalogId ?? 0) - (b.catalogId ?? 0)

  return {
    in: Array.from(added.values()).sort(byName),
    out: Array.from(removed.values()).sort(byName),
    emptyMessage: "No sideboard changes.",
  }
}
