/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { renderHook, waitFor } from "@testing-library/react"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { jsonResponse } from "@/test/http"
import { useMetagameDecks } from "./use-metagame-decks"

describe("useMetagameDecks", () => {
  beforeEach(() => {
    vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)
      if (url.includes("/metagame/modern")) {
        return jsonResponse({ data: [{
          id: 500,
          archetype: "Goryo's Vengeance",
          count: 25,
          percentage: "12.50%",
          match_count: 40,
          match_winrate: "55.00%",
        }] })
      }
      if (url.includes("/decks/modern")) {
        return jsonResponse({ data: [{
          id: 123,
          mainboard: [
            "(21737,\"Goryo's Vengeance\",4)",
            "(106607,\"Atraxa, Grand Unifier\",4)",
            "(263,Island,1)",
          ],
          sideboard: ["(456,Thoughtseize,2)"],
        }] })
      }
      if (url.includes("/cards/search") && init?.method === "QUERY") {
        return jsonResponse({ data: [
          { id: 21737, colors: ["B"], mana_value: 2, type_line: "Instant", rarity: "rare" },
          { id: 106607, colors: ["W", "U", "B", "R", "G"], mana_value: 7, type_line: "Legendary Creature — Angel" },
          { id: 263, colors: ["U"] },
          { id: 456, colors: ["B"], mana_value: 1, type_line: "Sorcery" },
        ] })
      }
      return jsonResponse({}, { status: 404 })
    }))
  })

  it("loads date-filtered metagame rows and representative card stacks", async () => {
    const { result } = renderHook(() => useMetagameDecks({
      formats: ["Modern"],
      decksPerFormat: 16,
      dateRange: {
        from: new Date("2026-07-24T12:00:00Z"),
        to: new Date("2026-08-23T12:00:00Z"),
      },
    }))

    await waitFor(() => expect(result.current.loading).toBe(false))

    expect(result.current.error).toBeNull()
    expect(result.current.decks).toEqual([expect.objectContaining({
      revisionId: 123,
      name: "Goryo's Vengeance",
      format: "Modern",
      archetype: "12.50% of the field",
      colors: ["W", "U", "B", "R", "G"],
      wins: 22,
      losses: 18,
      featuredCards: [
        expect.objectContaining({ catalogId: 21737, name: "Goryo's Vengeance", quantity: 4 }),
        expect.objectContaining({ catalogId: 106607, name: "Atraxa, Grand Unifier", quantity: 4 }),
      ],
      mainboardCount: 9,
      sideboardCount: 2,
      sideboard: [expect.objectContaining({
        catalogId: 456,
        name: "Thoughtseize",
        quantity: 2,
        cmc: 1,
        types: ["Sorcery"],
      })],
    })])

    const calls = vi.mocked(fetch).mock.calls
    expect(calls[0]?.[0]).toContain("/metagame/modern?")
    expect(calls[0]?.[0]).toContain("limit=16")
    expect(calls[0]?.[0]).toContain("min_date=")
    expect(calls[0]?.[0]).toContain("max_date=")
    expect(calls.some(([, init]) => init?.method === "QUERY")).toBe(true)
  })
})
