/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import React from "react"
import { motion, AnimatePresence } from "framer-motion"
import type { BoardState, CardState, BoardTransition } from "@/types/replay-types"
import { Tooltip, TooltipContent, TooltipTrigger, TooltipProvider } from "@/components/ui/tooltip"
import { GameLogText } from "@/utils/parse-game-log"
import {
  CARD_RATIO,
  CARD_TRANSITION,
  CardImg,
  CardTooltipContent,
  RevealedCardCell,
} from "./card-visuals"

const REVEAL_CARD_H = 140
const REVEAL_CARD_W = Math.floor(REVEAL_CARD_H * CARD_RATIO)
const REVEAL_OVERLAP = 0.88
const REVEAL_COL_GAP = 8
const REVEAL_VISIBLE_H = Math.floor(REVEAL_CARD_H * (1 - REVEAL_OVERLAP))

function RevealedColumnGrid({ cards }: { cards: CardState[] }) {
  const numCols = Math.min(5, Math.max(1, cards.length))
  const cardsPerCol = Math.ceil(cards.length / numCols)

  const columns: CardState[][] = Array.from({ length: numCols }, (_, i) =>
    cards.slice(i * cardsPerCol, (i + 1) * cardsPerCol)
  )

  const maxInCol = Math.max(...columns.map(c => c.length))
  const gridH = maxInCol <= 1 ? REVEAL_CARD_H : (maxInCol - 1) * REVEAL_VISIBLE_H + REVEAL_CARD_H
  const gridW = numCols * REVEAL_CARD_W + (numCols - 1) * REVEAL_COL_GAP

  return (
    <div className="relative" style={{ width: gridW, height: gridH }}>
      {columns.map((colCards, colIdx) =>
        colCards.map((card, rowIdx) => (
          <RevealedCardCell key={card.cardId} card={card} colIdx={colIdx} rowIdx={rowIdx} />
        ))
      )}
    </div>
  )
}

export function RevealedZoneOverlay({
  board,
  transition,
}: {
  board: BoardState
  transition: BoardTransition
}) {
  const revealedCards = board.zones.get("Revealed") ?? []
  const pile1 = board.zones.get("Pile1") ?? []
  const pile2 = board.zones.get("Pile2") ?? []
  const pile3 = board.zones.get("Pile3") ?? []

  const isPiles = pile1.length > 0 || pile2.length > 0 || pile3.length > 0
  const hasAny = revealedCards.length > 0 || isPiles

  const sections: { label: string; cards: CardState[] }[] = hasAny
    ? (isPiles
        ? ([
            pile1.length > 0 && { label: "Pile 1", cards: pile1 },
            pile2.length > 0 && { label: "Pile 2", cards: pile2 },
            pile3.length > 0 && { label: "Pile 3", cards: pile3 },
          ].filter(Boolean) as { label: string; cards: CardState[] }[])
        : [{ label: "Revealed", cards: revealedCards }])
    : []

  return (
    <AnimatePresence>
      {hasAny && (
        <motion.div
          key="revealed-overlay"
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          transition={{ duration: 0.15 }}
          className="absolute inset-x-0 top-0 z-20 flex justify-center pt-2 pointer-events-none"
        >
          <div className="bg-sidebar border border-sidebar-border/60 rounded-lg px-4 py-3 shadow-2xl pointer-events-auto">
            {sections.map(({ label, cards }) => (
              <div key={label} className="mb-4 last:mb-0">
                <div className="flex items-center gap-2 mb-2">
                  <span className="text-[10px] font-semibold text-muted-foreground uppercase tracking-wider">{label}</span>
                  <span className="text-[10px] text-muted-foreground/50">({cards.length})</span>
                </div>
                <RevealedColumnGrid cards={cards} />
              </div>
            ))}
          </div>
        </motion.div>
      )}
    </AnimatePresence>
  )
}

// -- Stack overlay --

const STACK_CARD_H = 120
const STACK_CARD_W = Math.floor(STACK_CARD_H * CARD_RATIO)
const STACK_MIN_OVERLAP = 20

export type StackHoverRelation = {
  sourceCardId: number
  targetIds: number[]
}

