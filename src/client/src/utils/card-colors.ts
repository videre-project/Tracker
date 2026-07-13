/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { VIDERE_OPENAPI_ENUMS } from "@/types/videre.g"

export const VIDERE_CARD_COLORS =
  VIDERE_OPENAPI_ENUMS.components.schemas.Card.properties.colors.items

export type VidereCardColor = (typeof VIDERE_CARD_COLORS)[number]

export const COLORLESS_CARD_COLOR = "C" as const
const COLORLESS_CARD_COLORS = [COLORLESS_CARD_COLOR] as const

export function getDisplayCardColors(
  colors?: readonly string[] | null,
): readonly string[] {
  return colors && colors.length > 0 ? colors : COLORLESS_CARD_COLORS
}
