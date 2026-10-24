/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { act, renderHook, waitFor } from "@testing-library/react"
import { beforeEach, describe, expect, it, vi } from "vitest"
import { jsonResponse } from "@/test/http"
import { useSessionQuery } from "./use-session-query"

vi.mock("./use-client-state", () => ({
  useClientState: () => ({ isReady: true, loading: false }),
}))

function pendingResponse() {
  let resolve!: (response: Response) => void
  const promise = new Promise<Response>(complete => { resolve = complete })
  return { promise, resolve }
}

describe("matchup request ownership", () => {
  beforeEach(() => vi.stubGlobal("fetch", vi.fn()))

  it("hides the previous matchup on the first render for new filters", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(jsonResponse({ deck: 1 }))
      .mockImplementationOnce(() => new Promise(() => {}))
    const seen: ReturnType<typeof useSessionQuery>[] = []
    const { result, rerender } = renderHook(({ path }) => {
      const value = useSessionQuery(path)
      seen.push(value)
      return value
    }, { initialProps: { path: "/api/games/matchups?deckId=1" } })
    await waitFor(() => expect(result.current.data).toEqual({ deck: 1 }))
    seen.length = 0
    rerender({ path: "/api/games/matchups?deckId=2" })
    expect(seen[0]).toMatchObject({ data: null, error: null, loading: true })
  })

  it("aborts obsolete requests and ignores their late results", async () => {
    const first = pendingResponse(), second = pendingResponse()
    vi.mocked(fetch).mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    const { result, rerender } = renderHook(({ path }) => useSessionQuery(path), {
      initialProps: { path: "/api/games/matchups?deckId=1" },
    })
    const signal = vi.mocked(fetch).mock.calls[0][1]?.signal
    rerender({ path: "/api/games/matchups?deckId=2" })
    expect(signal?.aborted).toBe(true)
    await act(async () => second.resolve(jsonResponse({ deck: 2 })))
    await waitFor(() => expect(result.current.data).toEqual({ deck: 2 }))
    await act(async () => first.resolve(jsonResponse({ deck: 1 })))
    expect(result.current.data).toEqual({ deck: 2 })
  })

  it("keeps the current result visible during a background refresh", async () => {
    const next = pendingResponse()
    vi.mocked(fetch).mockResolvedValueOnce(jsonResponse({ matches: 1 })).mockReturnValueOnce(next.promise)
    const { result } = renderHook(() => useSessionQuery("/api/games/matchups"))
    await waitFor(() => expect(result.current.data).toEqual({ matches: 1 }))
    act(() => result.current.refresh())
    expect(result.current).toMatchObject({ data: { matches: 1 }, loading: true })
    await act(async () => next.resolve(jsonResponse({ matches: 2 })))
    await waitFor(() => expect(result.current).toMatchObject({ data: { matches: 2 }, loading: false }))
  })

  it("retains the displayed grouping until its replacement arrives, but clears it for other filters", async () => {
    const next = pendingResponse()
    vi.mocked(fetch).mockResolvedValueOnce(jsonResponse({ grouping: "deck" }))
      .mockReturnValueOnce(next.promise).mockImplementationOnce(() => new Promise(() => {}))
    const { result, rerender } = renderHook(({ path, filters }) => useSessionQuery(path, { retainDataKey: filters }), {
      initialProps: { path: "/matchups?groupBy=deck", filters: "Modern" },
    })
    await waitFor(() => expect(result.current.data).toEqual({ grouping: "deck" }))
    rerender({ path: "/matchups?groupBy=archetype", filters: "Modern" })
    expect(result.current).toMatchObject({
      data: { grouping: "deck" }, dataPath: "/matchups?groupBy=deck", loading: true,
    })
    await act(async () => next.resolve(jsonResponse({ grouping: "archetype" })))
    expect(result.current).toMatchObject({
      data: { grouping: "archetype" }, dataPath: "/matchups?groupBy=archetype", loading: false,
    })
    rerender({ path: "/matchups?groupBy=archetype&format=Legacy", filters: "Legacy" })
    expect(result.current).toMatchObject({ data: null, dataPath: null, loading: true })
  })

  it("does not let a late grouping response replace a newer selection", async () => {
    const archetypes = pendingResponse(), decks = pendingResponse()
    vi.mocked(fetch).mockResolvedValueOnce(jsonResponse({ grouping: "deck" }))
      .mockReturnValueOnce(archetypes.promise).mockReturnValueOnce(decks.promise)
    const { result, rerender } = renderHook(({ path }) => useSessionQuery(path, { retainDataKey: "Modern" }), {
      initialProps: { path: "/matchups?groupBy=deck" },
    })
    await waitFor(() => expect(result.current.data).toEqual({ grouping: "deck" }))
    rerender({ path: "/matchups?groupBy=archetype" })
    rerender({ path: "/matchups?groupBy=deck" })
    await act(async () => archetypes.resolve(jsonResponse({ grouping: "archetype" })))
    expect(result.current).toMatchObject({ data: { grouping: "deck" }, dataPath: "/matchups?groupBy=deck" })
    await act(async () => decks.resolve(jsonResponse({ grouping: "deck", updated: true })))
    expect(result.current.data).toEqual({ grouping: "deck", updated: true })
  })
})
