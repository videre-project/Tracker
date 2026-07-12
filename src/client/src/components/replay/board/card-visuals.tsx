/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import React, { useRef, useState, useEffect, useCallback } from "react"
import { motion } from "framer-motion"
import type { BoardState, CardState, BoardTransition } from "@/types/replay-types"
import { Tooltip, TooltipContent, TooltipTrigger, TooltipProvider } from "@/components/ui/tooltip"
import { getApiUrl } from "@/utils/api-config"
import { GameLogText } from "@/utils/parse-game-log"
import { useCardImage } from "@/hooks/use-card-image"
// -- Layout constants --

/**
 * Card aspect ratio and cell geometry (all relative to cell side = 1).
 *
 * Cards are 5:7 (width:height). Each grid cell is a square whose side equals
 * the card height, so a tapped (90°-rotated) card fits within the same cell.
 * All positioning uses percentages so cells scale with available space.
 */
export const CARD_RATIO = 5 / 7                      // card width / card height
export const CARD_W_PCT = CARD_RATIO * 100           // card width as % of cell  (~71.4%)
export const CARD_H_PCT = 100                        // card height as % of cell (100%)
export const CARD_LEFT_PCT = (100 - CARD_W_PCT) / 2  // center card horizontally (~14.3%)

// Tapped card: after 90° rotation the visual box is 100% × 71.4%.
// Shift the layout box down so visual bottom = cell bottom.
export const TAP_TOP_PCT = (100 - CARD_W_PCT) / 2    // = CARD_LEFT_PCT (~14.3%)


export const CARD_TRANSITION = { type: "spring" as const, stiffness: 320, damping: 32 }
export const STACK_MAX = 10
export const DEBUG_EXILED_HITBOX = false

/** How much of the top zone bar peeks out above the battlefield (labels + card tops). */
export const TOP_BAR_PEEK = 50
export const TOP_BAR_FULL = 180

// -- Cached card image component --

/**
 * Renders a card image from the persistent cache (Cache API + in-memory).
 * Resolves CDN → MTGO fallback automatically; subsequent renders are instant.
 */
export function CardImg({
  catalogId,
  alt,
  className = "w-full h-full object-cover",
  style,
  fallback,
}: {
  catalogId: number | null
  alt: string
  className?: string
  style?: React.CSSProperties
  fallback?: React.ReactNode
}) {
  const src = useCardImage(catalogId)
  const [source, setSource] = useState<"cdn" | "fallback" | "failed">("cdn")

  useEffect(() => {
    setSource("cdn")
  }, [catalogId])

  const fallbackSrc = catalogId != null && catalogId > 0
    ? getApiUrl(`/api/collection/cards/${catalogId}/image`)
    : null
  const imageSrc = source === "fallback" ? fallbackSrc : source === "cdn" ? src : null

  if (imageSrc) {
    return (
      <img
        src={imageSrc}
        alt={alt}
        className={className}
        style={style}
        onError={() => {
          setSource(current => current === "cdn" && fallbackSrc ? "fallback" : "failed")
        }}
      />
    )
  }

  return <>{fallback}</>
}


// -- Reveal→destination flying card animation --

/**
 * Stores the last known screen rects of revealed cards so we can
 * animate a clone flying from the overlay to wherever the card lands.
 */
const revealedRectsRef = { current: new Map<number, DOMRect>() }

const REVEAL_CARD_H = 140
const REVEAL_CARD_W = Math.floor(REVEAL_CARD_H * CARD_RATIO)
const REVEAL_COL_GAP = 8
const REVEAL_VISIBLE_H = Math.floor(REVEAL_CARD_H * 0.12)

