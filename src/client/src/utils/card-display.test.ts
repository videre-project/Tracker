import { describe, expect, it } from 'vitest'

import { normalizeFormatName } from './event-format'

describe('card and event display utilities', () => {
  it('removes padding from event format names', () => {
    expect(normalizeFormatName('Pauper0\0\0')).toBe('Pauper')
    expect(normalizeFormatName('Commander x3O')).toBe('Commander x3')
  })
})
