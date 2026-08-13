import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { jsonResponse } from '@/test/http'

vi.mock('@/hooks/use-client-state', () => ({
  useClientState: vi.fn(),
}))

import { useClientState } from '@/hooks/use-client-state'
import { useCollectionCards } from './use-collection'
import { updateDeckArchetype } from './use-decks'
import { useGames, useGamesHistory } from './use-games'
import { useTradeHistory, useTradeHistoryDetail } from './use-trade-history'
import { useTrades } from './use-trades'

const clientState = vi.mocked(useClientState)

const readyState = {
  state: {
    isConnected: true,
    isInitialized: true,
    status: 'ready' as const,
  },
  loading: false,
  isReady: true,
}

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  const promise = new Promise<T>(resolver => { resolve = resolver })
  return { promise, resolve }
}

describe('representative data workflows', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
    clientState.mockReturnValue(readyState)
  })

  it('collection hook loads a snapshot and exposes its card results', async () => {
    const snapshot = {
      hash: 'fixture-collection',
      itemCount: 1,
      uniqueCount: 1,
      totalQuantity: 4,
      timestamp: '2026-01-01T00:00:00Z',
      priceCacheExpiresAt: '2099-01-01T00:00:00Z',
      elapsedMilliseconds: 1,
      cards: [{ catalogId: 102392, name: 'Island', quantity: 4 }],
      products: [],
    }
    vi.mocked(fetch).mockResolvedValue(jsonResponse(snapshot))

    const { result } = renderHook(() => useCollectionCards())

    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.cards).toEqual(snapshot.cards)
    expect(fetch).toHaveBeenCalledWith(
      '/api/collection/cards',
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    )

    vi.mocked(fetch).mockRejectedValueOnce(new Error('collection unavailable'))
    await act(async () => {
      await result.current.refresh({ force: true })
    })
    expect(result.current.error).toBe('collection unavailable')
  })

  it('trade history hook exposes HTTP errors and sends filter pagination parameters', async () => {
    vi.mocked(fetch).mockResolvedValue(
      jsonResponse({}, { status: 503, statusText: 'Unavailable' }),
    )

    const { result } = renderHook(() => useTradeHistory(
      { search: 'PlayerA', result: 'success' as never },
      25,
    ))

    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.error).toBe('HTTP 503')
    expect(fetch).toHaveBeenCalledWith(
      '/api/trades/history?limit=25&search=PlayerA&result=success',
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    )
  })

  it('trade history ignores a response from an aborted filter request', async () => {
    const first = deferred<Response>()
    const second = deferred<Response>()
    vi.mocked(fetch)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)

    const { result, rerender } = renderHook(
      ({ search }) => useTradeHistory({ search }),
      { initialProps: { search: 'old' } },
    )
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1))
    const firstOptions = vi.mocked(fetch).mock.calls[0]?.[1] as RequestInit

    rerender({ search: 'new' })
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2))
    expect(firstOptions.signal?.aborted).toBe(true)

    second.resolve(jsonResponse({ items: [{ id: 2 }], nextBeforeId: null }))
    await waitFor(() => expect(result.current.items[0]?.id).toBe(2))
    first.resolve(jsonResponse({ items: [{ id: 1 }], nextBeforeId: null }))
    await act(async () => { await first.promise })
    expect(result.current.items[0]?.id).toBe(2)
  })

  it('trade detail ignores a response from a replaced request', async () => {
    const first = deferred<Response>()
    const second = deferred<Response>()
    vi.mocked(fetch)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)

    const { result, rerender } = renderHook(
      ({ id }) => useTradeHistoryDetail(id),
      { initialProps: { id: 1 } },
    )
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1))
    const firstOptions = vi.mocked(fetch).mock.calls[0]?.[1] as RequestInit

    rerender({ id: 2 })
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2))
    expect(firstOptions.signal?.aborted).toBe(true)

    second.resolve(jsonResponse({ summary: { id: 2 } }))
    await waitFor(() => expect(result.current.data?.summary?.id).toBe(2))
    first.resolve(jsonResponse({ summary: { id: 1 } }))
    await act(async () => { await first.promise })
    expect(result.current.data?.summary?.id).toBe(2)
  })

  it('trades refresh replaces the in-flight request', async () => {
    const first = deferred<Response>()
    const second = deferred<Response>()
    vi.mocked(fetch)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)

    const { result } = renderHook(() => useTrades())
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1))
    const firstOptions = vi.mocked(fetch).mock.calls[0]?.[1] as RequestInit

    await act(async () => { void result.current.refresh() })
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2))
    expect(firstOptions.signal?.aborted).toBe(true)

    const currentTrade = { id: 2 }
    second.resolve(jsonResponse({ currentTrade, posts: [], lastUpdated: null }))
    await waitFor(() => expect(result.current.data?.currentTrade?.id).toBe(2))
    first.resolve(jsonResponse({ currentTrade: { id: 1 }, posts: [], lastUpdated: null }))
    await act(async () => { await first.promise })
    expect(result.current.data?.currentTrade?.id).toBe(2)
  })

  it('game history ignores a response from a replaced filter request', async () => {
    const first = deferred<Response>()
    const second = deferred<Response>()
    vi.mocked(fetch)
      .mockReturnValueOnce(first.promise)
      .mockReturnValueOnce(second.promise)

    const { result, rerender } = renderHook(
      ({ format }) => useGamesHistory(1, 25, 'ALL', format),
      { initialProps: { format: 'Pauper' } },
    )
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(1))
    const firstOptions = vi.mocked(fetch).mock.calls[0]?.[1] as RequestInit

    rerender({ format: 'Modern' })
    await waitFor(() => expect(fetch).toHaveBeenCalledTimes(2))
    expect(firstOptions.signal?.aborted).toBe(true)

    second.resolve(jsonResponse({
      items: [{ id: 2, eventName: 'Modern event' }],
      totalCount: 1,
      page: 1,
      pageSize: 25,
      totalPages: 1,
    }))
    await waitFor(() => expect(result.current.data?.items[0]?.eventName)
      .toBe('Modern event'))
    first.resolve(jsonResponse({
      items: [{ id: 1, eventName: 'Pauper event' }],
      totalCount: 1,
      page: 1,
      pageSize: 25,
      totalPages: 1,
    }))
    await act(async () => { await first.promise })
    expect(result.current.data?.items[0]?.eventName).toBe('Modern event')
  })

  it('deck archetype mutation reports a server failure', async () => {
    vi.mocked(fetch).mockResolvedValue(
      jsonResponse({}, { status: 500, statusText: 'Failure' }),
    )

    await expect(updateDeckArchetype(42, 'Broken update'))
      .rejects.toThrow('HTTP 500')
  })

  it('trades hook remains idle while the client is disconnected', async () => {
    clientState.mockReturnValue({
      ...readyState,
      loading: false,
      isReady: false,
      state: { ...readyState.state, isConnected: false, isInitialized: false, status: 'disconnected' },
    })

    const { result } = renderHook(() => useTrades())

    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.data).toBeNull()
    expect(result.current.error).toBeNull()
    expect(fetch).not.toHaveBeenCalled()
  })

  it('deck archetype mutation sends the expected PUT request body', async () => {
    vi.mocked(fetch).mockResolvedValue(jsonResponse({
      revisionId: 42,
      archetype: 'Mono-Blue Delver',
    }))

    await act(async () => {
      await expect(updateDeckArchetype(42, 'Mono-Blue Delver')).resolves.toEqual({
        revisionId: 42,
        archetype: 'Mono-Blue Delver',
      })
    })

    expect(fetch).toHaveBeenCalledWith('/api/decks/42/archetype', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ archetype: 'Mono-Blue Delver' }),
    })
  })

  it('game hooks load dashboard statistics and paginated match history', async () => {
    const stats = {
      overallWinrate: 0.75,
      totalMatches: 4,
      wins: 3,
      losses: 1,
      ties: 0,
      playWinrate: 1,
      playMatches: 2,
      drawWinrate: 0.5,
      drawMatches: 2,
      averageDuration: '00:20:00',
      durationTwoGames: '00:15:00',
      durationThreeGames: '00:25:00',
    }
    const trend = [{
      date: 'Jan 1', rawDate: '2026-01-01', winrate: 0.75,
      matches: 4, rollingAvg: 0.75, ci95: null, ci80: null, ci50: null,
    }]
    const history = {
      items: [{
        id: 7, eventId: 8, eventName: 'Fixture Event', format: 'Pauper',
        startTime: '2026-01-01T00:00:00Z', result: 'Win', record: '2-1',
        duration: '00:20:00', isActive: false, isEvent: false,
      }],
      totalCount: 1, page: 1, pageSize: 25, totalPages: 1,
    }
    vi.mocked(fetch)
      .mockResolvedValueOnce(jsonResponse(['Pauper']))
      .mockResolvedValueOnce(jsonResponse(stats))
      .mockResolvedValueOnce(jsonResponse(trend))

    const dashboard = renderHook(() => useGames('ALL', 'Pauper'))
    await waitFor(() => expect(dashboard.result.current.loading).toBe(false))
    expect(dashboard.result.current.stats).toEqual(stats)
    expect(dashboard.result.current.trend).toEqual(trend)

    vi.mocked(fetch).mockResolvedValueOnce(jsonResponse(history))
    const matches = renderHook(() => useGamesHistory(1, 25, 'ALL', 'Pauper'))
    await waitFor(() => expect(matches.result.current.loading).toBe(false))
    expect(matches.result.current.data?.items[0]?.eventName).toBe('Fixture Event')
    expect(fetch).toHaveBeenLastCalledWith(
      expect.stringMatching(/^\/api\/games\/history\?page=1&pageSize=25&format=Pauper&maxDate=/),
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    )
  })
})
