/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

export const MTGO_CHAT_SYMBOL_FILE_BY_TOKEN: Record<string, string> = {
  "adept": "adept.png",
  "arrow": "arrow.png",
  "clan": "clan.png",
  "clone": "clone.svg",
  "event ticket": "event-ticket.png",
  "frown": "frown.png",
  "hand": "hand.png",
  "life": "life.png",
  "lizard": "lizard.png",
  "mute": "mute.png",
  "pig": "pig.png",
  "sick": "sick.png",
  "smile": "smile.png",
  "trophy": "trophy.png",
  "wiz": "wiz.png",
  "wizhat": "wizhat.png",
  "zzz": "zzz.png"
}

export function getMtgoChatSymbolImagePath(symbol: string): string | null {
  const fileName = MTGO_CHAT_SYMBOL_FILE_BY_TOKEN[symbol]
  return fileName ? `/mtgo-chat-symbols/${fileName}` : null
}
