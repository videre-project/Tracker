/** @file
  Copyright (c) 2025, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import React from "react"
import { getManaSymbolSvgPath } from "@/utils/mana-symbols"
import { getMtgoChatSymbolImagePath } from "@/utils/mtgo-chat-symbols"

interface ParsedPart {
  type: "text" | "purple" | "card" | "mana" | "chatSymbol" | "italic"
  value: string
  cardId?: number
  textureId?: number
}

function renderTextWithInlineMana(
  text: string,
  keyPrefix: string,
  symbolClassName: string,
): React.ReactNode[] {
  const nodes: React.ReactNode[] = []
  const re = /\{([^}]+)\}/g
  let cursor = 0
  let match: RegExpExecArray | null
  let idx = 0

  while ((match = re.exec(text)) !== null) {
    if (match.index > cursor) {
      nodes.push(
        <React.Fragment key={`${keyPrefix}-t-${idx++}`}>
          {text.slice(cursor, match.index)}
        </React.Fragment>
      )
    }

    const symbol = match[1]
    const symbolPath = getManaSymbolSvgPath(symbol)
    if (symbolPath) {
      nodes.push(
        <img
          key={`${keyPrefix}-m-${idx++}`}
          src={symbolPath}
          alt={symbol}
          className={symbolClassName}
        />
      )
    } else {
      nodes.push(
        <React.Fragment key={`${keyPrefix}-r-${idx++}`}>
          {match[0]}
        </React.Fragment>
      )
    }

    cursor = re.lastIndex
  }

  if (cursor < text.length) {
    nodes.push(
      <React.Fragment key={`${keyPrefix}-t-${idx++}`}>
        {text.slice(cursor)}
      </React.Fragment>
    )
  }

  return nodes
}

/**
 * Parses MTGO game log markup into structured parts.
 *
 * Format reference (from ChatMarkupParser in MTGO client):
 *   @P<name>   — player name (ends at space or next @)
 *   @[<cardName>@:<nameToken>,<textureId>:@] — card link
 *   @i         — italics marker (stripped)
 *   {<symbol>} — mana/tap symbol
 */
export function parseGameLogMarkup(text: string): ParsedPart[] {
  const parts: ParsedPart[] = []
  const len = text.length
  let buf = ""

  const flush = () => {
    if (buf) {
      parts.push({ type: "text", value: buf })
      buf = ""
    }
  }

  let i = 0
  while (i < len) {
    if (text[i] === "@" && i + 1 < len) {
      const next = text[i + 1]

      // @P — purple text (typically player names, but also "Turn" headers)
      if (next === "P") {
        flush()
        i += 2 // skip @P
        let value = ""
        while (i < len && text[i] !== " " && text[i] !== "@") {
          value += text[i++]
        }
        // Include the trailing space
        if (i < len && text[i] === " ") {
          value += " "
          i++
        }
        parts.push({ type: "purple", value })
        continue
      }

      // @[ — card link
      if (next === "[") {
        flush()
        i += 2 // skip @[
        // Read card name until @ or end
        let cardName = ""
        while (i < len && text[i] !== "@") {
          cardName += text[i++]
        }
        // Skip delimiters (@:) and read first number (nameToken)
        let nameToken = ""
        let textureId = ""
        while (i < len && (text[i] === "@" || text[i] === ":" || text[i] === "~")) i++
        while (i < len && text[i] !== "," && text[i] !== ":" && text[i] !== "@") {
          nameToken += text[i++]
        }
        // Skip delimiters and read second number (textureId)
        while (i < len && (text[i] === "," || text[i] === ":" || text[i] === "@" || text[i] === "~")) i++
        while (i < len && text[i] !== "," && text[i] !== ":" && text[i] !== "@" && text[i] !== "]") {
          textureId += text[i++]
        }
        // Skip to closing ]
        while (i < len && text[i] !== "]") i++
        if (i < len) i++ // skip ]

        const parsedCardId = parseInt(nameToken, 10)
        const parsedTextureId = parseInt(textureId, 10)
        parts.push({
          type: "card",
          value: cardName,
          cardId: isNaN(parsedCardId) ? undefined : parsedCardId,
          textureId: isNaN(parsedTextureId) ? undefined : parsedTextureId,
        })
        continue
      }

      // @i — toggle italic. Content between @i ... @i is italic.
      if (next === "i") {
        flush()
        i += 2 // skip @i
        let value = ""
        // Read until closing @i
        while (i < len) {
          if (text[i] === "@" && i + 1 < len && text[i + 1] === "i") {
            i += 2 // skip closing @i
            break
          }
          value += text[i++]
        }
        if (value) parts.push({ type: "italic", value })
        continue
      }

      // Other @ markers — skip the marker character
      // (e.g. @R, @b, @g, @Y, @K, @H, /@)
      if (next === "/") {
        // /@ — end color marker
        i += 2
        continue
      }
      // Skip known formatting markers
      if ("RbgYKH".includes(next)) {
        i += 2
        continue
      }

      // Unknown @ sequence — just output it
      buf += text[i++]
      continue
    }

    // {…} — mana/symbol
    if (text[i] === "{") {
      const closeIdx = text.indexOf("}", i)
      if (closeIdx > i) {
        flush()
        const symbol = text.slice(i + 1, closeIdx)
        parts.push({ type: "mana", value: symbol })
        i = closeIdx + 1
        continue
      }
    }

    if (text[i] === "[") {
      const closeIdx = text.indexOf("]", i)
      if (closeIdx > i) {
        const symbol = text.slice(i + 1, closeIdx)
        if (getMtgoChatSymbolImagePath(symbol)) {
          flush()
          parts.push({ type: "chatSymbol", value: symbol })
          i = closeIdx + 1
          continue
        }
      }
    }

    buf += text[i++]
  }
  flush()
  return parts
}

/**
 * Renders parsed MTGO game log markup as React elements.
 */
export function GameLogText({
  text,
  className,
  manaSymbolClassName,
}: {
  text: string
  className?: string
  /** Optional class override for mana/tap glyph size in this render context. */
  manaSymbolClassName?: string
}) {
  const parts = parseGameLogMarkup(text)
  const symbolClassName = manaSymbolClassName ?? "inline h-3.5 w-3.5 align-text-bottom mx-px"
  return (
    <span className={className}>
      {parts.map((part, i) => {
        switch (part.type) {
          case "purple":
            return <span key={i} className="text-purple-400 font-medium">{part.value}</span>
          case "card":
            return <span key={i} className="text-sky-400 italic cursor-default">{part.value}</span>
          case "italic":
            return (
              <span key={i} className="gl-italic italic">
                {renderTextWithInlineMana(part.value, `italic-${i}`, symbolClassName)}
              </span>
            )
          case "mana": {
            const symbolPath = getManaSymbolSvgPath(part.value)
            if (symbolPath) {
              return (
                <img
                  key={i}
                  src={symbolPath}
                  alt={part.value}
                  className={symbolClassName}
                />
              )
            }
            // No SVG available — render as plain text
            return <React.Fragment key={i}>{`{${part.value}}`}</React.Fragment>
          }
          case "chatSymbol": {
            const symbolPath = getMtgoChatSymbolImagePath(part.value)
            if (symbolPath) {
              return (
                <img
                  key={i}
                  src={symbolPath}
                  alt={part.value}
                  title={part.value}
                  className={symbolClassName}
                />
              )
            }
            return <React.Fragment key={i}>{`[${part.value}]`}</React.Fragment>
          }
          default:
            return <React.Fragment key={i}>{part.value}</React.Fragment>
        }
      })}
    </span>
  )
}
