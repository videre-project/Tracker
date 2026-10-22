/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { act, renderHook, waitFor } from "@testing-library/react"
import { beforeEach, describe, expect, it, vi } from "vitest"
import { usePersonalActivity } from "./use-personal-activity"
import type { PersonalActivityDTO } from "@/types/api"

const client = vi.hoisted(() => ({ isReady: true, loading: false }))
vi.mock("./use-client-state", () => ({ useClientState: () => client }))

const event: PersonalActivityDTO = {
  id: "tournament-1", eventId: 1, kind: "tournament", name: "Challenge", format: "Pioneer",
  status: "registered", stateLabel: "Registered",
}

function activityStream() {
  let controller!: ReadableStreamDefaultController<Uint8Array>
  const stream = new ReadableStream<Uint8Array>({ start(value) { controller = value } })
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(stream)))
  return {
    send: (items: PersonalActivityDTO[]) => act(() => controller.enqueue(new TextEncoder().encode(JSON.stringify(items) + "\n"))),
    fail: () => act(() => controller.error(new Error("Connection lost"))),
  }
}

describe("personal sidebar activity", () => {
  beforeEach(() => { client.isReady = true })

  it("moves registrations into active play and replaces removed or completed entries", async () => {
    const stream = activityStream()
    const { result, unmount } = renderHook(() => usePersonalActivity())
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce())
    stream.send([event])
    await waitFor(() => expect(result.current.registered).toEqual([event]))
    expect(result.current.active).toEqual([])
    const playing = { ...event, status: "active" as const,
      match: { id: 20, opponent: "Opponent", gameNumber: 1, status: "Game in progress" } }
    stream.send([playing])
    await waitFor(() => expect(result.current.active).toEqual([playing]))
    expect(result.current.registered).toEqual([])
    stream.send([])
    await waitFor(() => expect(result.current.active).toEqual([]))
    expect(result.current.loading).toBe(false)
    unmount()
  })

  it("aborts the subscription and clears personal data on disconnect or disable", async () => {
    const stream = activityStream()
    const { result, rerender, unmount } = renderHook(({ enabled }) => usePersonalActivity(enabled), {
      initialProps: { enabled: true },
    })
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce())
    const signal = vi.mocked(fetch).mock.calls[0][1]?.signal
    stream.send([event])
    await waitFor(() => expect(result.current.registered).toHaveLength(1))
    client.isReady = false
    rerender({ enabled: true })
    expect(result.current.registered).toEqual([])
    expect(signal?.aborted).toBe(true)
    client.isReady = true
    rerender({ enabled: false })
    expect(result.current.registered).toEqual([])
    expect(result.current.loading).toBe(false)
    unmount()
  })

  it("clears stale registrations and reports a lost activity connection", async () => {
    const stream = activityStream()
    vi.spyOn(console, "error").mockImplementation(() => {})
    const { result, unmount } = renderHook(() => usePersonalActivity())
    await waitFor(() => expect(fetch).toHaveBeenCalledOnce())
    stream.send([event])
    await waitFor(() => expect(result.current.registered).toHaveLength(1))
    stream.fail()
    await waitFor(() => expect(result.current.error).toContain("Reconnecting"))
    expect(result.current.registered).toEqual([])
    unmount()
  })
})