export function RevealedCardCell({ card, colIdx, rowIdx }: { card: CardState; colIdx: number; rowIdx: number }) {
  const ref = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (ref.current) {
      revealedRectsRef.current.set(card.cardId, ref.current.getBoundingClientRect())
    }
  })
  return (
    <motion.div
      ref={ref}
      key={card.cardId}
      initial={{ opacity: 0, scale: 0.9 }}
      animate={{ opacity: 1, scale: 1 }}
      exit={{ opacity: 0, scale: 0.9 }}
      transition={CARD_TRANSITION}
      className="absolute rounded-md overflow-hidden border border-sidebar-border/60"
      style={{
        left: colIdx * (REVEAL_CARD_W + REVEAL_COL_GAP),
        top: rowIdx * REVEAL_VISIBLE_H,
        width: REVEAL_CARD_W,
        height: REVEAL_CARD_H,
        zIndex: rowIdx,
      }}
    >
      <TooltipProvider delayDuration={200}>
        <Tooltip>
          <TooltipTrigger asChild>
            <div className="w-full h-full bg-muted">
              <CardImg
                catalogId={card.catalogId}
                alt={card.name}
                fallback={
                  <div className="w-full h-full flex items-center justify-center p-1">
                    <span className="text-[9px] text-center text-muted-foreground leading-tight">{card.name}</span>
                  </div>
                }
              />
            </div>
          </TooltipTrigger>
          <TooltipContent side="bottom" className="max-w-[200px]">
            <CardTooltipContent card={card} />
          </TooltipContent>
        </Tooltip>
      </TooltipProvider>
    </motion.div>
  )
}

/**
 * A temporary floating clone that animates from the revealed card's position
 * to the destination card's position. Completely independent of Framer's
 * layoutId system — uses fixed positioning and Framer animate props.
 */
function FlyingCard({
  catalogId,
  name,
  from,
  to,
  onComplete,
}: {
  catalogId: number | null
  name: string
  from: DOMRect
  to: DOMRect
  onComplete: () => void
}) {
  return (
    <motion.div
      initial={{
        position: "fixed",
        left: from.left,
        top: from.top,
        width: from.width,
        height: from.height,
        zIndex: 9999,
      }}
      animate={{
        left: to.left,
        top: to.top,
        width: to.width,
        height: to.height,
      }}
      transition={{ ...CARD_TRANSITION, duration: 0.4 }}
      onAnimationComplete={onComplete}
      className="rounded-md overflow-hidden border border-sidebar-border/60 pointer-events-none"
      style={{ position: "fixed", zIndex: 9999 }}
    >
      <CardImg
        catalogId={catalogId}
        alt={name}
        fallback={
          <div className="w-full h-full bg-muted flex items-center justify-center p-1">
            <span className="text-[9px] text-center text-muted-foreground leading-tight">{name}</span>
          </div>
        }
      />
    </motion.div>
  )
}

/**
 * Manages flying card animations from Revealed zone to any destination.
 * Renders temporary clones that fly from the source position to the
 * destination, then self-destruct.
 */
export function FlyingCardLayer({
  board,
  transition,
}: {
  board: BoardState
  transition: BoardTransition
}) {
  const [flights, setFlights] = useState<{
    id: number
    catalogId: number | null
    name: string
    from: DOMRect
    to: DOMRect
  }[]>([])

  const pendingFlights = useRef<Map<number, { catalogId: number | null; name: string; from: DOMRect }>>(new Map())

  // Step 1: when transition says a card moved from Revealed, look up source rect
  useEffect(() => {
    const newPending = new Map<number, { catalogId: number | null; name: string; from: DOMRect }>()
    for (const [cardId, move] of transition.movedCards) {
      if (move.fromZone === "Revealed") {
        const card = board.cards.get(cardId)
        if (!card) continue
        const sourceRect = revealedRectsRef.current.get(card.lineageId)
        if (sourceRect) {
          newPending.set(cardId, { catalogId: card.catalogId, name: card.name, from: sourceRect })
        }
      }
    }
    pendingFlights.current = newPending
  }, [transition, board])

  // Step 2: after the DOM updates with the new card positions, measure
  // destination rects and launch flights
  useEffect(() => {
    if (pendingFlights.current.size === 0) return
    // Wait one frame for the destination cards to mount and layout
    const raf = requestAnimationFrame(() => {
      const newFlights: typeof flights = []
      for (const [cardId, flight] of pendingFlights.current) {
        // Find the destination element by layoutId data attribute or by
        // searching for the card's DOM element
        const destEl = document.querySelector(`[data-card-id="${cardId}"]`)
        if (destEl) {
          const to = destEl.getBoundingClientRect()
          newFlights.push({ id: cardId, ...flight, to })
        }
      }
      pendingFlights.current.clear()
      if (newFlights.length > 0) setFlights(prev => [...prev, ...newFlights])
    })
    return () => cancelAnimationFrame(raf)
  }, [transition])

  const handleComplete = useCallback((id: number) => {
    setFlights(prev => prev.filter(f => f.id !== id))
  }, [])

  return (
    <>
      {flights.map(f => (
        <FlyingCard
          key={f.id}
          catalogId={f.catalogId}
          name={f.name}
          from={f.from}
          to={f.to}
          onComplete={() => handleComplete(f.id)}
        />
      ))}
    </>
  )
}

