/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { CARD_COLORS, type CardColor } from '@videreproject/constants'

export const VIDERE_CARD_COLORS =
  CARD_COLORS.map(color => color.symbol)

export type VidereCardColor = CardColor['symbol']

export const COLORLESS_CARD_COLOR = "C" as const
const COLORLESS_CARD_COLORS = [COLORLESS_CARD_COLOR] as const

export function getDisplayCardColors(
  colors?: readonly string[] | null,
): readonly string[] {
  return colors && colors.length > 0 ? colors : COLORLESS_CARD_COLORS
}