export function StackOverlay({
  cards,
  transition,
  onHoverCard,
  activeSourceCardId,
}: {
  cards: CardState[]
  transition: BoardTransition
  /** Called with stack source + targets when hovering a stack card, null on leave. */
  onHoverCard?: (relation: StackHoverRelation | null) => void
  /** Active source card ID for source highlight treatment. */
  activeSourceCardId?: number | null
}) {
  const hasStack = cards.length > 0

  // FILO: leftmost = bottom of stack (entered first, resolves last)
  //       rightmost = top of stack (entered last, resolves next)
  const ordered = [...cards] // already oldest→newest from engine

  const count = ordered.length
  const maxW = 480
  const naturalW = count * STACK_CARD_W + (count - 1) * 4
  const offset = naturalW > maxW
    ? Math.max(STACK_MIN_OVERLAP, (maxW - STACK_CARD_W) / (count - 1))
    : STACK_CARD_W
  const containerW = count <= 1 ? STACK_CARD_W : offset * (count - 1) + STACK_CARD_W

  return (
    <AnimatePresence>
    {hasStack && (
    <motion.div
      key="stack-overlay"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.15 }}
      className="absolute inset-y-0 right-0 z-20 flex items-center pr-4"
    >
      <div className="bg-sidebar border border-sidebar-border/60 rounded-lg px-4 py-3 shadow-2xl">
        {/* Header */}
        <div className="flex items-center gap-2 mb-2.5">
          <span className="text-[10px] font-semibold text-muted-foreground uppercase tracking-wider">Stack</span>
          <span className="text-[10px] text-muted-foreground/50">({count})</span>
          <span className="ml-auto text-[9px] text-muted-foreground/40 italic">← resolves last · resolves next →</span>
        </div>

        {/* Cards laid out left-to-right, oldest on left */}
        <div className="relative" style={{ height: STACK_CARD_H, width: containerW }}>
          <AnimatePresence>
          {ordered.map((card, i) => {
            const isTop = i === count - 1 // rightmost = resolves next
            const targets = card.associations?.ActionTarget
            const hasTargets = !!targets && targets.length > 0
            const sourceHighlighted = hasTargets && activeSourceCardId === card.cardId
            return (
              <motion.div
                key={card.cardId}
                layoutId={(card.isTriggeredAbility || card.isActivatedAbility) ? undefined : `card-${card.lineageId}`}
                layout
                transition={CARD_TRANSITION}
                className={[
                  "absolute top-0 rounded-md overflow-hidden border",
                  isTop
                    ? "border-sidebar-accent ring-1 ring-sidebar-accent/60"
                    : "border-sidebar-border/60",
                  sourceHighlighted
                    ? "border-blue-400/80 ring-2 ring-blue-500 shadow-[0_0_14px_rgba(59,130,246,0.6)]"
                    : "",
                ].filter(Boolean).join(" ")}
                style={{
                  left: i * offset,
                  width: STACK_CARD_W,
                  height: STACK_CARD_H,
                  zIndex: i + 1,
                }}
                onMouseEnter={() => {
                  if (targets && targets.length > 0) {
                    onHoverCard?.({ sourceCardId: card.cardId, targetIds: targets })
                  }
                }}
                onMouseLeave={() => onHoverCard?.(null)}
              >
                <TooltipProvider delayDuration={200}>
                  <Tooltip>
                    <TooltipTrigger asChild>
                      <div className="w-full h-full">
                        {(card.isTriggeredAbility || card.isActivatedAbility) ? (
                          <div className="w-full h-full relative">
                            {/* M15 card frame (art box is transparent) */}
                            <img src="/m15-frame.png" alt="" className="absolute inset-0 w-full h-full z-10 pointer-events-none" style={{ filter: 'brightness(0.3)' }} />
                            {/* Full card image behind frame — art shows through transparent art box */}
                            <CardImg catalogId={card.catalogId} alt={card.name} className="absolute inset-0 w-full h-full object-cover z-0" />
                            {/* Card name */}
                            <div className="absolute z-20 flex items-center px-1.5 truncate" style={{ top: '3%', left: '5%', right: '5%', height: '12%' }}>
                              <span className="text-[7px] font-bold text-foreground leading-none truncate">{card.name}</span>
                            </div>
                            {/* Type line */}
                            <div className="absolute z-20 flex items-center px-1.5" style={{ top: '55%', left: '5%', right: '5%', height: '10%' }}>
                              <span className="text-[6px] font-semibold text-foreground/80 leading-none">
                                {card.isTriggeredAbility ? "Triggered Ability" : "Activated Ability"}
                              </span>
                            </div>
                            {/* Rules text */}
                            <div className="absolute z-20 overflow-hidden px-1.5 py-0.5" style={{ top: '64.5%', left: '5%', right: '5%', bottom: '5%' }}>
                              <span className="text-[6px] text-foreground/70 leading-tight line-clamp-4">
                                <GameLogText text={card.rulesText} manaSymbolClassName="inline h-[6px] w-[6px] align-text-bottom mx-[0.5px]" />
                              </span>
                            </div>
                          </div>
                        ) : (
                          <CardImg
                            catalogId={card.catalogId}
                            alt={card.name}
                            fallback={
                              <div className="w-full h-full bg-muted/80 flex flex-col items-center justify-center p-1">
                                <span className="text-[8px] font-semibold text-muted-foreground text-center leading-tight">
                                  {card.name}
                                </span>
                                {card.rulesText && (
                                  <span className="text-[7px] text-muted-foreground/70 text-center leading-tight mt-0.5 line-clamp-3">
                                    <GameLogText text={card.rulesText} manaSymbolClassName="inline h-[7px] w-[7px] align-text-bottom mx-[0.5px]" />
                                  </span>
                                )}
                              </div>
                            }
                          />
                        )}
                      </div>
                    </TooltipTrigger>
                    <TooltipContent side="top" className="max-w-[200px]">
                      <CardTooltipContent card={card} footer={
                        <div className="text-muted-foreground/60 text-[10px]">
                          {isTop ? "Resolves next" : `Resolves in ${count - i} steps`}
                        </div>
                      } />
                    </TooltipContent>
                  </Tooltip>
                </TooltipProvider>
              </motion.div>
            )
          })}
          </AnimatePresence>
        </div>
      </div>
    </motion.div>
    )}
    </AnimatePresence>
  )
}
