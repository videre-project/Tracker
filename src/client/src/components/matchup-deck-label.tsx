/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { Layers } from "lucide-react"
import { CardImage, DeckManaSymbols, useCardMedia } from "@videreproject/ui"
import type { DeckReferenceData } from "@/types/api"

export type DeckIdentity = Pick<DeckReferenceData, "name" | "archetype" | "colors" | "featuredCards" | "format">

export function MatchupDeckLabel({
  deck,
  showFormat = false,
  showArchetype = false,
  heading = false,
  compact = false,
  align = "start",
  wrapName = false,
  selected = false
}: {
  deck: DeckIdentity
  showFormat?: boolean
  showArchetype?: boolean
  heading?: boolean
  compact?: boolean
  align?: "start" | "end"
  wrapName?: boolean
  selected?: boolean
}) {
  const { getCardArtUrl } = useCardMedia()
  const card = deck.featuredCards?.[0]
  return (
    <div className={`flex min-w-0 items-center gap-2.5 ${align === "end" ? "flex-row-reverse" : ""}`}>
      <div
        className={`grid shrink-0 place-items-center overflow-hidden rounded bg-muted ${heading && !compact ? "h-12 w-12" : "h-8 w-8"} ${selected ? "outline outline-2 outline-offset-1 outline-primary/75" : ""}`}
        aria-hidden="true"
      >
        <CardImage
          catalogId={card?.catalogId ?? null}
          name={card?.name ?? undefined}
          imageUrl={card?.catalogId ? getCardArtUrl?.(card.catalogId) : undefined}
          alt=""
          className="h-full w-full object-cover object-[center_25%]"
          fallback={<Layers className="h-4 w-4 text-muted-foreground" />}
        />
      </div>
      <div className={`min-w-0 ${align === "end" ? "text-right" : "text-left"}`}>
        <div
          className={`${wrapName ? "break-words leading-4" : "truncate"} font-medium ${heading ? "text-base" : "text-xs"}`}
          title={deck.name ?? "Unclassified"}
        >
          {deck.name || "Unclassified"}
        </div>
        <div className={`mt-1 flex h-3.5 items-center gap-1 text-xs text-muted-foreground ${align === "end" ? "justify-end" : ""}`}>
          {showArchetype && <span className="truncate" title={deck.archetype || "Unclassified deck"}>
            {deck.archetype || "Unclassified deck"}
          </span>}
          <DeckManaSymbols colors={deck.colors} className="h-3.5 gap-1" symbolClassName="h-3 w-3" />
          {showFormat && (
            <span className="ml-1 truncate text-[11px] text-muted-foreground">{deck.format}</span>
          )}
        </div>
      </div>
    </div>
  )
}

export function MatchupPairing({ deck, opponent, heading = false, compact = false, showFormat = false }: {
  deck: DeckIdentity
  opponent: DeckIdentity
  heading?: boolean
  compact?: boolean
  showFormat?: boolean
}) {
  return (
    <div role="group" aria-label="Matchup" className="grid min-w-0 grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)] items-center gap-3">
      <div role="group" aria-label="Your deck" className="min-w-0">
        <MatchupDeckLabel deck={deck} heading={heading} compact={compact} showFormat={showFormat} wrapName />
      </div>
      <span className="text-xs text-muted-foreground">vs</span>
      <div role="group" aria-label="Opponent deck" className="min-w-0">
        <MatchupDeckLabel deck={opponent} heading={heading} compact={compact} align="end" wrapName />
      </div>
    </div>
  )
}
