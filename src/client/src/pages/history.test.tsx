import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { HistoryLayoutProps } from '@videreproject/ui'

import { useGamesHistory } from '@/hooks/use-games'
import { ApplicationSettingsContext } from '@/hooks/use-application-settings'
import { defaultPreferences } from '@/lib/application-preferences'

function TestHistoryLayout(props: HistoryLayoutProps) {
  return (
    <div>
      <div data-testid="history-items">{props.items.map(item => item.eventName).join(',')}</div>
      <button type="button" onClick={() => props.onRowClick?.(props.items[0]!)}>open first</button>
      <button type="button" onClick={() => props.pagination?.onPageIndexChange(props.pagination.pageIndex + 1)}>next page</button>
      <div data-testid="history-page">{props.pagination ? props.pagination.pageIndex + 1 : 'none'}</div>
    </div>
  )
}

// Exercise page data and callbacks here; shared layout and native-window behavior require browser checks.
vi.mock('@videreproject/ui', async () => {
  return {
    HistoryLayout: TestHistoryLayout,
    compareFormats: (left: string, right: string) => left.localeCompare(right),
    isLimitedFormat: (format: string) => /draft|sealed|limited/i.test(format),
  }
})

vi.mock('@/hooks/use-client-state', () => ({
  useClientState: () => ({ isReady: true }),
}))

vi.mock('@/hooks/use-ndjson-stream', () => ({
  useNDJSONStream: vi.fn(),
}))

vi.mock('@/hooks/use-games', () => ({
  useGameFormats: vi.fn(() => ({ formats: ['Pauper'], loading: false, error: null })),
  useGamesHistory: vi.fn(() => ({
    data: {
      items: [{
        id: 7,
        eventId: 8,
        eventName: 'Fixture Event',
        format: 'Pauper',
        isEvent: false,
      }],
      page: 1,
      pageSize: 50,
      totalPages: 2,
      totalCount: 51,
    },
    loading: false,
    error: null,
  })),
}))

import History from './history'

function LocationProbe() {
  return <output data-testid="location">{useLocation().pathname}</output>
}

describe('History page wiring', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('passes matches to the layout, requests the next page, and navigates to a match', () => {
    render(
      <ApplicationSettingsContext.Provider value={{ preferences: defaultPreferences, settings: { application: defaultPreferences }, error: null, setSettings: vi.fn(), refresh: vi.fn() }}>
        <MemoryRouter initialEntries={['/history']}>
          <Routes>
            <Route path="*" element={<><History /><LocationProbe /></>} />
          </Routes>
        </MemoryRouter>
      </ApplicationSettingsContext.Provider>,
    )

    expect(screen.getByTestId('history-items')).toHaveTextContent('Fixture Event')
    expect(screen.getByTestId('history-page')).toHaveTextContent('1')

    fireEvent.click(screen.getByRole('button', { name: 'next page' }))
    expect(vi.mocked(useGamesHistory)).toHaveBeenLastCalledWith(
      2,
      50,
      expect.anything(),
      '',
      undefined,
      'gameType=All',
    )

    fireEvent.click(screen.getByRole('button', { name: 'open first' }))
    expect(screen.getByTestId('location')).toHaveTextContent('/history/7')
  })
})
