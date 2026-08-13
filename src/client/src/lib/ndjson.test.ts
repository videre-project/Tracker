import { renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { fetchNDJSON, parseNDJSONStream } from './ndjson'
import { useNDJSONStream } from '@/hooks/use-ndjson-stream'
import { jsonResponse } from '@/test/http'

function streamFrom(...chunks: string[]) {
  const encoder = new TextEncoder()
  return new ReadableStream<Uint8Array>({
    start(controller) {
      for (const chunk of chunks) controller.enqueue(encoder.encode(chunk))
      controller.close()
    },
  })
}

describe('NDJSON helpers', () => {
  it('NDJSON parsing reconstructs split lines and ignores blank lines', async () => {
    const reader = streamFrom('{"id":1}\n{"id":', '2}\n\n').getReader()

    await expect(parseNDJSONStream<{ id: number }>(reader)).resolves.toEqual([
      { id: 1 },
      { id: 2 },
    ])
  })

  it('NDJSON streaming sends the accept header and rejects failed responses', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(streamFrom('{"status":"ready"}\n'), {
        status: 200,
      }))
      .mockResolvedValueOnce(jsonResponse({}, { status: 503, statusText: 'Unavailable' }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(fetchNDJSON('/api/client/watchstate')).resolves.toEqual([
      { status: 'ready' },
    ])
    expect(fetchMock.mock.calls[0]?.[1]).toMatchObject({
      headers: { Accept: 'application/x-ndjson' },
    })
    await expect(fetchNDJSON('/api/client/watchstate')).rejects.toThrow('503')
  })

  it('NDJSON stream cleanup aborts the request and cancels the reader on unmount', async () => {
    let requestSignal: AbortSignal | undefined
    let readerCancelled = false
    const stream = new ReadableStream<Uint8Array>({
      start(controller) {
        controller.enqueue(new TextEncoder().encode('{"id":1}\n'))
      },
      cancel() {
        readerCancelled = true
      },
    })
    vi.stubGlobal('fetch', vi.fn((_url: string, options: RequestInit) => {
      requestSignal = options.signal ?? undefined
      return Promise.resolve(new Response(stream, {
        status: 200,
        headers: { 'content-type': 'application/x-ndjson' },
      }))
    }))
    const onMessage = vi.fn()

    const { unmount } = renderHook(() => useNDJSONStream({
      url: '/api/diagnostics/watchlogs',
      onMessage,
      autoReconnect: false,
    }))

    await waitFor(() => expect(onMessage).toHaveBeenCalledWith({ id: 1 }))
    unmount()

    expect(requestSignal?.aborted).toBe(true)
    await waitFor(() => expect(readerCancelled).toBe(true))
  })
})