// -- Zone helpers --

export const CONTROLLER_ZONES = new Set(["Battlefield", "Stack"])

export function getPlayerZoneCards(
  board: BoardState,
  zoneName: string,
  playerIndex: number,
): CardState[] {
  const zoneCards = board.zones.get(zoneName)
  if (!zoneCards) return []
  const useController = CONTROLLER_ZONES.has(zoneName)
  return zoneCards.filter(c =>
    useController ? c.controllerId === playerIndex : c.ownerId === playerIndex
  )
}

/** A card is a creature if it has power or toughness set (even "0"). */
export function isCreature(c: CardState): boolean {
  return (c.power != null && c.power !== "")
      || (c.toughness != null && c.toughness !== "")
}

// -- Card tooltip --

export function CardTooltipContent({ card, footer }: { card: CardState; footer?: React.ReactNode }) {
  const isAbility = card.isTriggeredAbility || card.isActivatedAbility
  const nonModifierCounters = Object.entries(card.counters).filter(([n]) => !isModifierCounter(n))
  const grantedAbilities = card.abilities.filter(a => !(card.initialAbilities ?? []).includes(a))
  const normalizeRulesText = (text: string) => text
    .replace(/\\r\\n/g, "\n")
    .replace(/\\n/g, "\n")
    .replace(/\r\n/g, "\n")

  return (
    <div className="text-xs space-y-0.5">
      <div className="font-semibold flex items-center gap-1">
        <span>{card.name}</span>
        {card.manaCost && !isAbility && (
          <span className="text-yellow-300 font-normal">
            <GameLogText text={card.manaCost} />
          </span>
        )}
      </div>
      {card.typeLine && !isAbility && (
        <div className="text-black/85 text-[10px]">{card.typeLine}</div>
      )}
      {isAbility && (
        <div className="text-cyan-400 text-[10px]">
          {card.isTriggeredAbility ? "Triggered Ability" : "Activated Ability"}
        </div>
      )}
      {!isAbility && card.isTapped && <div className="text-amber-400">Tapped</div>}
      {!isAbility && card.isAttacking && <div className="text-red-400">Attacking</div>}
      {!isAbility && card.isBlocking && <div className="text-blue-400">Blocking</div>}
      {!isAbility && nonModifierCounters.length > 0 && (
        <div className="text-black/85 text-[10px]">
          {nonModifierCounters.map(([n, c]) => `${formatCounterLabel(n)}: ${c}`).join(", ")}
        </div>
      )}
      {!isAbility && grantedAbilities.length > 0 && (
        <div className="text-black/85 text-[10px]">
          {grantedAbilities.map(formatAbility).join(", ")}
        </div>
      )}
      {card.rulesText && (
        <div className="text-black/85 text-[10px] whitespace-pre-line [&_.gl-italic]:text-black/55">
          <GameLogText text={normalizeRulesText(card.rulesText)} manaSymbolClassName="inline h-2.5 w-2.5 align-text-bottom mx-[1px]" />
        </div>
      )}
      {card.blueText && (
        <div className="text-black/85 text-[10px] whitespace-pre-line [&_.gl-italic]:text-black/55">
          <GameLogText text={normalizeRulesText(card.blueText)} manaSymbolClassName="inline h-2.5 w-2.5 align-text-bottom mx-[1px]" />
        </div>
      )}
      {!isAbility && card.power != null && (
        <div className="text-black/85">
          {card.power}/{card.toughness}
        </div>
      )}
      {footer ?? (
        <div className="text-muted-foreground/60 text-[10px]">
          Zone: {card.zone} | ID: {card.cardId}
        </div>
      )}
    </div>
  )
}

