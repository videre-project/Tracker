import React, { useRef, useState, useEffect, useCallback, useMemo } from "react"
import { motion, AnimatePresence, LayoutGroup } from "framer-motion"
import type { BoardState, CardState, PlayerState, BoardTransition } from "@/types/replay-types"
import { EMPTY_TRANSITION } from "@/types/replay-types"
import { Tooltip, TooltipContent, TooltipTrigger, TooltipProvider } from "@/components/ui/tooltip"
import type { GameAction } from "@/types/game-types"
import { parseCardName } from "@/types/game-types"
import { getApiUrl } from "@/utils/api-config"
import { GameLogText } from "@/utils/parse-game-log"
import { getManaSymbolSvgPath } from "@/utils/mana-symbols"
import { useCardImage } from "@/hooks/use-card-image"

// -- Layout constants --

/**
 * Card aspect ratio and cell geometry (all relative to cell side = 1).
 *
 * Cards are 5:7 (width:height). Each grid cell is a square whose side equals
 * the card height, so a tapped (90°-rotated) card fits within the same cell.
 * All positioning uses percentages so cells scale with available space.
 */
const CARD_RATIO = 5 / 7                      // card width / card height
const CARD_W_PCT = CARD_RATIO * 100           // card width as % of cell  (~71.4%)
const CARD_H_PCT = 100                        // card height as % of cell (100%)
const CARD_LEFT_PCT = (100 - CARD_W_PCT) / 2  // center card horizontally (~14.3%)

// Tapped card: after 90° rotation the visual box is 100% × 71.4%.
// Shift the layout box down so visual bottom = cell bottom.
const TAP_TOP_PCT = (100 - CARD_W_PCT) / 2    // = CARD_LEFT_PCT (~14.3%)


const CARD_TRANSITION = { type: "spring" as const, stiffness: 320, damping: 32 }
const STACK_MAX = 10
const DEBUG_EXILED_HITBOX = false

/** How much of the top zone bar peeks out above the battlefield (labels + card tops). */
const TOP_BAR_PEEK = 50
const TOP_BAR_FULL = 180

// -- Cached card image component --

/**
 * Renders a card image from the persistent cache (Cache API + in-memory).
 * Resolves CDN → MTGO fallback automatically; subsequent renders are instant.
 */
