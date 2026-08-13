import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { ChangeEvent } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

type CollectionCard = { name: string; catalogId: number }
type CollectionLayoutProps = {
  search: string
  onSearchChange: (value: string) => void
  cards: CollectionCard[]
  error?: string | null
}

function TestCollectionLayout(props: CollectionLayoutProps) {
  return (
    <div>
      <input
        aria-label="collection search"
        value={props.search}
        onChange={(event: ChangeEvent<HTMLInputElement>) => props.onSearchChange(event.target.value)}
      />
      <div data-testid="collection-cards">{props.cards.map(card => card.name).join(',')}</div>
      <div data-testid="collection-error">{props.error ?? ''}</div>
    </div>
  )
}

function TestCardFilterPanel() {
  return <div data-testid="card-filter-panel" />
}

vi.mock('@videreproject/ui', async () => {
  return {
    CollectionLayout: TestCollectionLayout,
    CardFilterPanel: TestCardFilterPanel,
    DEFAULT_CARD_FILTERS: {},
    buildCardSearchQuery: (search: string) => search.trim(),
    getActiveCardFilterCount: () => 0,
  }
})

vi.mock('@/hooks/use-collection', () => ({
  useCollectionCards: vi.fn(() => ({
    snapshot: { uniqueCount: 1, totalQuantity: 4 },
    cards: [{ catalogId: 102392, name: 'Island', quantity: 4 }],
    products: [],
    loading: false,
    error: null,
  })),
}))

vi.mock('@/hooks/use-collection-selection', () => ({
  useCollectionSelection: vi.fn(() => ({
    selection: null,
    loading: false,
    error: null,
  })),
}))

import Collection from './collection'

describe('Collection page', () => {
  beforeEach(() => {
    vi.stubGlobal('fetch', vi.fn())
  })

  it('collection page search posts the query and hides cards outside the result set', async () => {
    vi.mocked(fetch).mockResolvedValue(new Response(
      JSON.stringify({ catalogIds: [] }),
      { status: 200, headers: { 'content-type': 'application/json' } },
    ))

    render(<Collection />)
    expect(screen.getByTestId('collection-cards')).toHaveTextContent('Island')

    fireEvent.change(screen.getByRole('textbox', { name: 'collection search' }), {
      target: { value: 'island' },
    })

    await waitFor(() => expect(fetch).toHaveBeenCalledWith(
      '/api/collection/cards/search',
      expect.objectContaining({
        method: 'POST',
        body: JSON.stringify({ query: 'island' }),
      }),
    ), { timeout: 1000 })
    await waitFor(() => {
      expect(screen.getByTestId('collection-cards')).toHaveTextContent('')
    })
    expect(screen.getByTestId('collection-error')).toHaveTextContent('')
  })
})
