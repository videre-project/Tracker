/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { VIDERE_OPENAPI_ENUMS } from "@/types/videre.g"

const VIDERE_CARD_FORMATS = VIDERE_OPENAPI_ENUMS.paths["/cards"].get.parameters.format
type VidereCardFormat = (typeof VIDERE_CARD_FORMATS)[number]

const FORMAT_PRESENTATION = {
  standard:  { label: "Standard",  dot: "bg-purple-500", background: "bg-purple-700" },
  modern:    { label: "Modern",    dot: "bg-red-500",    background: "bg-red-700"    },
  pioneer:   { label: "Pioneer",   dot: "bg-pink-500",   background: "bg-pink-700"   },
  vintage:   { label: "Vintage",   dot: "bg-amber-500",  background: "bg-amber-700"  },
  legacy:    { label: "Legacy",    dot: "bg-blue-500",   background: "bg-blue-700"   },
  pauper:    { label: "Pauper",    dot: "bg-teal-500",   background: "bg-teal-700"   },
  premodern: { label: "Premodern", dot: "bg-red-400",    background: "bg-red-900"    },
  extended:  { label: "Extended",  dot: "bg-orange-500", background: "bg-orange-700" },
  classic:   { label: "Classic",   dot: "bg-orange-500", background: "bg-orange-700" },
} satisfies Record<VidereCardFormat, {
  label: string
  dot: string
  background: string
}>

const FORMAT_DEFINITIONS = VIDERE_CARD_FORMATS.map(id => ({
  id,
  ...FORMAT_PRESENTATION[id],
}))

export type CardSearchFormat = VidereCardFormat

export const CARD_FORMATS = [...VIDERE_CARD_FORMATS]

const FORMAT_MATCH_ORDER = [...FORMAT_DEFINITIONS]
  .sort((a, b) => b.id.length - a.id.length)

function findFormat(format: string) {
  const normalizedFormat = format.trim().toLowerCase()
  return FORMAT_DEFINITIONS.find(candidate => candidate.id === normalizedFormat)
    ?? FORMAT_MATCH_ORDER.find(candidate => normalizedFormat.includes(candidate.id))
}

export function compareFormats(a: string, b: string): number {
  const aDefinition = findFormat(a)
  const bDefinition = findFormat(b)
  const aIndex = aDefinition ? FORMAT_DEFINITIONS.indexOf(aDefinition) : -1
  const bIndex = bDefinition ? FORMAT_DEFINITIONS.indexOf(bDefinition) : -1

  if (aIndex >= 0 && bIndex >= 0) return aIndex - bIndex
  if (aIndex >= 0) return -1
  if (bIndex >= 0) return 1
  return a.localeCompare(b)
}

export function isLimitedFormat(format: string): boolean {
  return /draft|sealed|limited/i.test(format)
}

export function getFormatDotColor(format: string): string {
  return findFormat(format)?.dot ?? "bg-orange-500"
}

export function getFormatBackgroundColor(format: string): string {
  return findFormat(format)?.background ?? "bg-orange-700"
}