function CardImg({
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
  if (src) return <img src={src} alt={alt} className={className} style={style} />
  return <>{fallback}</>
}


// -- Reveal→destination flying card animation --

/**
 * Stores the last known screen rects of revealed cards so we can
 * animate a clone flying from the overlay to wherever the card lands.
 */
const revealedRectsRef = { current: new Map<number, DOMRect>() }

function RevealedCardCell({ card, colIdx, rowIdx }: { card: CardState; colIdx: number; rowIdx: number }) {
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
function FlyingCardLayer({
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

const CONTROLLER_ZONES = new Set(["Battlefield", "Stack"])

function getPlayerZoneCards(
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
function isCreature(c: CardState): boolean {
  return (c.power != null && c.power !== "")
      || (c.toughness != null && c.toughness !== "")
}

// -- Card tooltip --

function CardTooltipContent({ card, footer }: { card: CardState; footer?: React.ReactNode }) {
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

function CardImage({
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

// -- Revealed zone overlay --

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

function RevealedZoneOverlay({
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

type StackHoverRelation = {
  sourceCardId: number
  targetIds: number[]
}

function StackOverlay({
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

// -- Combat phase helpers --

const COMBAT_PHASES = new Set([
  "BeginCombat",
  "DeclareAttackers",
  "DeclareBlockers",
  "CombatDamage",
  "EndOfCombat",
])

/** Fraction of row height that attacking creatures shift toward center. */
const ATTACK_SHIFT_FRACTION = 0.85

// -- Battlefield row --

/**
 * Renders a single battlefield row as a centered flex row of square cells.
 *
 * Each cell is a square (aspect-ratio: 1) whose height matches the row.
 * Cards scale proportionally — on large viewports cells approach their ideal
 * size, on small viewports they shrink to fit. Cards within cells use
 * percentage-based positioning so they scale with the cell.
 */
function BattlefieldRow({
  cards,
  transition,
  alignEnd,
  tapAlignTop,
  idPrefix = "card",
  attackShiftDir = 0,
  combatSlots = null,
  combatRole = null,
  allBoardCards,
  highlightedCardIds,
  sourceCardIds,
  overlapCardIds,
}: {
  cards: CardState[]
  transition: BoardTransition
  /** Pack cards toward the bottom of the row (for opponent rows). */
  alignEnd?: boolean
  /** Align tapped cards to the top of their cell (for opponent rows). */
  tapAlignTop?: boolean
  idPrefix?: string
  /** Direction attacking/blocking creatures shift: -1 = up (our side), +1 = down (opponent side), 0 = none. */
  attackShiftDir?: -1 | 0 | 1
  /** Combat slot assignments for horizontal pairing of attackers and blockers. */
  combatSlots?: { groups: Map<number, { slotIndex: number }>; totalSlots: number; totalColumns: number } | null
  /** In mirrored mode, restrict which cards get combat-slotted: "attacker" or "blocker". */
  combatRole?: "attacker" | "blocker" | null
  /** Full board card map for resolving cross-row attachment targets. */
  allBoardCards?: Map<number, CardState>
  /** Set of card IDs to highlight as targets. */
  highlightedCardIds?: Set<number> | null
  /** Set of card IDs to highlight as source cards. */
  sourceCardIds?: Set<number> | null
  /** Set of card IDs that are both source and target. */
  overlapCardIds?: Set<number> | null
}) {
  const rowRef = useRef<HTMLDivElement>(null)
  const [rowH, setRowH] = useState(0)

  useEffect(() => {
    if (!rowRef.current) return
    const observer = new ResizeObserver(([entry]) => {
      setRowH(entry.contentRect.height)
    })
    observer.observe(rowRef.current)
    return () => observer.disconnect()
  }, [])

  // Combat zone is ~16% of the battlefield; each row is ~22%, so the zone
  // height ≈ (16/22) × rowH.  Each side gets half the zone so attackers from
  // both sides meet closer to the center without overlapping.
  const combatZoneH = rowH * (16 / 22)
  const padding = 2
  const rawShift = rowH * ATTACK_SHIFT_FRACTION
  const maxShift = Math.max(0, combatZoneH / 2 - padding)
  const baseShift = Math.min(rawShift, maxShift)

  // Tapped cards' visual top sits ~28.6% lower in the cell (bottom-aligned
  // rotation).  Add compensation so tapped attackers meet the same y-line as
  // untapped ones.
  const tapCompensation = rowH * (100 - CARD_W_PCT) / 100

  const getYShift = (card: CardState): number => {
    const isInCombat = card.isAttacking || (card.isBlocking && card.blockingOrderIds.length > 0)
    if (!isInCombat || attackShiftDir === 0) return 0
    const extra = card.isTapped ? tapCompensation : 0
    return (baseShift + extra) * attackShiftDir
  }

  // Build a render list with combat cards placed at their slot positions and
  // spacers for empty slots, ensuring both opposing rows share the same
  // column layout so attackers and blockers line up vertically.
  type RenderEntry =
    | { type: "card"; card: CardState }
    | { type: "spacer"; key: string }
    | { type: "stack"; cards: CardState[] }
    | { type: "attached-group"; parent: CardState; attachments: CardState[] }

  const renderEntries: RenderEntry[] = useMemo(() => {
    // Build attachment groups: cards attached to a parent on the board
    const attachmentMap = new Map<number, CardState[]>()  // parentId → attached cards
    const attachedCardIds = new Set<number>()
    if (allBoardCards) {
      for (const card of cards) {
        if (card.attachedToId > 0 && allBoardCards.has(card.attachedToId)) {
          const parentId = card.attachedToId
          if (!attachmentMap.has(parentId)) attachmentMap.set(parentId, [])
          attachmentMap.get(parentId)!.push(card)
          attachedCardIds.add(card.cardId)
        }
      }
    }

    // Emit a card or attached-group entry depending on whether the card has attachments
    const emitCardEntry = (card: CardState): RenderEntry => {
      const attachments = attachmentMap.get(card.cardId)
      if (attachments && attachments.length > 0) {
        return { type: "attached-group", parent: card, attachments }
      }
      return { type: "card", card }
    }

    // Helper to group cards by name into stacks, but keep combat cards ungrouped
    const groupCardsByName = (cardList: CardState[]): RenderEntry[] => {
      // Filter out cards that are attached to a parent (they render as part of their parent's group)
      const freeCards = cardList.filter(c => !attachedCardIds.has(c.cardId))

      // Separate combat cards (blockers, or attackers with blockers) from non-combat
      const combatCards: CardState[] = []
      const nonCombatCards: CardState[] = []
      for (const card of freeCards) {
        const isBlocking = card.isBlocking && card.blockingOrderIds.length > 0
        const isBlockedAttacker = card.isAttacking && card.attackingOrderIds.length > 0
        if (isBlocking || isBlockedAttacker) {
          combatCards.push(card)
        } else {
          nonCombatCards.push(card)
        }
      }

      // Group non-combat cards by name into stacks.
      // Only stack tokens and lands — real cards may have distinct game state
      // (counters, abilities, equipment, etc.) and should render individually.
      const nameGroups = new Map<string, CardState[]>()
      const groupOrder: string[] = []
      for (const card of nonCombatCards) {
        const canStack = card.isToken || card.isLand
        const key = canStack ? card.name : `__unique_${card.cardId}`
        if (!nameGroups.has(key)) {
          nameGroups.set(key, [])
          groupOrder.push(key)
        }
        const group = nameGroups.get(key)!
        if (group.length < STACK_MAX) {
          group.push(card)
        } else {
          // Start a new group if stack is full
          const newName = `${key}__overflow_${groupOrder.filter(n => n.startsWith(key + '__')).length + 1}`
          nameGroups.set(newName, [card])
          groupOrder.push(newName)
        }
      }

      // Build entries: combat cards first (ungrouped), then grouped non-combat
      const entries: RenderEntry[] = []
      for (const card of combatCards) {
        entries.push(emitCardEntry(card))
      }
      for (const name of groupOrder) {
        const group = nameGroups.get(name)!
        if (group.length === 1 && !group[0].isLand && !group[0].isToken) {
          // Non-stackable single cards render as individual flex items
          entries.push(emitCardEntry(group[0]))
        } else {
          // Stackable cards (lands, tokens) always render inside a stack
          // container — even with 1 card — so the container structure is
          // stable when more cards join (no flex slot add/remove).
          entries.push({ type: "stack", cards: group })
        }
      }
      return entries
    }

    if (!combatSlots || combatSlots.totalSlots === 0)
      return groupCardsByName(cards)

    // Bucket this row's cards by slot index.
    // When combatRole is set (mirrored mode), only slot cards matching the
    // role — the rest go into normal (non-slotted) positioning.
    const slotBuckets = new Map<number, CardState[]>()
    const rest: CardState[] = []
    for (const card of cards) {
      const slot = combatSlots.groups.get(card.cardId)
      const matchesRole = combatRole == null
        || (combatRole === "attacker" && card.isAttacking)
        || (combatRole === "blocker" && card.isBlocking)
      if (slot != null && matchesRole) {
        const bucket = slotBuckets.get(slot.slotIndex) ?? []
        bucket.push(card)
        slotBuckets.set(slot.slotIndex, bucket)
      } else {
        rest.push(card)
      }
    }

    // Emit one entry per slot (card, stack, or spacer), preserving column
    // alignment. Multiple blockers sharing a slot are stacked together.
    const entries: RenderEntry[] = []
    for (let i = 0; i < combatSlots.totalSlots; i++) {
      const bucket = slotBuckets.get(i)
      if (bucket && bucket.length > 1) {
        entries.push({ type: "stack", cards: bucket })
      } else if (bucket && bucket.length === 1) {
        entries.push(emitCardEntry(bucket[0]))
      } else {
        entries.push({ type: "spacer", key: `spacer-${i}` })
      }
    }

    // Append non-combat cards after the combat slots, grouping by name into stacks
    const restEntries = groupCardsByName(rest)
    entries.push(...restEntries)

    // Pad with trailing spacers so both rows have identical total column
    // count, ensuring justify-center aligns combat slots at the same x.
    const currentCount = entries.length
    for (let i = currentCount; i < combatSlots.totalColumns; i++) {
      entries.push({ type: "spacer", key: `pad-${i}` })
    }

    return entries
  }, [cards, combatSlots, combatRole, allBoardCards])

  // During combat, add a horizontal gap so tapped attackers (which fill
  // 100% of cell width) don't touch/overlap adjacent cells.
  const combatGap = combatSlots && combatSlots.totalSlots > 0 && attackShiftDir !== 0
    ? Math.round(rowH * 0.14)
    : 0

  return (
    <div
      ref={rowRef}
      className={[
        "flex-1 flex flex-row justify-center",
        alignEnd ? "items-end" : "items-start",
      ].join(" ")}
      style={combatGap > 0 ? { gap: `${combatGap}px` } : undefined}
    >
      <AnimatePresence>
        {renderEntries.map(entry => {
          if (entry.type === "spacer") {
            return (
              <div
                key={entry.key}
                className="relative h-full"
                style={{ aspectRatio: "1" }}
              />
            )
          }
          if (entry.type === "stack") {
            const { cards } = entry

            // Group cards by name into sub-groups so identical tokens
            // cluster as a tight inner stack within the outer fan.
            const subGroups: CardState[][] = []
            const subGroupMap = new Map<string, CardState[]>()
            for (const card of cards) {
              const key = (card.isToken || card.isLand) ? card.name : `__unique_${card.cardId}`
              let group = subGroupMap.get(key)
              if (!group) {
                group = []
                subGroupMap.set(key, group)
                subGroups.push(group)
              }
              group.push(card)
            }

            // Keep untapped cards visually ahead of tapped cards within each stack.
            const orderedSubGroups = subGroups.map(group => {
              const untapped: CardState[] = []
              const tapped: CardState[] = []
              for (const card of group) {
                if (card.isTapped) tapped.push(card)
                else untapped.push(card)
              }
              return [...untapped, ...tapped]
            })

            const innerFanStep = rowH * 0.12
            const outerFanStep = rowH * (orderedSubGroups.length === 1 ? 0.12 : 0.35)

            // Total width: sum of each sub-group's width, with outer fan
            // gaps between sub-groups.
            let totalWidth = 0
            for (let g = 0; g < orderedSubGroups.length; g++) {
              if (g > 0) totalWidth += outerFanStep
              else totalWidth += rowH
              if (orderedSubGroups[g].length > 1)
                totalWidth += (orderedSubGroups[g].length - 1) * innerFanStep
            }

            // Flatten sub-groups into positioned cards.
            let xOffset = 0
            let globalZ = cards.length
            const positioned: { card: CardState; left: number; z: number }[] = []
            for (let g = 0; g < orderedSubGroups.length; g++) {
              if (g > 0) xOffset += outerFanStep
              for (let i = 0; i < orderedSubGroups[g].length; i++) {
                positioned.push({
                  card: orderedSubGroups[g][i],
                  left: xOffset + i * innerFanStep,
                  z: globalZ--,
                })
              }
            }

            return (
              <div
                key={`stack-${cards[0].cardId}`}
                className="relative h-full flex-shrink-0"
                style={{ width: totalWidth, minWidth: totalWidth }}
              >
                {positioned.map(({ card, left, z }, idx) => {
                  const isTopmostStackCard = idx === 0
                  const useRightPeekTooltip = !isTopmostStackCard
                  const prevCard = idx > 0 ? positioned[idx - 1].card : null
                  const tapPeekBonus = rowH * CARD_LEFT_PCT / 100
                  const stackPeekHitboxPx = useRightPeekTooltip
                    ? (card.isTapped && prevCard && !prevCard.isTapped
                        ? innerFanStep + tapPeekBonus
                        : innerFanStep)
                    : undefined
                  return (
                    <motion.div
                      key={card.cardId}
                      layoutId={`${idPrefix}-${card.lineageId}`}
                      transition={{
                        ...CARD_TRANSITION,
                        y: { type: "tween", duration: 0.25, ease: "easeOut" },
                      }}
                      animate={{
                        y: getYShift(card),
                      }}
                      className="absolute top-0 bottom-0 pointer-events-none"
                      style={{ left: `${left}px`, width: rowH, zIndex: z }}
                    >
                      <CardImage
                        card={card}
                        transition={transition}
                        tapAlignTop={tapAlignTop}
                        highlighted={highlightedCardIds?.has(card.cardId)}
                        isSource={sourceCardIds?.has(card.cardId)}
                        isSourceAndTarget={overlapCardIds?.has(card.cardId)}
                        useCellHitbox={useRightPeekTooltip}
                        peekHitboxPx={stackPeekHitboxPx}
                        peekHitboxSide={useRightPeekTooltip ? "right" : undefined}
                      />
                    </motion.div>
                  )
                })}
                {orderedSubGroups.map((group) => {
                  if (group.length <= 1) return null
                  const baseLeft = positioned.find(p => p.card.cardId === group[0].cardId)!.left
                  const fullyTapped = group.every(c => c.isTapped)
                  const fullyTappedOnTopHalf = tapAlignTop && fullyTapped
                  const leftNudge = fullyTapped ? rowH * CARD_LEFT_PCT / 100 : 0
                  const badgeCornerClass = fullyTapped ? "rounded-tl" : "rounded-tr"
                  return (
                    <div
                      key={`count-${group[0].cardId}`}
                      className={`absolute bottom-0 z-20 bg-black/80 text-white text-[9px] font-bold px-1 py-0.5 ${badgeCornerClass} pointer-events-none`}
                      style={{
                        left: `${baseLeft - leftNudge}px`,
                        bottom: fullyTappedOnTopHalf
                          ? `${rowH * (100 - CARD_W_PCT) / 100}px`
                          : "0px",
                      }}
                    >
                      {group.length}
                    </div>
                  )
                })}
              </div>
            )
          }
          if (entry.type === "attached-group") {
            const { parent, attachments } = entry
            const fanStep = rowH * 0.12
            const totalCards = 1 + attachments.length
            const groupWidth = rowH + (totalCards - 1) * fanStep
            return (
              <div
                key={`attach-${parent.cardId}`}
                className="relative h-full flex-shrink-0"
                style={{ width: groupWidth, minWidth: groupWidth }}
              >
                {attachments.map((att, i) => (
                  <motion.div
                    key={att.cardId}
                    layoutId={`${idPrefix}-${att.lineageId}`}
                    layout
                    transition={CARD_TRANSITION}
                    className="absolute top-0 bottom-0"
                    style={{
                      left: `${i * fanStep}px`,
                      width: rowH,
                      zIndex: i + 1,
                    }}
                  >
                    <CardImage
                      card={att}
                      transition={transition}
                      tapAlignTop={tapAlignTop}
                      dimmed={att.isExiledOnBattlefield}
                      highlighted={highlightedCardIds?.has(att.cardId)}
                      isSource={sourceCardIds?.has(att.cardId)}
                      isSourceAndTarget={overlapCardIds?.has(att.cardId)}
                      useCellHitbox={att.isExiledOnBattlefield}
                      peekHitboxPx={att.isExiledOnBattlefield ? fanStep : undefined}
                      tooltipAlignOffsetPx={att.isExiledOnBattlefield ? 6 : undefined}
                    />
                  </motion.div>
                ))}
                <motion.div
                  key={parent.cardId}
                  layoutId={`${idPrefix}-${parent.lineageId}`}
                  layout
                  transition={{
                    ...CARD_TRANSITION,
                    y: { type: "tween", duration: 0.25, ease: "easeOut" },
                  }}
                  animate={{ y: getYShift(parent) }}
                  className="absolute top-0 bottom-0 pointer-events-none"
                  style={{
                    left: `${attachments.length * fanStep}px`,
                    width: rowH,
                    zIndex: totalCards,
                  }}
                >
                  <CardImage
                    card={parent}
                    transition={transition}
                    tapAlignTop={tapAlignTop}
                    highlighted={highlightedCardIds?.has(parent.cardId)}
                    isSource={sourceCardIds?.has(parent.cardId)}
                    isSourceAndTarget={overlapCardIds?.has(parent.cardId)}
                  />
                </motion.div>
              </div>
            )
          }
          const card = entry.card
          const isInCombat = (card.isAttacking || card.isBlocking) && attackShiftDir !== 0
          return (
            <motion.div
              key={card.cardId}
              layoutId={`${idPrefix}-${card.lineageId}`}
              layout={isInCombat}
              transition={{
                ...CARD_TRANSITION,
                y: { type: "tween", duration: 0.25, ease: "easeOut" },
              }}
              animate={{
                y: getYShift(card),
              }}
              className={[
                "relative h-full",
                isInCombat ? "z-10" : "",
              ].filter(Boolean).join(" ")}
              style={{ aspectRatio: "1" }}
            >
              <CardImage
                card={card}
                transition={transition}
                tapAlignTop={tapAlignTop}
                highlighted={highlightedCardIds?.has(card.cardId)}
                isSource={sourceCardIds?.has(card.cardId)}
                isSourceAndTarget={overlapCardIds?.has(card.cardId)}
              />
            </motion.div>
          )
        })}
      </AnimatePresence>
    </div>
  )
}

// -- Chess clock --

/** Max reasonable clock value — anything above this is a sentinel for "no limit". */
const MAX_CLOCK_SECONDS = 7200 // 2 hours

function formatClock(totalSeconds: number): string {
  const mins = Math.floor(totalSeconds / 60)
  const secs = Math.floor(totalSeconds % 60)
  return `${mins.toString().padStart(2, "0")}:${secs.toString().padStart(2, "0")}`
}

type ManaPoolRow = {
  symbol: string
  amount: number
}

function colorIdToSymbol(color: number): string {
  switch (color) {
    case 1: return "{W}"
    case 2: return "{U}"
    case 4: return "{B}"
    case 8: return "{R}"
    case 16: return "{G}"
    case 32: return "{C}"
    default: {
      const symbol = [
        (color & 1) !== 0 ? "{W}" : "",
        (color & 2) !== 0 ? "{U}" : "",
        (color & 4) !== 0 ? "{B}" : "",
        (color & 8) !== 0 ? "{R}" : "",
        (color & 16) !== 0 ? "{G}" : "",
        (color & 32) !== 0 ? "{C}" : "",
      ].join("")
      return symbol || `{${color}}`
    }
  }
}

function parseManaPoolRows(manaPool: string | null): ManaPoolRow[] {
  if (!manaPool) return []

  try {
    const parsed = JSON.parse(manaPool)
    if (!Array.isArray(parsed)) return []

    const rows: ManaPoolRow[] = []
    for (const entry of parsed as any[]) {
      const amountRaw = entry?.amount ?? entry?.Amount
      const amount = typeof amountRaw === "number"
        ? amountRaw
        : Number.parseInt(String(amountRaw ?? "0"), 10)
      if (!Number.isFinite(amount) || amount <= 0) continue

      const symbolRaw = entry?.symbol ?? entry?.Symbol
      const symbol = typeof symbolRaw === "string" && symbolRaw.length > 0
        ? symbolRaw
        : colorIdToSymbol(Number(entry?.color ?? entry?.Color ?? 0))

      rows.push({ symbol, amount })
    }

    return rows
  } catch {
    return []
  }
}

function ManaPoolBox({ manaPool }: { manaPool: string | null }) {
  const rows = parseManaPoolRows(manaPool)
  if (rows.length === 0) return null
  const amountColumnWidth = `${Math.max(...rows.map(r => `${r.amount}`.length))}ch`

  const renderManaSymbols = (symbolText: string) => {
    const tokens = symbolText.match(/\{([^}]+)\}/g)?.map(t => t.slice(1, -1)) ?? [symbolText]
    return (
      <span className="inline-flex items-center gap-[2px]">
        {tokens.map((token, i) => {
          const path = getManaSymbolSvgPath(token.toUpperCase())
          if (path) {
            return (
              <img
                key={`${token}-${i}`}
                src={path}
                alt={token}
                className="h-3 w-3"
              />
            )
          }
          return <span key={`${token}-${i}`}>{`{${token}}`}</span>
        })}
      </span>
    )
  }

  return (
    <div className="inline-block bg-black/70 rounded-sm px-1.5 py-0.5 border border-sidebar-border/40">
      <div className="text-[9px] leading-tight space-y-0.5">
        {rows.map((row, idx) => (
          <div
            key={`${row.symbol}-${idx}`}
            className="grid items-center gap-x-1"
            style={{ gridTemplateColumns: "auto auto" }}
          >
            <span className="inline-flex items-center h-4 text-foreground/90 whitespace-nowrap">
              {renderManaSymbols(row.symbol)}
            </span>
            <span className="inline-flex items-center justify-end h-4 font-mono tabular-nums leading-none whitespace-nowrap">
              <span className="text-[9px] text-foreground/60 pr-0.5">x</span>
              <span
                className="text-[11px] font-semibold text-right text-foreground/90"
                style={{ width: amountColumnWidth }}
              >
                {row.amount}
              </span>
            </span>
          </div>
        ))}
      </div>
    </div>
  )
}

// -- Player avatar badge --

function PlayerAvatar({
  player,
  clockSeconds,
}: {
  player: PlayerState
  clockSeconds?: number
}) {
  const [artUrl, setArtUrl] = useState<string | null>(null)

  useEffect(() => {
    if (player.avatarId <= 0) return

    let cancelled = false
    ;(async () => {
      try {
        const res = await fetch(
          getApiUrl(`/api/collection/cards/${player.avatarId}/art`)
        )
        if (!res.ok || cancelled) return
        const blob = await res.blob()
        if (cancelled) return
        setArtUrl(URL.createObjectURL(blob))
      } catch { /* ignore */ }
    })()

    return () => { cancelled = true }
  }, [player.avatarId])

  const showClock = clockSeconds != null && clockSeconds > 0
  const displayClockSeconds = clockSeconds != null && clockSeconds >= MAX_CLOCK_SECONDS ? 0 : (clockSeconds ?? 0)

  return (
    <div className={[
      "flex items-center gap-2 bg-black/70 rounded pl-0.5 pr-2.5 py-0.5",
      player.hasPriority ? "border border-white/80" : "",
    ].filter(Boolean).join(" ")}>
      <div className="relative w-8 h-8 rounded overflow-hidden shrink-0 bg-muted/40">
        {artUrl ? (
          <img
            src={artUrl}
            alt={player.name}
            className="w-full h-full object-cover"
          />
        ) : (
          <div className="w-full h-full flex items-center justify-center text-[9px] text-muted-foreground font-bold">
            {player.name.charAt(0).toUpperCase()}
          </div>
        )}
        <span
          className="absolute inset-0 flex items-center justify-center mt-1 text-lg font-bold text-white leading-none font-mono tabular-nums"
          style={{ WebkitTextStroke: "2px black", paintOrder: "stroke fill" }}
        >
          {player.life}
        </span>
      </div>
      <div className="flex flex-col">
        <span className="text-xs font-semibold text-foreground/90 whitespace-nowrap">
          {player.name}
        </span>
        {showClock && (
          <span className="text-[10px] font-mono font-bold text-foreground/60 tabular-nums">
            {formatClock(displayClockSeconds)}
          </span>
        )}
      </div>
    </div>
  )
}

// -- Phase ladder --

const PHASE_LADDER = [
  { key: "Untap", label: "Untap" },
  { key: "Upkeep", label: "Upkeep" },
  { key: "Draw", label: "Draw" },
  { key: "PreCombatMain", label: "Main 1" },
  { key: "BeginCombat", label: "Combat" },
  { key: "DeclareAttackers", label: "Attack" },
  { key: "DeclareBlockers", label: "Block" },
  { key: "CombatDamage", label: "Damage" },
  { key: "EndOfCombat", label: "End Combat" },
  { key: "PostCombatMain", label: "Main 2" },
  { key: "EndOfTurn", label: "End" },
  { key: "Cleanup", label: "Cleanup" },
]

function PhaseLadder({
  currentPhase,
  turn,
  activePlayerName,
}: {
  currentPhase: string
  turn: number
  activePlayerName?: string
}) {
  return (
    <div className="flex items-center py-1 bg-muted/20 border-t border-sidebar-border/60 shrink-0">
      {/* Turn indicator — same width as the prompt box so phases align with hand */}
      <div className="w-[200px] shrink-0 px-3 border-r border-transparent flex items-center">
        {turn > 0 && (
          <span className="text-[11px] font-medium text-foreground/70 whitespace-nowrap leading-none">
            Turn {turn}{activePlayerName ? `: ${activePlayerName}` : ""}
          </span>
        )}
      </div>

      {/* Phase steps — left-aligned with hand content */}
      <div className="flex-1 flex items-center gap-0 min-w-0 pl-2.5 pr-4">
        {PHASE_LADDER.map((phase, i) => {
          const isCurrent = phase.key === currentPhase
          const isPast = PHASE_LADDER.findIndex(p => p.key === currentPhase) > i
          return (
            <React.Fragment key={phase.key}>
              {i > 0 && (
                <div className={`w-2 h-px shrink-0 ${
                  isPast ? "bg-sidebar-accent/40" : "bg-sidebar-border/40"
                }`} />
              )}
              <span
                className={[
                  "text-[11px] px-1.5 py-1 rounded-md whitespace-nowrap transition-colors leading-none",
                  isCurrent
                    ? "bg-sidebar-accent text-sidebar-accent-foreground font-medium"
                    : isPast
                      ? "text-muted-foreground/60"
                      : "text-muted-foreground/35",
                ].join(" ")}
              >
                {phase.label}
              </span>
            </React.Fragment>
          )
        })}
      </div>
    </div>
  )
}

// -- Bottom zone bar (Prompt | Hand | Lib/GY/Exile) --

const ZONE_GAP_PX = 12
const ZONE_PAD_X = 16
const ZONE_COUNT = 3
const PROMPT_W = 200
const ZONE_INNER_PAD_Y = 10
const ZONE_LABEL_ROW_H = 20
const ZONE_CARD_H = TOP_BAR_FULL - (ZONE_INNER_PAD_Y * 2) - ZONE_LABEL_ROW_H
const ZONE_CARD_W = ZONE_CARD_H * CARD_RATIO
const DEFAULT_HAND_WIDTH = 520
const MIN_HAND_OFFSET = 20

function useElementWidth<T extends HTMLElement>(ref: React.RefObject<T | null>, fallbackWidth: number) {
  const [width, setWidth] = useState(fallbackWidth)

  useEffect(() => {
    const el = ref.current
    if (!el) return

    const updateWidth = () => {
      setWidth(el.clientWidth || fallbackWidth)
    }

    updateWidth()
    if (typeof ResizeObserver === "undefined") return

    const observer = new ResizeObserver(([entry]) => {
      setWidth(entry.contentRect.width || fallbackWidth)
    })
    observer.observe(el)
    return () => observer.disconnect()
  }, [fallbackWidth, ref])

  return width
}

function getHandOffset(handCount: number, cardWidth: number, availableWidth: number) {
  if (handCount <= 1) return cardWidth
  const span = Math.max(0, availableWidth - cardWidth)
  return Math.min(cardWidth, Math.max(MIN_HAND_OFFSET, span / (handCount - 1)))
}

interface ZonePileEntry {
  label: string
  count: number
  topCard?: CardState
  isLibrary?: boolean
  cards?: CardState[]
}

function ZoneBrowsePanel({
  label,
  cards,
  onClose,
  anchorRefs,
  position = "above",
}: {
  label: string
  cards: CardState[]
  onClose: () => void
  /** Clicks on these anchors should not trigger outside-close. */
  anchorRefs?: Array<HTMLElement | null>
  position?: "above" | "below"
}) {
  const panelRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    const handleClick = (e: MouseEvent) => {
      const target = e.target as Node
      const clickedAnchor = anchorRefs?.some(el => el?.contains(target))
      if (clickedAnchor) return
      if (panelRef.current && !panelRef.current.contains(target)) onClose()
    }
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose()
    }
    document.addEventListener("mousedown", handleClick)
    document.addEventListener("keydown", handleKey)
    return () => {
      document.removeEventListener("mousedown", handleClick)
      document.removeEventListener("keydown", handleKey)
    }
  }, [onClose, anchorRefs])

  const posClass = position === "above"
    ? "bottom-full right-0 mb-2"
    : "top-full right-0 mt-2"

  return (
    <motion.div
      ref={panelRef}
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.15 }}
      className={`absolute ${posClass} z-30 bg-background border border-sidebar-border/60 rounded-lg shadow-xl overflow-hidden`}
      style={{ width: "min(420px, 100%)" }}
    >
      <div className="flex items-center justify-between px-3 py-1.5 border-b border-sidebar-border/40">
        <span className="text-xs font-semibold text-muted-foreground uppercase tracking-wider">
          {label} ({cards.length})
        </span>
        <button onClick={onClose} className="text-muted-foreground hover:text-foreground text-xs px-1">
          &times;
        </button>
      </div>
      <div className="overflow-y-auto max-h-[280px] p-2">
        <div className="grid grid-cols-4 gap-1.5">
          {cards.map(card => (
            <TooltipProvider key={card.cardId} delayDuration={200}>
              <Tooltip>
                <TooltipTrigger asChild>
                  <div className="rounded-md overflow-hidden border border-sidebar-border/60 bg-muted/20" style={{ aspectRatio: "5/7" }}>
                    <CardImg
                      catalogId={card.catalogId}
                      alt={card.name}
                      fallback={<div className="w-full h-full flex items-center justify-center p-1"><span className="text-[8px] text-muted-foreground text-center leading-tight break-words">{card.name}</span></div>}
                    />
                  </div>
                </TooltipTrigger>
                <TooltipContent side="top" className="max-w-[200px]">
                  <CardTooltipContent card={card} />
                </TooltipContent>
              </Tooltip>
            </TooltipProvider>
          ))}
        </div>
      </div>
    </motion.div>
  )
}

function BottomZoneBar({
  hand,
  zones,
  transition,
  promptText,
  promptOptions,
}: {
  hand: CardState[]
  zones: ZonePileEntry[]
  transition: BoardTransition
  promptText?: string
  promptOptions: GameAction[]
}) {
  const [browseZone, setBrowseZone] = useState<ZonePileEntry | null>(null)
  const zoneToggleRefs = useRef<Record<string, HTMLDivElement | null>>({})
  const handAreaRef = useRef<HTMLDivElement>(null)
  const handAreaWidth = useElementWidth(handAreaRef, DEFAULT_HAND_WIDTH)
  const handOffset = getHandOffset(hand.length, ZONE_CARD_W, handAreaWidth)

  return (
    <div className="flex border-t border-sidebar-border/60 bg-muted/10 shrink-0" style={{ height: TOP_BAR_FULL }}>
      <PromptBox promptText={promptText} promptOptions={promptOptions} />

      <div ref={handAreaRef} className="flex-1 min-w-0 overflow-clip px-4 py-2.5">
        <div className="flex items-center gap-1.5 mb-1.5">
          <span className="text-[10px] font-semibold text-muted-foreground uppercase tracking-wider">Hand</span>
          <span className="text-[10px] text-muted-foreground/60">({hand.length})</span>
        </div>
        {hand.length > 0 && (
          <div className="relative" style={{ height: ZONE_CARD_H }}>
            <AnimatePresence>
              {hand.map((card, i) => (
                <motion.div
                  key={card.cardId}
                  layoutId={`card-${card.lineageId}`}
                  layout
                  transition={CARD_TRANSITION}
                  className="absolute top-0 rounded-md overflow-hidden border border-sidebar-border/60"
                  style={{ left: i * handOffset, width: ZONE_CARD_W, height: ZONE_CARD_H, zIndex: hand.length - i }}
                >
                  <TooltipProvider delayDuration={200}>
                    <Tooltip>
                      <TooltipTrigger asChild>
                        <div className="w-full h-full">
                          <CardImg
                            catalogId={card.catalogId}
                            alt={card.name}
                            fallback={<div className="w-full h-full bg-muted/80 flex items-center justify-center p-1"><span className="text-[8px] text-muted-foreground text-center leading-tight break-words">{card.name}</span></div>}
                          />
                        </div>
                      </TooltipTrigger>
                      <TooltipContent side="top" className="max-w-[200px]">
                        <CardTooltipContent card={card} />
                      </TooltipContent>
                    </Tooltip>
                  </TooltipProvider>
                </motion.div>
              ))}
            </AnimatePresence>
          </div>
        )}
      </div>

      <div className="flex gap-3 px-4 py-2.5 border-l border-sidebar-border/40 shrink-0 items-start relative">
        {zones.map(zone => {
          const browsable = !zone.isLibrary && zone.cards && zone.cards.length > 0
          const isBrowseOpen = browseZone?.label === zone.label
          return (
            <div key={zone.label} className="flex flex-col items-start">
              <span className="text-[10px] font-semibold text-muted-foreground uppercase tracking-wider whitespace-nowrap mb-1.5">
                {zone.label} <span className="text-muted-foreground/60 font-normal">({zone.count})</span>
              </span>
              <div
                ref={el => { zoneToggleRefs.current[zone.label] = el }}
                className={[
                  "rounded-md border border-sidebar-border/60 overflow-hidden relative bg-muted/20",
                  browsable ? "cursor-pointer group/zone" : "",
                  browsable ? "transition-[border-color,box-shadow] hover:border-sidebar-accent/70 hover:shadow-[0_0_0_1px_rgba(255,255,255,0.15)]" : "",
                  isBrowseOpen ? "border-sidebar-accent ring-1 ring-sidebar-accent/70" : "",
                ].filter(Boolean).join(" ")}
                style={{ width: ZONE_CARD_W, height: ZONE_CARD_H }}
                onClick={browsable ? () => setBrowseZone(prev => (prev?.label === zone.label ? null : zone)) : undefined}
                role={browsable ? "button" : undefined}
                aria-expanded={browsable ? isBrowseOpen : undefined}
              >
                {zone.isLibrary && zone.count > 0 ? (
                  <img src="/backface.png" alt="Library" className="w-full h-full object-cover" />
                ) : zone.topCard ? (
                  <motion.div layoutId={`card-${zone.topCard.lineageId}`} layout transition={CARD_TRANSITION} className="absolute inset-0">
                    <CardImg
                      catalogId={zone.topCard.catalogId}
                      alt={zone.topCard.name}
                      fallback={<div className="w-full h-full flex items-center justify-center p-1"><span className="text-[8px] text-muted-foreground text-center leading-tight break-words">{zone.topCard.name}</span></div>}
                    />
                  </motion.div>
                ) : null}
                {browsable && (
                  <div className="absolute inset-0 pointer-events-none bg-black/25 opacity-0 group-hover/zone:opacity-100 transition-opacity duration-150" />
                )}
                {browsable && (
                  <div className={[
                    "absolute inset-0 flex items-center justify-center pointer-events-none transition-opacity duration-150",
                    "opacity-0 group-hover/zone:opacity-100 group-focus-within/zone:opacity-100",
                  ].join(" ")}>
                    <span className={[
                      "rounded-md px-2 py-1 text-[10px] font-medium leading-none shadow-sm",
                      isBrowseOpen
                        ? "bg-sidebar-accent text-sidebar-accent-foreground"
                        : "bg-black/70 text-foreground/90",
                    ].join(" ")}>
                      {isBrowseOpen ? "Hide" : "Browse"}
                    </span>
                  </div>
                )}
              </div>
            </div>
          )
        })}
        <AnimatePresence>
          {browseZone && (
            <ZoneBrowsePanel
              label={browseZone.label}
              cards={browseZone.cards!}
              onClose={() => setBrowseZone(null)}
              anchorRefs={Object.values(zoneToggleRefs.current)}
            />
          )}
        </AnimatePresence>
      </div>
    </div>
  )
}

function TopZoneBar({
  hand,
  zones,
  transition,
  headerContent,
  idPrefix = "card",
}: {
  hand: CardState[]
  zones: ZonePileEntry[]
  transition: BoardTransition
  headerContent?: React.ReactNode
  idPrefix?: string
}) {
  const [browseZone, setBrowseZone] = useState<ZonePileEntry | null>(null)
  const zoneToggleRefs = useRef<Record<string, HTMLDivElement | null>>({})
  const [isExpanded, setIsExpanded] = useState(false)
  const handAreaRef = useRef<HTMLDivElement>(null)
  const handAreaWidth = useElementWidth(handAreaRef, DEFAULT_HAND_WIDTH)
  const handOffset = getHandOffset(hand.length, ZONE_CARD_W, handAreaWidth)

  return (
    <div
      className={isExpanded || browseZone ? "absolute inset-x-0 top-0 z-30" : "absolute inset-x-0 top-0 z-0"}
      onMouseEnter={() => setIsExpanded(true)}
      onMouseLeave={() => setIsExpanded(false)}
    >
      <div className={["overflow-hidden", "transition-[height,box-shadow] duration-300 ease-in-out", isExpanded ? "shadow-xl" : ""].join(" ")} style={{ height: isExpanded ? TOP_BAR_FULL : TOP_BAR_PEEK }}>
        <div className="flex border-t border-x border-sidebar-border/60 bg-background" style={{ height: TOP_BAR_FULL }}>
          <div className="shrink-0 flex flex-col justify-start px-3 py-2.5 border-r border-sidebar-border/40 pointer-events-auto relative z-20" style={{ width: PROMPT_W }}>
            {headerContent}
          </div>

          <div ref={handAreaRef} className="flex-1 min-w-0 overflow-clip px-4 py-2.5">
            <div className="flex items-center gap-1.5 mb-1.5">
              <span className="text-[10px] font-semibold text-muted-foreground uppercase tracking-wider">Hand</span>
              <span className="text-[10px] text-muted-foreground/60">({hand.length})</span>
            </div>
            {hand.length > 0 && (
              <div className="relative" style={{ height: ZONE_CARD_H }}>
                <AnimatePresence>
                  {hand.map((card, i) => (
                    <motion.div
                      key={card.cardId}
                      layoutId={`${idPrefix}-${card.lineageId}`}
                      layout
                      transition={CARD_TRANSITION}
                      className="absolute top-0 rounded-md overflow-hidden border border-sidebar-border/60"
                      style={{ left: i * handOffset, width: ZONE_CARD_W, height: ZONE_CARD_H, zIndex: hand.length - i }}
                    >
                      <TooltipProvider delayDuration={200}>
                        <Tooltip>
                          <TooltipTrigger asChild>
                            <div className="w-full h-full">
                              <CardImg
                                catalogId={card.catalogId}
                                alt={card.name}
                                fallback={<div className="w-full h-full bg-muted/80 flex items-center justify-center p-1"><span className="text-[8px] text-muted-foreground text-center leading-tight break-words">{card.name}</span></div>}
                              />
                            </div>
                          </TooltipTrigger>
                          <TooltipContent side="bottom" className="max-w-[200px]">
                            <CardTooltipContent card={card} />
                          </TooltipContent>
                        </Tooltip>
                      </TooltipProvider>
                    </motion.div>
                  ))}
                </AnimatePresence>
              </div>
            )}
          </div>

          <div className="flex gap-3 px-4 py-2.5 border-l border-sidebar-border/40 shrink-0 items-start pointer-events-auto relative z-20">
            {zones.map(zone => {
              const browsable = !zone.isLibrary && zone.cards && zone.cards.length > 0
              const isBrowseOpen = browseZone?.label === zone.label
              return (
                <div key={zone.label} className="flex flex-col items-start">
                  <span className="text-[10px] font-semibold text-muted-foreground uppercase tracking-wider whitespace-nowrap mb-1.5">
                    {zone.label} <span className="text-muted-foreground/60 font-normal">({zone.count})</span>
                  </span>
                  <div
                    ref={el => { zoneToggleRefs.current[zone.label] = el }}
                    className={[
                      "rounded-md border border-sidebar-border/60 overflow-hidden relative bg-muted/20",
                      browsable ? "cursor-pointer group/zone" : "",
                      browsable ? "transition-[border-color,box-shadow] hover:border-sidebar-accent/70 hover:shadow-[0_0_0_1px_rgba(255,255,255,0.15)]" : "",
                      isBrowseOpen ? "border-sidebar-accent ring-1 ring-sidebar-accent/70" : "",
                    ].filter(Boolean).join(" ")}
                    style={{ width: ZONE_CARD_W, height: ZONE_CARD_H }}
                    onClick={browsable ? () => setBrowseZone(prev => (prev?.label === zone.label ? null : zone)) : undefined}
                    role={browsable ? "button" : undefined}
                    aria-expanded={browsable ? isBrowseOpen : undefined}
                  >
                    {zone.isLibrary && zone.count > 0 ? (
                      <img src="/backface.png" alt="Library" className="w-full h-full object-cover" />
                    ) : zone.topCard ? (
                      <motion.div layoutId={`${idPrefix}-${zone.topCard.lineageId}`} layout transition={CARD_TRANSITION} className="absolute inset-0">
                        <CardImg
                          catalogId={zone.topCard.catalogId}
                          alt={zone.topCard.name}
                          fallback={<div className="w-full h-full flex items-center justify-center p-1"><span className="text-[8px] text-muted-foreground text-center leading-tight break-words">{zone.topCard.name}</span></div>}
                        />
                      </motion.div>
                    ) : null}
                    {browsable && (
                      <div className="absolute inset-0 pointer-events-none bg-black/25 opacity-0 group-hover/zone:opacity-100 transition-opacity duration-150" />
                    )}
                    {browsable && (
                      <div className={[
                        "absolute inset-0 flex items-center justify-center pointer-events-none transition-opacity duration-150",
                        "opacity-0 group-hover/zone:opacity-100 group-focus-within/zone:opacity-100",
                      ].join(" ")}>
                        <span className={[
                          "rounded-md px-2 py-1 text-[10px] font-medium leading-none shadow-sm",
                          isBrowseOpen
                            ? "bg-sidebar-accent text-sidebar-accent-foreground"
                            : "bg-black/70 text-foreground/90",
                        ].join(" ")}>
                          {isBrowseOpen ? "Hide" : "Browse"}
                        </span>
                      </div>
                    )}
                  </div>
                </div>
              )
            })}
          </div>
        </div>
      </div>
      <AnimatePresence>
        {browseZone && (
          <ZoneBrowsePanel
            label={browseZone.label}
            cards={browseZone.cards!}
            onClose={() => setBrowseZone(null)}
            anchorRefs={Object.values(zoneToggleRefs.current)}
            position="below"
          />
        )}
      </AnimatePresence>
    </div>
  )
}

// -- Prompt box --

function optionLabel(action: GameAction): string {
  switch (action.$type) {
    case "PrimitiveAction":
    case "ConcedeGameAction":
    case "UndoAction":
    case "FunctionKeyMessageAction":
    case "ToggleMessageAction":
    case "LocalAction":
      return action.name ?? action.$type
    case "CardAction":
    case "DistributingCardAction": {
      const card = parseCardName((action as any).card)
      return card
        ? `${action.name ?? "Play"}: ${card}`
        : action.name ?? "Play"
    }
    case "SelectFromListAction": {
      const items = (action as any).availableItems as { name?: string }[] | undefined
      if (items && items.length <= 3) {
        return items.map(i => i.name ?? "?").join(" / ")
      }
      return action.name ?? "Select"
    }
    case "NumericAction": {
      const a = action as any
      return `Choose (${a.minimum ?? 0}–${a.maximum ?? "?"})`
    }
    case "SelectPlayerAction":
      return action.name ?? "Choose player"
    case "CardSelectorAction":
      return action.name ?? "Choose card"
    default:
      return action.name ?? action.$type
  }
}

function PromptBox({
  promptText,
  promptOptions,
}: {
  promptText?: string
  promptOptions: GameAction[]
}) {
  const buttons = promptOptions.filter(a =>
    a.$type === "PrimitiveAction" ||
    a.$type === "ConcedeGameAction" ||
    a.$type === "UndoAction" ||
    a.$type === "SelectFromListAction" ||
    a.$type === "NumericAction" ||
    a.$type === "SelectPlayerAction"
  )
  const cardActions = promptOptions.filter(a =>
    a.$type === "CardAction" || a.$type === "DistributingCardAction"
  )

  return (
    <div className="flex flex-col justify-between w-[200px] shrink-0 border-r border-sidebar-border/40 px-3 py-2.5">
      <div className="flex-1 min-w-0">
        {promptText && (
          <GameLogText text={promptText} className="text-[11px] text-foreground/80 leading-tight" />
        )}
      </div>
      {(buttons.length > 0 || cardActions.length > 0) && (
        <div className="flex flex-wrap items-center gap-1.5 mt-2">
          {buttons.map((opt, i) => (
            <span
              key={i}
              className="inline-flex items-center px-2.5 py-1 rounded text-[10px] font-medium bg-muted/60 text-foreground/70 border border-sidebar-border/50"
            >
              {optionLabel(opt)}
            </span>
          ))}
          {cardActions.length > 0 && (
            <span className="text-[9px] text-muted-foreground/50 italic">
              +{cardActions.length} card {cardActions.length === 1 ? "action" : "actions"}
            </span>
          )}
        </div>
      )}
    </div>
  )
}

// -- Main board view --

export interface BoardViewProps {
  board: BoardState
  transition?: BoardTransition
  perspectivePlayer?: number
  promptText?: string
  promptOptions?: string | null
  /** Content rendered in the top-left pane (aligned with the prompt box below). */
  headerContent?: React.ReactNode
}

export function BoardView({
  board,
  transition = EMPTY_TRANSITION,
  perspectivePlayer,
  promptText,
  promptOptions,
  headerContent,
}: BoardViewProps) {
  const playerEntries = Array.from(board.players.entries())
  if (playerEntries.length === 0) {
    return (
      <div className="flex items-center justify-center h-full text-muted-foreground text-sm">
        No game state loaded
      </div>
    )
  }

  const bottomIdx = perspectivePlayer ?? playerEntries[0][0]
  const bottomPlayer = board.players.get(bottomIdx)
  const topEntry = playerEntries.find(([idx]) => idx !== bottomIdx)
  // Single-player mirroring: if only one player, use the same player as opponent
  const topPlayer = topEntry ? topEntry[1] : bottomPlayer ?? null
  const topIdx = topEntry ? topEntry[0] : bottomIdx
  const isMirrored = !topEntry && playerEntries.length === 1

  const topBattlefieldRaw = getPlayerZoneCards(board, "Battlefield", topIdx)
  const bottomBattlefieldRaw = getPlayerZoneCards(board, "Battlefield", bottomIdx)

  // Inject exiled-under cards into battlefield — they display under their
  // parent permanent even though their actual zone is Exile.
  // The exiled card may belong to a different player than the parent (e.g.
  // The Princess Takes Flight exiles an opponent's creature), so we scan
  // ALL exile cards and inject into whichever side owns the parent.
  const allExile = board.zones.get("Exile") ?? []
  const topBfIds = new Set(topBattlefieldRaw.map(c => c.cardId))
  const bottomBfIds = new Set(bottomBattlefieldRaw.map(c => c.cardId))
  const topExiledUnder: CardState[] = []
  const bottomExiledUnder: CardState[] = []
  for (const c of allExile) {
    if (c.attachedToId <= 0) continue
    const clone = { ...c, isExiledOnBattlefield: true }
    if (topBfIds.has(c.attachedToId)) {
      topExiledUnder.push(clone)
    }
    if (bottomBfIds.has(c.attachedToId)) {
      bottomExiledUnder.push(isMirrored ? { ...clone } : clone)
    }
  }
  const topBattlefield = [...topBattlefieldRaw, ...topExiledUnder]
  const bottomBattlefield = [...bottomBattlefieldRaw, ...bottomExiledUnder]

  // Split battlefield into creature/non-creature rows, but move attached cards
  // to whichever row their parent is in (e.g. a creature exiled under a saga
  // should appear in the non-creatures row with the saga, not the creatures row).
  const splitWithAttachments = (bf: CardState[], wantCreatures: boolean): CardState[] => {
    const bfById = new Map(bf.map(c => [c.cardId, c]))
    const result: CardState[] = []
    for (const c of bf) {
      if (c.attachedToId > 0) {
        // Attached card goes to whichever row contains the parent
        const parent = bfById.get(c.attachedToId)
        if (parent) {
          if (isCreature(parent) === wantCreatures) result.push(c)
          continue
        }
      }
      // Free card: split by creature/non-creature as normal
      if (isCreature(c) === wantCreatures) result.push(c)
    }
    return result
  }

  // Build combat groups: map each card in combat to a horizontal slot index.
  // Attackers get sequential slots; blockers share the slot of their attacker.
  // Both creature rows are padded to the same total column count so
  // justify-center aligns their slots identically.
  const combatSlots = useMemo(() => {
    if (!COMBAT_PHASES.has(board.phase)) return null
    // Deduplicate cards by cardId (mirrored single-player shares both sides)
    const seen = new Set<number>()
    const allCards: CardState[] = []
    for (const c of [...topBattlefield, ...bottomBattlefield]) {
      if (!seen.has(c.cardId)) {
        seen.add(c.cardId)
        allCards.push(c)
      }
    }

    const groups = new Map<number, { slotIndex: number }>()

    const attackers = allCards.filter(c => c.isAttacking)
    attackers.sort((a, b) => a.cardId - b.cardId)

    attackers.forEach((atk, i) => {
      groups.set(atk.cardId, { slotIndex: i })
    })

    const blockers = allCards.filter(c => c.isBlocking && c.blockingOrderIds.length > 0)
    for (const blocker of blockers) {
      const attackerId = blocker.blockingOrderIds[0]
      const attackerGroup = groups.get(attackerId)
      if (attackerGroup) {
        groups.set(blocker.cardId, { slotIndex: attackerGroup.slotIndex })
      }
    }

    // Compute total columns each row needs (combat slots + rest cards) and
    // use the max so both rows have identical entry counts for centering.
    // "Rest" includes non-combat creatures AND, in mirrored mode,
    // role-mismatched combat cards (attackers on the blocker row, blockers
    // on the attacker row) that aren't placed into slot buckets.
    const topCreatures = topBattlefield.filter(c => isCreature(c))
    const bottomCreatures = bottomBattlefield.filter(c => isCreature(c))
    const topNonCombat = topCreatures.filter(c => !groups.has(c.cardId)).length
    const bottomNonCombat = bottomCreatures.filter(c => !groups.has(c.cardId)).length

    // In mirrored mode the top row uses combatRole="blocker" so attackers
    // with slots go to rest; the bottom row uses "attacker" so blockers do.
    const topRoleMismatch = isMirrored
      ? topCreatures.filter(c => groups.has(c.cardId) && c.isAttacking && !c.isBlocking).length
      : 0
    const bottomRoleMismatch = isMirrored
      ? bottomCreatures.filter(c => groups.has(c.cardId) && c.isBlocking && !c.isAttacking).length
      : 0

    const topRestCount = topNonCombat + topRoleMismatch
    const bottomRestCount = bottomNonCombat + bottomRoleMismatch
    const totalColumns = attackers.length + Math.max(topRestCount, bottomRestCount)

    return { groups, totalSlots: attackers.length, totalColumns }
  }, [board.phase, topBattlefield, bottomBattlefield, isMirrored])

  const bottomHand = getPlayerZoneCards(board, "Hand", bottomIdx)
  const bottomGraveyard = getPlayerZoneCards(board, "Graveyard", bottomIdx)
  // Exclude exiled-under cards from exile pile display (they're shown on the battlefield)
  const allBfIds = new Set([...topBfIds, ...bottomBfIds])
  const bottomExile = getPlayerZoneCards(board, "Exile", bottomIdx)
    .filter(c => !(c.attachedToId > 0 && allBfIds.has(c.attachedToId)))

  // For mirrored single-player, opponent zones are the same as ours
  const topHand = isMirrored ? bottomHand : getPlayerZoneCards(board, "Hand", topIdx)
  const topGraveyard = isMirrored ? bottomGraveyard : getPlayerZoneCards(board, "Graveyard", topIdx)
  const topExile = isMirrored ? bottomExile
    : getPlayerZoneCards(board, "Exile", topIdx)
      .filter(c => !(c.attachedToId > 0 && allBfIds.has(c.attachedToId)))

  const stackCards = board.zones.get("Stack") ?? []

  // Stack relation highlighting: source on stack (blue), targets on battlefield (orange).
  const [activeStackRelation, setActiveStackRelation] = useState<StackHoverRelation | null>(null)
  const highlightedCardIds = useMemo(
    () => (activeStackRelation ? new Set(activeStackRelation.targetIds) : null),
    [activeStackRelation],
  )
  const sourceCardIds = useMemo(
    () => (activeStackRelation ? new Set([activeStackRelation.sourceCardId]) : null),
    [activeStackRelation],
  )
  const overlapCardIds = useMemo(() => {
    if (!activeStackRelation) return null
    return activeStackRelation.targetIds.includes(activeStackRelation.sourceCardId)
      ? new Set([activeStackRelation.sourceCardId])
      : null
  }, [activeStackRelation])
  const activeSourceCardId = activeStackRelation?.sourceCardId ?? null
  const handleStackHover = useCallback((relation: StackHoverRelation | null) => {
    setActiveStackRelation(relation)
  }, [])

  let parsedPromptOptions: GameAction[] = []
  if (promptOptions) {
    try { parsedPromptOptions = JSON.parse(promptOptions) } catch { /* */ }
  }

  return (
    <LayoutGroup id="board">
    <div className="flex flex-col h-full border-r border-sidebar-border/60 rounded-lg bg-background relative overflow-clip">
      {/* Top zone bar: opponent Hand | Lib/GY/Exile — absolutely positioned behind */}
      <TopZoneBar
        hand={topHand}
        zones={[
          { label: "Library", count: topPlayer?.libraryCount ?? 0, isLibrary: true },
          { label: "Graveyard", count: topGraveyard.length, topCard: topGraveyard.at(-1), cards: topGraveyard },
          { label: "Exile", count: topExile.length, topCard: topExile.at(-1), cards: topExile },
        ]}
        transition={transition}
        headerContent={headerContent}
        idPrefix="card-opponent"
      />

      {/* Spacer to let the top zone bar peek through — pointer-events-none so it doesn't block the header */}
      <div className="shrink-0 pointer-events-none" style={{ height: TOP_BAR_PEEK }} />

      {/* Everything below here sits above the top zone bar */}
      <div className="flex flex-col flex-1 min-h-0 relative z-10 border-t border-sidebar-border/60 bg-background">

      {/* Battlefield area — 4 rows (2 per player), expands to fill space */}
      {(() => {
        const isCombat = COMBAT_PHASES.has(board.phase)
        return (
      <div className="flex flex-col flex-1 min-h-0 relative px-4 pt-2 pb-2">
        {/* Player avatars — positioned relative to the full battlefield area so
            they stay fixed when the combat zone appears/disappears */}
        {topPlayer && (
          <div className="absolute bottom-1/2 left-4 z-20 mb-2">
            <PlayerAvatar
              player={topPlayer}
              clockSeconds={topPlayer.clockRemaining ?? undefined}
            />
          </div>
        )}
        {bottomPlayer && (
          <div className="absolute top-1/2 left-4 z-20 mt-2">
            <PlayerAvatar
              player={bottomPlayer}
              clockSeconds={bottomPlayer.clockRemaining ?? undefined}
            />
          </div>
        )}
        {topPlayer && (
          <div className="absolute top-2 left-4 z-20">
            <ManaPoolBox manaPool={topPlayer.manaPool} />
          </div>
        )}
        {bottomPlayer && (
          <div className="absolute bottom-2 left-4 z-20">
            <ManaPoolBox manaPool={bottomPlayer.manaPool} />
          </div>
        )}

        {/* Opponent half */}
        <div className={`flex flex-col flex-1 min-h-0 -mx-3 -my-1 px-3 py-1 rounded-lg ${isCombat ? "mb-1" : ""} ${topPlayer?.isActivePlayer && !bottomPlayer?.isActivePlayer ? "bg-white/5" : ""}`}>
          <BattlefieldRow
            cards={splitWithAttachments(topBattlefield, false)}
            transition={transition}
            alignEnd
            tapAlignTop
            idPrefix="card-opponent"
            allBoardCards={board.cards}
            highlightedCardIds={highlightedCardIds}
            sourceCardIds={sourceCardIds}
            overlapCardIds={overlapCardIds}
          />
          <div className="shrink-0 h-1" />
          <BattlefieldRow
            cards={splitWithAttachments(topBattlefield, true)}
            transition={transition}
            alignEnd
            tapAlignTop
            idPrefix="card-opponent"
            attackShiftDir={isCombat ? 1 : 0}
            combatSlots={combatSlots}
            combatRole={isMirrored ? "blocker" : null}
            allBoardCards={board.cards}
            highlightedCardIds={highlightedCardIds}
            sourceCardIds={sourceCardIds}
            overlapCardIds={overlapCardIds}
          />
        </div>

        {/* Center divider / combat zone */}
        {isCombat ? (
          <motion.div
            key="combat-zone"
            initial={{ height: 0, opacity: 0 }}
            animate={{ height: "12%", opacity: 1 }}
            exit={{ height: 0, opacity: 0 }}
            transition={CARD_TRANSITION}
            className="shrink-0 -mx-7 border-y border-red-500/50 bg-red-900/15"
          />
        ) : (
          <div className="border-t border-dashed border-sidebar-border/30 shrink-0 my-1" />
        )}

        {/* Our half */}
        <div className={`flex flex-col flex-1 min-h-0 -mx-3 -my-1 px-3 py-1 rounded-lg ${isCombat ? "mt-1" : ""} ${bottomPlayer?.isActivePlayer ? "bg-white/5" : ""}`}>
          <BattlefieldRow
            cards={splitWithAttachments(bottomBattlefield, true)}
            transition={transition}
            attackShiftDir={isCombat ? -1 : 0}
            combatSlots={combatSlots}
            combatRole={isMirrored ? "attacker" : null}
            allBoardCards={board.cards}
            highlightedCardIds={highlightedCardIds}
            sourceCardIds={sourceCardIds}
            overlapCardIds={overlapCardIds}
          />
          <div className="shrink-0 h-1" />
          <BattlefieldRow
            cards={splitWithAttachments(bottomBattlefield, false)}
            transition={transition}
            allBoardCards={board.cards}
            highlightedCardIds={highlightedCardIds}
            sourceCardIds={sourceCardIds}
            overlapCardIds={overlapCardIds}
          />
        </div>

        {/* Revealed / pile zone overlay */}
        <RevealedZoneOverlay board={board} transition={transition} />

        {/* Flying card animation layer */}
        <FlyingCardLayer board={board} transition={transition} />

        {/* Stack overlay */}
        <StackOverlay
          cards={stackCards}
          transition={transition}
          onHoverCard={handleStackHover}
          activeSourceCardId={activeSourceCardId}
        />
      </div>
        )
      })()}

      {/* Phase ladder — z-10 to sit above the absolutely-positioned top zone bar */}
      <PhaseLadder
        currentPhase={board.phase}
        turn={board.turn}
        activePlayerName={
          playerEntries.find(([, p]) => p.isActivePlayer)?.[1]?.name
        }
      />

      {/* Bottom zone bar: Prompt | Hand | Lib/GY/Exile */}
      <BottomZoneBar
        hand={bottomHand}
        zones={[
          { label: "Library", count: bottomPlayer?.libraryCount ?? 0, isLibrary: true },
          { label: "Graveyard", count: bottomGraveyard.length, topCard: bottomGraveyard.at(-1), cards: bottomGraveyard },
          { label: "Exile", count: bottomExile.length, topCard: bottomExile.at(-1), cards: bottomExile },
        ]}
        transition={transition}
        promptText={promptText}
        promptOptions={parsedPromptOptions}
      />
      </div>{/* end z-10 wrapper */}
    </div>
    </LayoutGroup>
  )
}
