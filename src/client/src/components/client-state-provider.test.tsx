import { render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { ClientStateProvider } from './client-state-provider'
import { useClientState } from '@/hooks/use-client-state'
import { ndjsonResponse } from '@/test/http'

function Probe() {
  const { state, loading, isReady } = useClientState()
  return (
    <output data-testid="state">
      {`${state.status}:${loading}:${isReady}`}
    </output>
  )
}

describe('ClientStateProvider', () => {
  it('client state provider initializes from the first NDJSON message', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      ndjsonResponse([
        { isConnected: true, isInitialized: true, status: 'ready' },
      ]),
    )
    vi.stubGlobal('fetch', fetchMock)

    const { unmount } = render(
      <ClientStateProvider>
        <Probe />
      </ClientStateProvider>,
    )

    await waitFor(() => expect(screen.getByTestId('state')).toHaveTextContent('ready:false:true'))
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/Client/WatchState',
      expect.objectContaining({ headers: { Accept: 'application/x-ndjson' } }),
    )
    unmount()
  })
})
