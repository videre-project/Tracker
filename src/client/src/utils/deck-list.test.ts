import { describe, expect, it } from 'vitest'

import { buildDeckListText, getDeckFileName } from './deck-list'

describe('deck-list utilities', () => {
  it('deck list formatting combines repeated cards and separates the sideboard', () => {
    expect(buildDeckListText({
      mainboard: [
        { name: 'Island', quantity: 2 },
        { name: 'Island', quantity: 2 },
        { name: 'Counterspell', quantity: 4 },
      ],
      sideboard: [{ name: 'Dispel', quantity: 2 }],
    })).toBe('4 Island\n4 Counterspell\n\n2 Dispel')
  })

  it('deck filename formatting removes unsafe characters from the deck name', () => {
    expect(getDeckFileName('  Mono-U Delver / Test  ')).toBe('mono-u-delver-test.txt')
    expect(getDeckFileName('')).toBe('deck.txt')
  })
})
