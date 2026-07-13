/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { getMtgoChatSymbolUrl } from "@/utils/videre-cdn"

export type MtgoChatMarkupSymbol = {
  type: "chatSymbol"
  value: string
}

const NORMALIZED_CHAT_SYMBOL_PATTERN =
  /^[a-z0-9]+(?:[\s_-]+[a-z0-9]+)*$/

export function normalizeMtgoChatSymbolToken(symbol: string): string {
  return symbol
    .trim()
    .toLowerCase()
    .replace(/[\s_]+/g, "-")
    .replace(/-+/g, "-")
}

export function getMtgoChatMarkupSymbol(
  symbol: string,
): MtgoChatMarkupSymbol | null {
  const token = symbol.trim()
  if (!NORMALIZED_CHAT_SYMBOL_PATTERN.test(token)) return null

  return {
    type: "chatSymbol",
    value: token,
  }
}

export function getMtgoChatSymbolImagePath(symbol: string): string | null {
  const token = symbol.trim()
  if (!NORMALIZED_CHAT_SYMBOL_PATTERN.test(token)) return null

  const normalizedSymbol = normalizeMtgoChatSymbolToken(token)
  const extension = normalizedSymbol === "clone" ? "svg" : "png"
  return getMtgoChatSymbolUrl(`${normalizedSymbol}.${extension}`)
}
