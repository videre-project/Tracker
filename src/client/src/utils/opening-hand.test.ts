import { describe, expect, it } from "vitest"

import type { GameLogDTO } from "@/types/api"

import { getCatalogIdByCardId, getOpeningHandCards } from "./opening-hand"

function zoneLog(
  timestamp: string,
  transfers: Array<{ cardId: number; cardName: string; fromZone?: string; toZone?: string }>,
): GameLogDTO {
  return {
    gameLogType: "ZoneChange",
    timestamp,
    data: JSON.stringify(transfers),
  }
}

describe("opening hand", () => {
  it("returns no cards when the game log has no Keep", () => {
    expect(getOpeningHandCards([
      zoneLog("2026-01-01T00:00:00Z", [
        { cardId: 1, cardName: "Island", toZone: "Hand" },
      ]),
    ], new Map())).toEqual([])
  })

  it("keeps the pregame hand and marks cards bottomed after Keep", () => {
    const catalog = getCatalogIdByCardId({
      cards: [{ cardId: 11, catalogId: 102392 }],
    })
    const cards = getOpeningHandCards([
      zoneLog("2026-01-01T00:00:01Z", [
        { cardId: 11, cardName: "Island", toZone: "Hand" },
        { cardId: 12, cardName: "Consider", toZone: "Hand" },
      ]),
      {
        gameLogType: "GameAction",
        timestamp: "2026-01-01T00:00:02Z",
        data: JSON.stringify({ name: "Keep" }),
      },
      zoneLog("2026-01-01T00:00:03Z", [
        { cardId: 12, cardName: "Consider", fromZone: "Hand" },
      ]),
      {
        gameLogType: "GameState",
        timestamp: "2026-01-01T00:00:04Z",
        data: JSON.stringify({ phase: "PreGame2" }),
      },
    ], catalog)

    expect(cards).toEqual([
      { key: "card:11", name: "Island", catalogId: 102392, bottomed: false },
      { key: "card:12", name: "Consider", catalogId: null, bottomed: true },
    ])
  })
})
