import { fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { useGamesHistory } from '@/hooks/use-games'

type HistoryItem = { eventName: string }
type HistoryLayoutProps = {
  items: HistoryItem[]
  onRowClick: (item: HistoryItem) => void
  onNextPage: () => void
  pagination?: { page: number } | null
}

function TestHistoryLayout(props: HistoryLayoutProps) {
  return (
    <div>
      <div data-testid="history-items">{props.items.map(item => item.eventName).join(',')}</div>
      <button type="button" onClick={() => props.onRowClick(props.items[0]!)}>open first</button>
      <button type="button" onClick={props.onNextPage}>next page</button>
      <div data-testid="history-page">{props.pagination?.page ?? 'none'}</div>
    </div>
  )
}

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
  useGames: vi.fn(() => ({ formats: ['Pauper'], loading: false })),
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

describe('History page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('history page renders matches, requests the next page, and navigates to a match', () => {
    render(
      <MemoryRouter initialEntries={['/history']}>
        <Routes>
          <Route path="*" element={<><History /><LocationProbe /></>} />
        </Routes>
      </MemoryRouter>,
    )

    expect(screen.getByTestId('history-items')).toHaveTextContent('Fixture Event')
    expect(screen.getByTestId('history-page')).toHaveTextContent('1')

    fireEvent.click(screen.getByRole('button', { name: 'next page' }))
    expect(vi.mocked(useGamesHistory)).toHaveBeenLastCalledWith(
      2,
      50,
      expect.anything(),
      '',
    )

    fireEvent.click(screen.getByRole('button', { name: 'open first' }))
    expect(screen.getByTestId('location')).toHaveTextContent('/history/7')
  })
})
