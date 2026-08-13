import { act, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { EventsProvider } from './use-events'
import { useEvents } from './events-context'
import type { NDJSONStreamOptions } from './use-ndjson-stream'

const streams: Array<NDJSONStreamOptions<unknown>> = []

vi.mock('@/hooks/use-client-state', () => ({
  useClientState: vi.fn(() => ({
    state: { isConnected: true, isInitialized: true, status: 'ready' },
    loading: false,
    isReady: true,
  })),
}))

vi.mock('./use-ndjson-stream', () => ({
  useNDJSONStream: vi.fn((options: NDJSONStreamOptions<unknown>) => {
    streams.push(options)
  }),
}))

function Probe() {
  const { activeGames, selectedEventId, setSelectedEventId } = useEvents()
  return (
    <>
      <output data-testid="events">{activeGames[0]?.name ?? 'empty'}</output>
      <output data-testid="selected">{selectedEventId ?? 'none'}</output>
      <button onClick={() => setSelectedEventId(activeGames[0]?.id ?? null)}>
        select
      </button>
    </>
  )
}

describe('event update and navigation workflow', () => {
  it('events provider promotes a streamed tournament and preserves the selected event', async () => {
    streams.length = 0
    render(
      <EventsProvider>
        <Probe />
      </EventsProvider>,
    )

    expect(streams.length).toBeGreaterThanOrEqual(3)
    act(() => streams[0]?.onMessage?.({
      id: 42,
      description: 'Fixture Pauper Event',
      format: 'Pauper',
      state: 'RoundInProgress',
      totalRounds: 3,
      startTime: '2026-01-01T00:00:00Z',
      endTime: '2026-01-01T01:00:00Z',
    }))

    await waitFor(() => expect(screen.getByTestId('events'))
      .toHaveTextContent('Fixture Pauper Event'))
    act(() => screen.getByRole('button', { name: 'select' }).click())
    expect(screen.getByTestId('selected')).toHaveTextContent('42')
  })
})
