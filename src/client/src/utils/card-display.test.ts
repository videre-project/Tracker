import { describe, expect, it } from 'vitest'

import { COLORLESS_CARD_COLOR, getDisplayCardColors } from './card-colors'
import { formatCardRarity } from './card-rarity'
import { normalizeFormatName } from './event-format'

describe('card and event display utilities', () => {
  it('card colors default to colorless when a card has no colors', () => {
    expect(getDisplayCardColors([])).toEqual([COLORLESS_CARD_COLOR])
    expect(getDisplayCardColors(['U', 'B'])).toEqual(['U', 'B'])
  })

  it('card metadata formats rarity and removes padding from event format names', () => {
    expect(formatCardRarity('mythic')).toBe('Mythic')
    expect(normalizeFormatName('Pauper0\0\0')).toBe('Pauper')
    expect(normalizeFormatName('Commander x3O')).toBe('Commander x3')
  })
})
