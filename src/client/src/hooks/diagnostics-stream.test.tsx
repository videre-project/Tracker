import { act, renderHook } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import type { NDJSONStreamOptions } from './use-ndjson-stream'

const streamOptions: { current?: NDJSONStreamOptions<unknown> } = {}

vi.mock('./use-ndjson-stream', () => ({
  useNDJSONStream: vi.fn((options: NDJSONStreamOptions<unknown>) => {
    streamOptions.current = options
  }),
}))

import { useLogsStream } from './use-logs-stream'

describe('diagnostics log workflow', () => {
  it('diagnostics log stream orders entries and clears its buffer on stream end', () => {
    const { result } = renderHook(() => useLogsStream())
    const onMessage = streamOptions.current?.onMessage
    const onEnd = streamOptions.current?.onEnd
    expect(onMessage).toBeDefined()
    expect(onEnd).toBeDefined()

    act(() => {
      onMessage?.({
        timestamp: '2026-01-01T00:00:02Z', source: 'Tracker',
        level: 'Info', logger: 'Tracker', message: 'newer',
      })
      onMessage?.({
        timestamp: '2026-01-01T00:00:01Z', source: 'SDK',
        level: 'Debug', logger: 'SDK', message: 'older',
      })
    })

    expect(result.current.logs.map(log => log.message)).toEqual(['older', 'newer'])
    act(() => onEnd?.())
    expect(result.current.logs).toEqual([])
  })
})