// -- Counter dice helpers --

type DiceStyle = {
  bg: string
  text: string
  border: string
  gradient: string
  textShadow: string
  boxShadow: string
}

function getCounterDiceStyle(counterName: string): DiceStyle {
  switch (counterName) {
    case "PlusOnePlusOne":
      return {
        bg: "bg-white/90",
        text: "text-gray-700",
        border: "border-gray-300",
        gradient: "linear-gradient(135deg, rgba(255,255,255,0.95) 0%, rgba(220,220,220,0.9) 100%)",
        textShadow: "1px 1px 0 rgba(255,255,255,0.8)",
        boxShadow: "0 1px 2px rgba(0,0,0,0.3), inset 0 1px 2px rgba(255,255,255,0.6), inset 0 -1px 2px rgba(0,0,0,0.1)",
      }
    case "MinusOneMinusOne":
    case "Loyalty":
      return {
        bg: "bg-gray-900/90",
        text: "text-gray-400",
        border: "border-gray-600",
        gradient: "linear-gradient(135deg, rgba(60,60,60,0.95) 0%, rgba(20,20,20,0.9) 100%)",
        textShadow: "-1px -1px 0 rgba(0,0,0,0.6), 1px 1px 0 rgba(255,255,255,0.15)",
        boxShadow: "0 1px 2px rgba(0,0,0,0.4), inset 0 1px 2px rgba(255,255,255,0.15), inset 0 -1px 2px rgba(0,0,0,0.3)",
      }
    default:
      return {
        bg: "bg-emerald-600/90",
        text: "text-emerald-100",
        border: "border-emerald-800",
        gradient: "linear-gradient(135deg, rgba(16,185,129,0.95) 0%, rgba(5,150,105,0.9) 100%)",
        textShadow: "-1px -1px 0 rgba(0,0,0,0.4), 1px 1px 0 rgba(255,255,255,0.15)",
        boxShadow: "0 1px 2px rgba(0,0,0,0.3), inset 0 1px 2px rgba(255,255,255,0.2), inset 0 -1px 2px rgba(0,0,0,0.2)",
      }
  }
}

function isLabeledCounter(name: string): boolean {
  return name !== "PlusOnePlusOne"
      && name !== "MinusOneMinusOne"
      && name !== "Loyalty"
}

const WORD_TO_NUM: Record<string, string> = {
  Zero: "0", One: "1", Two: "2", Three: "3", Four: "4",
  Five: "5", Six: "6", Seven: "7", Eight: "8", Nine: "9",
}

function isModifierCounter(name: string): boolean {
  return /^(Plus|Minus)\w+?(Plus|Minus)\w+$/.test(name)
}

function formatCounterLabel(name: string): string {
  // Match modifier patterns like PlusOnePlusOne, MinusTwoMinusOne
  const m = name.match(/^(Plus|Minus)(\w+?)(Plus|Minus)(\w+)$/)
  if (m) {
    const s1 = m[1] === "Plus" ? "+" : "-"
    const s2 = m[3] === "Plus" ? "+" : "-"
    const v1 = WORD_TO_NUM[m[2]] ?? m[2]
    const v2 = WORD_TO_NUM[m[4]] ?? m[4]
    return `${s1}${v1}/${s2}${v2}`
  }
  // Split PascalCase for non-modifier counters, e.g. "DoubleStrike" → "Double Strike"
  return name.replace(/([a-z])([A-Z])/g, "$1 $2")
}

/** Splits PascalCase into space-separated words, e.g. "FirstStrike" → "First Strike" */
function formatAbility(name: string): string {
  return name.replace(/([a-z])([A-Z])/g, "$1 $2")
}

