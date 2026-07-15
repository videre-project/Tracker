/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import React from "react"

import { CardImage } from "@/components/card-image"
import { cn } from "@/lib/utils"
import type { OpeningHandCard, SideboardingCard, SideboardingDiff } from "./match-review-model"

const CARD_STACK_BADGE_TOP = "12%"

function ReviewStripHeader({
  label,
  badge,
  borderTop,
  endContent,
}: {
  label: string
  badge: React.ReactNode
  borderTop?: boolean
  endContent?: React.ReactNode
}) {
  return (
    <div className={cn(
      "flex h-9 shrink-0 items-center gap-2 border-b border-sidebar-border/60 bg-muted/10 px-3",
      borderTop && "border-t"
    )}>
      <div className="flex min-w-0 flex-1 items-center gap-2">
        <span className="text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">
          {label}
        </span>
        <span className="rounded-sm bg-muted/45 px-1.5 py-0.5 text-[10px] font-semibold leading-none text-muted-foreground">
          {badge}
        </span>
      </div>
      {endContent ? <div className="ml-auto flex shrink-0 items-center gap-2">{endContent}</div> : null}
    </div>
  )
}

function OpeningHandCardTile({ card }: { card: OpeningHandCard }) {
  return (
    <div
      className={cn(
        "relative h-full shrink-0 overflow-hidden rounded-sm border border-sidebar-border/60 bg-muted/30 shadow-sm",
        card.bottomed && "opacity-35 grayscale",
      )}
      style={{ aspectRatio: "5 / 7" }}
      title={card.bottomed ? `${card.name} (put on bottom)` : card.name}
    >
      <div className="absolute inset-0 flex items-center justify-center px-1.5 text-center text-[10px] font-semibold leading-tight text-muted-foreground">
        {card.name}
      </div>
      {card.catalogId != null ? (
        <CardImage
          catalogId={card.catalogId}
          alt={card.name}
          className="absolute inset-0 h-full w-full object-cover"
        />
      ) : null}
    </div>
  )
}

function OpeningHandPreview({ cards }: { cards: OpeningHandCard[] }) {
  return (
    <div className="min-h-0 border-b border-sidebar-border/60 bg-muted/10 px-3 py-2">
      {cards.length > 0 ? (
        <div className="flex h-full min-w-0 gap-2 overflow-x-auto overflow-y-hidden">
          {cards.map((card, index) => (
            <OpeningHandCardTile key={`${card.key}-${index}`} card={card} />
          ))}
        </div>
      ) : (
        <div className="flex h-full items-center justify-center rounded-md border border-dashed border-sidebar-border/60 bg-background/25 px-3 text-center text-xs text-muted-foreground">
          Opening hand could not be reconstructed from this game log.
        </div>
      )}
    </div>
  )
}

function SideboardingCardTile({
  card,
  direction,
}: {
  card: SideboardingCard
  direction: "in" | "out"
}) {
  return (
    <div
      className="h-full shrink-0"
      style={{ aspectRatio: "5 / 7" }}
      title={`${direction === "in" ? "Added" : "Removed"} ${card.quantity} ${card.name}`}
    >
      <div className="relative h-full w-full overflow-hidden rounded-sm bg-background/35 shadow-sm shadow-black/30">
        <div className="absolute inset-0 flex items-center justify-center px-1.5 text-center text-[10px] font-semibold leading-tight text-muted-foreground">
          {card.name}
        </div>
        {card.catalogId != null ? (
          <CardImage
            catalogId={card.catalogId}
            alt=""
            aria-hidden="true"
            className="absolute inset-0 h-full w-full object-cover"
          />
        ) : null}
        <div
          className="absolute left-2 z-10 rounded-sm bg-black/75 px-1.5 py-0.5 text-[10px] font-bold leading-none text-white ring-1 ring-white/10"
          style={{ top: CARD_STACK_BADGE_TOP }}
        >
          {card.quantity}
        </div>
      </div>
    </div>
  )
}

function SideboardingGroup({
  title,
  direction,
  cards,
  className,
}: {
  title: string
  direction: "in" | "out"
  cards: SideboardingCard[]
  className?: string
}) {
  const titleClass = direction === "in" ? "text-emerald-300" : "text-rose-300"
  const bandClass = direction === "in"
    ? "bg-emerald-500/[0.10]"
    : "bg-rose-500/[0.10]"
  const sign = direction === "in" ? "+" : "-"

  return (
    <div className={cn("grid min-h-0 min-w-0 grid-cols-[1.75rem_minmax(0,1fr)] gap-2 px-2 py-2", bandClass, className)} title={title}>
      <div className={cn("flex items-center justify-center self-stretch font-mono text-lg font-bold", titleClass)}>
        {sign}
      </div>
      <div className="flex h-full min-w-0 gap-2 overflow-x-auto overflow-y-hidden">
        {cards.map(card => (
          <SideboardingCardTile key={card.key} card={card} direction={direction} />
        ))}
      </div>
    </div>
  )
}

function SideboardingEmptyState({ message }: { message: string }) {
  return (
    <div className="flex min-h-0 items-center justify-center border-b border-sidebar-border/60 px-3 py-3">
      <div className="flex h-full w-full items-center justify-center rounded-md border border-dashed border-sidebar-border/60 bg-background/25 px-3 text-center text-xs text-muted-foreground">
        {message}
      </div>
    </div>
  )
}

export function GameReviewPanel({
  openingHandCards,
  sideboardingDiff,
  endContent,
}: {
  openingHandCards: OpeningHandCard[]
  sideboardingDiff: SideboardingDiff
  endContent?: React.ReactNode
}) {
  const addedCount = sideboardingDiff.in.reduce((total, card) => total + card.quantity, 0)
  const removedCount = sideboardingDiff.out.reduce((total, card) => total + card.quantity, 0)
  const keptCount = openingHandCards.filter(card => !card.bottomed).length
  const hasSideboarding = addedCount > 0 || removedCount > 0

  if (!hasSideboarding) {
    return (
      <div className="grid h-full min-h-0 grid-rows-[auto_minmax(7rem,1fr)_auto_minmax(14rem,2fr)] overflow-y-auto overflow-x-hidden bg-muted/10">
        <ReviewStripHeader label="Opening hand" badge={keptCount} endContent={endContent} />
        <OpeningHandPreview cards={openingHandCards} />
        <ReviewStripHeader label="Sideboarding" badge="None" />
        <SideboardingEmptyState message={sideboardingDiff.emptyMessage} />
      </div>
    )
  }

  return (
    <div className="grid h-full min-h-0 grid-rows-[auto_minmax(7rem,1fr)_auto_minmax(7rem,1fr)_minmax(7rem,1fr)] overflow-y-auto overflow-x-hidden bg-muted/10">
      <ReviewStripHeader label="Opening hand" badge={keptCount} endContent={endContent} />
      <OpeningHandPreview cards={openingHandCards} />
      <ReviewStripHeader label="Sideboarding" badge={`+${addedCount} / -${removedCount}`} />
      <SideboardingGroup
        title="Added"
        direction="in"
        cards={sideboardingDiff.in}
        className="border-b border-sidebar-border/60"
      />
      <SideboardingGroup title="Removed" direction="out" cards={sideboardingDiff.out} />
    </div>
  )
}
