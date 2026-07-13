/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import React, { useEffect } from "react"
import { SquarePen } from "lucide-react"

import { useCardArtContext } from "@/components/card-art"
import type { CardEntry, DeckDetail } from "@/hooks/use-decks"
import { cn } from "@/lib/utils"
import { getManaSymbolSvgPath } from "@/utils/mana-symbols"
import { getDisplayCardColors } from "@/utils/card-colors"
export function getDeckPreviewCards(detail?: DeckDetail | null) {
  if (!detail) return []

  return [...detail.mainboard]
    .sort((a, b) => b.quantity - a.quantity)
    .slice(0, 5)
}

function getCardImageUrl(card: CardEntry) {
  return `https://r2.videreproject.com/cards/${card.catalogId}-300px.png`
}

export function DeckManaSymbols({ colors }: { colors?: string[] | null }) {
  if (!colors) return null

  const visibleColors = getDisplayCardColors(colors)

  return (
    <span className="inline-flex h-4 items-center gap-0.5 leading-none">
      {visibleColors.map((color, index) => (
        <img
          key={`${color}-${index}`}
          src={getManaSymbolSvgPath(color) ?? undefined}
          alt={color}
          className="block h-3.5 w-3.5 rounded-full bg-background shadow-sm ring-1 ring-background"
        />
      ))}
    </span>
  )
}

export function MatchDeckCard({
  deckName,
  deckHash,
  deckArchetype,
  deckColors,
  previewCards,
  loading,
  onOpen,
}: {
  deckName?: string | null
  deckHash?: string | null
  deckArchetype?: string | null
  deckColors?: string[] | null
  previewCards: CardEntry[]
  loading?: boolean
  onOpen: () => void
}) {
  const canOpen = Boolean(deckHash)
  const backgroundCard = previewCards[Math.floor(previewCards.length / 2)]
  const { getArtUrl, prefetchCards, isReady: cardArtReady } = useCardArtContext()
  const backgroundArtUrl = backgroundCard ? getArtUrl(backgroundCard.name) : null

  useEffect(() => {
    if (!cardArtReady || !backgroundCard?.name || backgroundArtUrl) return
    void prefetchCards([backgroundCard.name])
  }, [backgroundArtUrl, backgroundCard?.name, cardArtReady, prefetchCards])

  return (
    <button
      type="button"
      disabled={!canOpen}
      onClick={onOpen}
      className={cn(
        "group/editor relative block w-full overflow-visible rounded-lg border border-sidebar-border/60 bg-card text-left transition-colors",
        canOpen
          ? "hover:z-20 hover:border-primary/35 hover:bg-muted/15 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring"
          : "cursor-default opacity-80"
      )}
    >
      <div className="relative z-0 h-36 overflow-visible rounded-t-lg border-b border-sidebar-border/60">
        <div className="absolute inset-0 overflow-hidden rounded-t-lg bg-muted/25 transition-colors duration-300 group-hover/editor:bg-muted/40">
          {backgroundArtUrl ? (
            <img
              src={backgroundArtUrl}
              alt=""
              aria-hidden="true"
              className="absolute inset-0 h-full w-full scale-110 object-cover object-top opacity-55 blur-sm saturate-125 transition-opacity duration-300 group-hover/editor:opacity-65"
            />
          ) : null}
          <div className="absolute inset-0 bg-background/45" />
          <div className="absolute inset-0 bg-gradient-to-b from-background/25 via-background/5 to-background/45" />
        </div>

        <div className="absolute inset-x-0 bottom-0 top-0 z-10 [clip-path:inset(-4rem_-5rem_1px_-5rem)]">
          {previewCards.length > 0 ? (
            previewCards.map((card, index) => {
              const offset = index - (previewCards.length - 1) / 2
              const distance = Math.abs(offset)
              const restingTransform = `translateX(calc(-50% + ${offset * 20}px)) translateY(${-distance * 3}px) rotate(${offset * 8}deg)`
              const activeTransform = `translateX(calc(-50% + ${offset * 34}px)) translateY(${-16 - distance * 4}px) rotate(${offset * 12}deg)`

              return (
                <img
                  key={`${card.catalogId}-${card.name}`}
                  src={getCardImageUrl(card)}
                  alt={card.name}
                  title={card.name}
                  className="absolute bottom-[-10px] left-1/2 h-32 w-[5.7rem] origin-[50%_92%] rounded-sm object-cover shadow-lg ring-1 ring-border/70 transition-[filter,transform] duration-300 ease-out [transform:var(--deck-card-transform)] group-hover/editor:brightness-110 group-hover/editor:[transform:var(--deck-card-active-transform)] group-focus-visible/editor:brightness-110 group-focus-visible/editor:[transform:var(--deck-card-active-transform)]"
                  style={{
                    "--deck-card-transform": restingTransform,
                    "--deck-card-active-transform": activeTransform,
                    zIndex: 10 - distance,
                  } as React.CSSProperties}
                />
              )
            })
          ) : (
            <div className="absolute inset-x-6 bottom-3 grid grid-cols-5 gap-2">
              {Array.from({ length: 5 }).map((_, index) => (
                <div
                  key={index}
                  className={cn(
                    "aspect-[5/7] rounded-sm border border-sidebar-border/60 bg-muted/45",
                    loading && "animate-pulse"
                  )}
                />
              ))}
            </div>
          )}
        </div>

        <div className="absolute inset-x-0 bottom-0 z-20 h-11 bg-gradient-to-t from-card/90 from-[0%] via-card/45 via-[38%] to-transparent" />
        {deckColors ? (
          <div className="absolute bottom-2 left-3 z-30 px-2 py-1">
            <DeckManaSymbols colors={deckColors} />
          </div>
        ) : null}
      </div>

      {canOpen ? (
        <span
          className="absolute right-3 top-3 z-30 inline-flex h-8 w-8 items-center justify-center rounded-md border border-sidebar-border/70 bg-background/85 text-foreground opacity-0 shadow-sm backdrop-blur-sm transition-opacity group-hover/editor:opacity-100 group-focus-visible/editor:opacity-100"
          aria-hidden="true"
        >
          <SquarePen className="h-4 w-4" />
        </span>
      ) : null}

      <div className="relative z-20 rounded-b-lg bg-card p-3">
        <div className="flex min-w-0 items-start justify-between gap-3">
          <div className="min-w-0 pt-1">
            <h2 className="truncate text-[15px] font-semibold leading-5 text-foreground">{deckName || "Unknown deck"}</h2>
            <p className="mt-0.5 truncate text-xs leading-4 text-muted-foreground">
              {deckArchetype || (loading ? "Loading deck..." : "Unclassified deck")}
            </p>
          </div>
        </div>
      </div>
    </button>
  )
}