function counterSortKey(name: string): number {
  if (name === "PlusOnePlusOne") return 0
  if (name === "MinusOneMinusOne") return 1
  if (name === "Loyalty") return 2
  return 3
}

// -- Card component --

export function CardImage({
  card,
  transition,
  tapAlignTop,
  dimmed,
  highlighted,
  isSource,
  isSourceAndTarget,
  useCellHitbox,
  peekHitboxPx,
  peekHitboxSide,
  tooltipAlignOffsetPx,
}: {
  card: CardState
  transition: BoardTransition
  /** Align tapped cards to the top of the cell (for opponent rows). Default: bottom. */
  tapAlignTop?: boolean
  /** Render at reduced opacity (e.g. exiled-under cards). */
  dimmed?: boolean
  /** Card is a target of a hovered stack spell/ability. */
  highlighted?: boolean
  /** Card is the source of the hovered stack spell/ability. */
  isSource?: boolean
  /** Card is both source and target for the hovered relation. */
  isSourceAndTarget?: boolean
  /** Use full cell bounds for hover/tooltip hitbox (useful for underlaid cards). */
  useCellHitbox?: boolean
  /** Optional width (px) for a narrow tooltip trigger strip (e.g. visible peek region). */
  peekHitboxPx?: number
  /** Which side of the card to use for the narrow trigger strip. */
  peekHitboxSide?: "left" | "right"
  /** Optional horizontal tooltip nudge (positive = right when side is top/bottom). */
  tooltipAlignOffsetPx?: number
}) {
  const hasPT = card.power != null || card.toughness != null
  const ptModified = hasPT && (card.power !== card.initialPower || card.toughness !== card.initialToughness)
  const hasCounters = Object.keys(card.counters).length > 0
  const showDebugHitbox = DEBUG_EXILED_HITBOX && useCellHitbox
  const cardBoundsStyle = card.isTapped ? {
    width: `${CARD_W_PCT}%`,
    height: `${CARD_H_PCT}%`,
    left: `${CARD_LEFT_PCT}%`,
    top: tapAlignTop ? `-${TAP_TOP_PCT}%` : `${TAP_TOP_PCT}%`,
  } : {
    width: `${CARD_W_PCT}%`,
    height: `${CARD_H_PCT}%`,
    left: `${CARD_LEFT_PCT}%`,
    top: 0,
  }
  const tappedVisualBounds = {
    left: "0%",
    width: "100%",
    top: tapAlignTop ? "0%" : `${100 - CARD_W_PCT}%`,
    height: `${CARD_W_PCT}%`,
  }
  const peekBaseBounds = card.isTapped ? {
    ...tappedVisualBounds,
  } : cardBoundsStyle
  const tooltipTriggerStyle = useCellHitbox && peekHitboxPx != null && peekHitboxPx > 0
    ? {
        ...peekBaseBounds,
        left: peekHitboxSide === "right"
          ? `calc(${peekBaseBounds.left} + ${peekBaseBounds.width} - ${peekHitboxPx}px)`
          : peekBaseBounds.left,
        width: `${peekHitboxPx}px`,
      }
    : cardBoundsStyle
  const relationHighlightClass = isSourceAndTarget
    ? "ring-2 ring-orange-400 ring-offset-2 ring-offset-blue-500 outline outline-1 outline-blue-400 shadow-[0_0_12px_rgba(59,130,246,0.45),0_0_10px_rgba(251,146,60,0.55)]"
    : isSource
      ? "ring-2 ring-blue-500 shadow-[0_0_12px_rgba(59,130,246,0.55)]"
      : highlighted
        ? "ring-2 ring-orange-400 shadow-[0_0_10px_rgba(251,146,60,0.55)]"
        : ""

  const cardElement = (
    <motion.div
              animate={{ rotate: card.isTapped ? 90 : 0 }}
              transition={CARD_TRANSITION}
              className={[
                "absolute rounded-md overflow-hidden border border-sidebar-border/60",
                useCellHitbox ? "pointer-events-none" : "pointer-events-auto",
                card.isAttacking ? "ring-2 ring-red-500 shadow-[0_0_8px_rgba(239,68,68,0.4)]" : "",
                card.isBlocking ? "ring-2 ring-blue-500/70" : "",
                relationHighlightClass,
                dimmed ? "opacity-40" : "",
              ].filter(Boolean).join(" ")}
              style={cardBoundsStyle}
            >
              <CardImg
                catalogId={card.catalogId}
                alt={card.name}
                fallback={
                  <div className="w-full h-full bg-muted/80 flex items-center justify-center p-1">
                    <span className="text-[8px] text-muted-foreground text-center leading-tight break-words">
                      {card.name}
                    </span>
                  </div>
                }
              />

              {hasPT && (
                <div className={`absolute bottom-0 right-0 bg-black/80 text-[9px] font-bold px-1 rounded-tl ${ptModified ? "text-sky-300" : "text-white"}`}>
                  {card.power}/{card.toughness}
                </div>
              )}

              {(() => {
                const grantedAbilities = card.abilities.filter(a => !(card.initialAbilities ?? []).includes(a))
                return (card.blueText || grantedAbilities.length > 0) ? (
                  <div className="absolute left-0 right-0 bg-sky-900/80 px-1 py-0.5"
                       style={{ top: '13%' }}>
                    {grantedAbilities.length > 0 && (
                      <div className="text-sky-300 text-[7px] font-semibold leading-tight">
                        {grantedAbilities.map(formatAbility).join(", ")}
                      </div>
                    )}
                    {card.blueText && (
                      <div className="text-sky-300 text-[7px] leading-tight line-clamp-3">
                        <GameLogText text={card.blueText} manaSymbolClassName="inline h-[7px] w-[7px] align-text-bottom mx-[0.5px]" />
                      </div>
                    )}
                  </div>
                ) : null
              })()}

              {hasCounters && (() => {
                const entries = Object.entries(card.counters)
                  .sort(([a], [b]) => counterSortKey(a) - counterSortKey(b))

                const dieStyle = {
                  borderRadius: "25%",
                }

                return (
                  <div
                    className="absolute left-1/2 -translate-x-1/2 flex flex-row items-center gap-0.5"
                    style={{ top: "33%" }}
                  >
                    {entries.map(([name, count]) => {
                      const s = getCounterDiceStyle(name)
                      return (
                        <div
                          key={name}
                          className={`w-[19px] h-[19px] flex items-center justify-center border ${s.text} ${s.border} text-[12px] font-bold leading-none`}
                          style={{
                            ...dieStyle,
                            background: s.gradient,
                            textShadow: s.textShadow,
                            boxShadow: s.boxShadow,
                          }}
                        >
                          {count}
                        </div>
                      )
                    })}
                  </div>
                )
              })()}
            </motion.div>
  )

  return (
    <TooltipProvider delayDuration={200}>
      <Tooltip>
        <div className="absolute inset-0 pointer-events-none">
          {showDebugHitbox && (
            <>
              {/* Full cell bounds */}
              <div className="absolute inset-0 pointer-events-none border border-cyan-400/90 bg-cyan-400/10" />
              {/* Current tooltip trigger bounds */}
              <div className="absolute pointer-events-none border-2 border-red-500/90 bg-red-500/10" style={tooltipTriggerStyle} />
            </>
          )}
          {useCellHitbox ? (
            <>
              {cardElement}
              <TooltipTrigger asChild>
                <div
                  className="absolute pointer-events-auto"
                  style={tooltipTriggerStyle}
                />
              </TooltipTrigger>
            </>
          ) : (
            <TooltipTrigger asChild>
              {cardElement}
            </TooltipTrigger>
          )}
        </div>
        <TooltipContent side="top" align="center" alignOffset={tooltipAlignOffsetPx ?? 0} className="max-w-[200px]">
          <CardTooltipContent card={card} />
        </TooltipContent>
      </Tooltip>
    </TooltipProvider>
  )
}
