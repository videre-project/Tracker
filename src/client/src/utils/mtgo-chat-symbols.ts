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

export type MtgoChatMarkupSymbol =
  | { type: "mana"; value: string }
  | { type: "chatSymbol"; value: string }

const MTGO_EXACT_MARKUP_SYMBOL_BY_TOKEN: Record<string, MtgoChatMarkupSymbol> = {
  "sW": { type: "mana", value: "W" },
  "sU": { type: "mana", value: "U" },
  "sB": { type: "mana", value: "B" },
  "sR": { type: "mana", value: "R" },
  "sG": { type: "mana", value: "G" },
  "s_": { type: "mana", value: "B/G" },
  "s=": { type: "mana", value: "B/R" },
  "s$": { type: "mana", value: "U/B" },
  "s`": { type: "mana", value: "U/R" },
  "s&amp,": { type: "mana", value: "G/U" },
  "s&": { type: "mana", value: "G/U" },
  "s-": { type: "mana", value: "G/W" },
  "s'": { type: "mana", value: "R/G" },
  "s~": { type: "mana", value: "R/W" },
  "s,": { type: "mana", value: "W/B" },
  "s+": { type: "mana", value: "W/U" },
  "s&gt,": { type: "mana", value: "2/G" },
  "s>": { type: "mana", value: "2/G" },
  "s&lt,": { type: "mana", value: "2/R" },
  "s<": { type: "mana", value: "2/R" },
  "s%": { type: "mana", value: "2/B" },
  "s@": { type: "mana", value: "2/U" },
  "s!": { type: "mana", value: "2/W" },
  "s0": { type: "mana", value: "0" },
  "s1": { type: "mana", value: "1" },
  "s2": { type: "mana", value: "2" },
  "s3": { type: "mana", value: "3" },
  "s4": { type: "mana", value: "4" },
  "s5": { type: "mana", value: "5" },
  "s6": { type: "mana", value: "6" },
  "s7": { type: "mana", value: "7" },
  "s8": { type: "mana", value: "8" },
  "s9": { type: "mana", value: "9" },
  "sa": { type: "mana", value: "10" },
  "sb": { type: "mana", value: "11" },
  "sc": { type: "mana", value: "12" },
  "sd": { type: "mana", value: "13" },
  "se": { type: "mana", value: "14" },
  "sf": { type: "mana", value: "15" },
  "sg": { type: "mana", value: "16" },
  "sh": { type: "mana", value: "17" },
  "si": { type: "mana", value: "18" },
  "sj": { type: "mana", value: "19" },
  "sk": { type: "mana", value: "20" },
  "sX": { type: "mana", value: "X" },
  "so": { type: "mana", value: "S" },
  "sT": { type: "mana", value: "T" },
  "sJ": { type: "mana", value: "Q" },
  "sTap": { type: "mana", value: "T" },
  "sV": { type: "chatSymbol", value: "arrow" },
  "sClone": { type: "chatSymbol", value: "clone" },
  "sCLONE": { type: "chatSymbol", value: "clone" },
  "sD": { type: "chatSymbol", value: "trophy" },
  "sY": { type: "chatSymbol", value: "sick" },
  "sF": { type: "chatSymbol", value: "frown" },
  "sS": { type: "chatSymbol", value: "smile" },
  "sMute": { type: "chatSymbol", value: "mute" },
  "sWiz": { type: "chatSymbol", value: "wiz" },
  "sHat": { type: "chatSymbol", value: "wizhat" },
  "sZ": { type: "chatSymbol", value: "zzz" },
  "sAdept": { type: "chatSymbol", value: "adept" },
  "sClan": { type: "chatSymbol", value: "clan" },
  "sPig": { type: "chatSymbol", value: "pig" },
  "sLizard": { type: "chatSymbol", value: "lizard" },
  "sEventTicket": { type: "chatSymbol", value: "event ticket" },
  "sLifeHeart": { type: "chatSymbol", value: "life" },
  "sCardHand": { type: "chatSymbol", value: "hand" }
}

const MTGO_CASE_INSENSITIVE_MARKUP_SYMBOL_BY_TOKEN:
  Record<string, MtgoChatMarkupSymbol> = {
    "sadept": { type: "chatSymbol", value: "adept" },
    "scardhand": { type: "chatSymbol", value: "hand" },
    "sclan": { type: "chatSymbol", value: "clan" },
    "sclone": { type: "chatSymbol", value: "clone" },
    "seventticket": { type: "chatSymbol", value: "event ticket" },
    "shat": { type: "chatSymbol", value: "wizhat" },
    "slifeheart": { type: "chatSymbol", value: "life" },
    "slizard": { type: "chatSymbol", value: "lizard" },
    "smute": { type: "chatSymbol", value: "mute" },
    "spig": { type: "chatSymbol", value: "pig" },
    "stap": { type: "mana", value: "T" },
    "swiz": { type: "chatSymbol", value: "wiz" }
  }

export function normalizeMtgoChatSymbolToken(symbol: string): string {
  return symbol
    .trim()
    .toLowerCase()
    .replace(/[_-]+/g, " ")
    .replace(/\s+/g, " ")
}

export function getMtgoChatMarkupSymbol(
  symbol: string
): MtgoChatMarkupSymbol | null {
  const token = symbol.trim()
  const exactSymbol = MTGO_EXACT_MARKUP_SYMBOL_BY_TOKEN[token]
  if (exactSymbol) return exactSymbol

  const caseInsensitiveSymbol =
    MTGO_CASE_INSENSITIVE_MARKUP_SYMBOL_BY_TOKEN[token.toLowerCase()]
  if (caseInsensitiveSymbol) return caseInsensitiveSymbol

  const chatSymbol = normalizeMtgoChatSymbolToken(token)
  return MTGO_CHAT_SYMBOL_FILE_BY_TOKEN[chatSymbol]
    ? { type: "chatSymbol", value: chatSymbol }
    : null
}

export function getMtgoChatSymbolImagePath(symbol: string): string | null {
  const fileName = MTGO_CHAT_SYMBOL_FILE_BY_TOKEN[
    normalizeMtgoChatSymbolToken(symbol)
  ]
  return fileName ? `/mtgo-chat-symbols/${fileName}` : null
}
