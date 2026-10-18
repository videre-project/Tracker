/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useState } from "react"
import type { ComponentProps } from "react"
import { act, fireEvent, render, screen, waitFor } from "@testing-library/react"
import { afterEach, expect, it, vi } from "vitest"
import { jsonResponse } from "@/test/http"
import type { DashboardDeckSelection } from "@/utils/match-results"
import type { MatchupDecks } from "./matchup-decks"
import type { MatchupResults } from "./matchup-results"
import { MatchupAnalysis } from "./matchup-analysis"

vi.mock("@videreproject/ui", () => ({
  Button: (props: ComponentProps<"button">) => <button {...props} />,
  Input: (props: ComponentProps<"input">) => <input {...props} />,
  isLimitedFormat: () => false,
}))
vi.mock("@/hooks/use-client-state", () => ({
  useClientState: () => ({ isReady: true, loading: false, state: { status: "ready" } }),
}))
vi.mock("@/hooks/use-ndjson-stream", () => ({ useNDJSONStream: () => {} }))
vi.mock("./matchup-decks", () => ({
  MatchupDecks: ({ decks, groupBy, onGroupByChange, loading }: ComponentProps<typeof MatchupDecks>) => (
    <div>
      <button onClick={() => onGroupByChange("deck")}>Decks</button>
      <button onClick={() => onGroupByChange("archetype")}>Archetypes</button>
      <output data-testid="grouping">{groupBy}</output>
      <output data-testid="decks">{loading ? "Loading" : decks.map(deck => deck.name).join(",")}</output>
    </div>
  ),
}))
vi.mock("./matchup-results", () => ({
  MatchupResults: ({ rows, loading }: ComponentProps<typeof MatchupResults>) =>
    <output data-testid="results">{loading ? "Loading" : rows.map(row => row.opponentArchetype).join(",")}</output>,
}))
vi.mock("./matchup-details", () => ({
  MatchupDetails: () => <aside className="matchup-details" data-testid="details">Details</aside>,
}))

afterEach(() => vi.unstubAllGlobals())

it("keeps the workspace intact and switches grouping labels together with the loaded results", async () => {
  vi.stubGlobal("ResizeObserver", class { observe() {} disconnect() {} })
  const deck = { id: 1, name: "My Burn Deck", archetype: "Burn", format: "Modern" }
  const archetype = { name: "Burn", archetype: "Burn", format: "Modern" }
  let resolve!: (response: Response) => void
  const replacement = new Promise<Response>(complete => { resolve = complete })
  vi.stubGlobal("fetch", vi.fn()
    .mockResolvedValueOnce(jsonResponse({ decks: [deck], matchups: [{ deck, opponentArchetype: "Control" }] }))
    .mockReturnValueOnce(replacement))
  function Dashboard() {
    const [selection, setSelection] = useState<DashboardDeckSelection>(null)
    return <MatchupAnalysis format="Modern" gameType="Constructed" selection={selection} onSelectionChange={setSelection} />
  }
  render(<Dashboard />)
  await waitFor(() => expect(screen.getByTestId("decks")).toHaveTextContent("My Burn Deck"))
  const details = screen.getByTestId("details")
  fireEvent.click(screen.getByRole("button", { name: "Archetypes" }))
  expect(screen.getByTestId("matchup-analysis")).toHaveAttribute("aria-busy", "true")
  expect(screen.getByTestId("grouping")).toHaveTextContent(/^deck$/)
  expect(screen.getByTestId("decks")).toHaveTextContent("My Burn Deck")
  expect(screen.getByTestId("results")).toHaveTextContent("Control")
  expect(screen.getByTestId("details")).toBe(details)
  await act(async () => resolve(jsonResponse({ decks: [archetype], matchups: [{ deck: archetype, opponentArchetype: "Control" }] })))
  expect(screen.getByTestId("grouping")).toHaveTextContent(/^archetype$/)
  expect(screen.getByTestId("decks")).toHaveTextContent(/^Burn$/)
  expect(screen.getByTestId("details")).toBe(details)
  expect(screen.getByTestId("matchup-analysis")).toHaveAttribute("aria-busy", "false")
})
