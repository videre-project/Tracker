/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { getManaSymbolUrl } from "@/utils/videre-cdn"

const MANA_SYMBOL_PATTERN =
  /^(?:\d+|[A-Z]+|[A-Z0-9]+(?:\/[A-Z0-9]+){1,2})$/

export function getManaSymbolSvgPath(symbol: string): string | null {
  const normalizedSymbol = symbol.trim().toUpperCase()
  if (!MANA_SYMBOL_PATTERN.test(normalizedSymbol)) return null

  return getManaSymbolUrl(`${normalizedSymbol.replace(/\//g, "")}.svg`)
}
