/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { act, renderHook, waitFor } from "@testing-library/react"
import { beforeEach, describe, expect, it, vi } from "vitest"
import { jsonResponse } from "@/test/http"
import { useDeckDetail } from "./use-decks"
import { useDeckHistory, type DeckHistoryData } from "./use-deck-history"

const { preloadCardImages } = vi.hoisted(() => ({ preloadCardImages: vi.fn() }))
vi.mock("./use-client-state", () => ({ useClientState: () => ({ isReady: true, loading: false }) }))
vi.mock("@videreproject/ui", () => ({
  useCardMedia: () => ({ preloadCardImages }),
}))

function pendingResponse() {
  let resolve!: (response: Response) => void
  const promise = new Promise<Response>(complete => { resolve = complete })
  return { promise, resolve }
}

function history(revisionId: number): DeckHistoryData {
  return {
    currentRevisionId: revisionId, cardGroupingId: revisionId,
    name: `Deck ${revisionId}`, format: "Pioneer",
    revisions: [{
      revisionId, cardGroupingId: revisionId, observedAt: "2026-10-20T00:00:00Z",
      timestamp: "2026-10-20T00:00:00Z", name: `Deck ${revisionId}`, format: "Pioneer",
      mainboardCount: 1, sideboardCount: 0, colors: [],
      mainboard: [{ catalogId: revisionId, name: `Card ${revisionId}`, quantity: 1 }],
      sideboard: [], changesFromPrevious: [], changesFromLatest: [],
    }],
  }
}

describe("deck request ownership", () => {
  beforeEach(() => vi.stubGlobal("fetch", vi.fn()))

  it("starts new detail requests immediately and ignores late older responses", async () => {
    const first = pendingResponse(), second = pendingResponse()
    vi.mocked(fetch).mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    const { result, rerender } = renderHook(({ id }) => useDeckDetail(id), {
      initialProps: { id: "35" },
    })
    const firstSignal = vi.mocked(fetch).mock.calls[0][1]?.signal
    rerender({ id: "80" })
    expect(firstSignal?.aborted).toBe(true)
    expect(fetch).toHaveBeenCalledTimes(2)
    expect(String(vi.mocked(fetch).mock.calls[1][0])).toContain("/decks/80")
    await act(async () => second.resolve(jsonResponse({ revisionId: 80 })))
    await waitFor(() => expect(result.current.detail?.revisionId).toBe(80))
    await act(async () => first.resolve(jsonResponse({ revisionId: 35 })))
    expect(result.current.detail?.revisionId).toBe(80)
  })

  it("hides old cards on the first render for a new detail request", async () => {
    vi.mocked(fetch).mockResolvedValueOnce(jsonResponse({ revisionId: 35 }))
      .mockImplementationOnce(() => new Promise(() => {}))
    const seen: ReturnType<typeof useDeckDetail>[] = []
    const { result, rerender, unmount } = renderHook(({ id }) => {
      const value = useDeckDetail(id)
      seen.push(value)
      return value
    }, { initialProps: { id: "35" } })
    await waitFor(() => expect(result.current.detail?.revisionId).toBe(35))
    seen.length = 0
    rerender({ id: "80" })
    expect(seen[0]).toMatchObject({ detail: null, loading: true, error: null })
    const signal = vi.mocked(fetch).mock.calls[1][1]?.signal
    unmount()
    expect(signal?.aborted).toBe(true)
  })

  it("never exposes another deck's history or selected revision, even for one render", async () => {
    const next = pendingResponse()
    vi.mocked(fetch).mockResolvedValueOnce(jsonResponse(history(35))).mockReturnValueOnce(next.promise)
    const seen: ReturnType<typeof useDeckHistory>[] = []
    const { result, rerender } = renderHook(({ id }) => {
      const value = useDeckHistory(id)
      seen.push(value)
      return value
    }, { initialProps: { id: "35" } })
    await waitFor(() => expect(result.current.history?.currentRevisionId).toBe(35))
    act(() => result.current.setSelectedRevisionId(35))
    seen.length = 0
    rerender({ id: "80" })
    expect(seen[0]).toMatchObject({ history: null, selectedRevisionId: null, loading: true })
    expect(seen[0].selectedRevisionCards).toEqual([])
    await act(async () => next.resolve(jsonResponse(history(80))))
    expect(result.current.history?.currentRevisionId).toBe(80)
    expect(result.current.selectedRevisionId).toBeNull()
  })

  it("ignores an obsolete history response arriving after the current response", async () => {
    const first = pendingResponse(), second = pendingResponse()
    vi.mocked(fetch).mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    const { result, rerender } = renderHook(({ id }) => useDeckHistory(id), {
      initialProps: { id: "35" },
    })
    const signal = vi.mocked(fetch).mock.calls[0][1]?.signal
    rerender({ id: "80" })
    expect(signal?.aborted).toBe(true)
    await act(async () => second.resolve(jsonResponse(history(80))))
    await act(async () => first.resolve(jsonResponse(history(35))))
    expect(result.current.history?.currentRevisionId).toBe(80)
    expect(result.current.selectedRevisionCards[0]?.catalogId).toBe(80)
  })
})
