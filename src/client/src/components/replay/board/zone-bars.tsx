/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import React, { useRef, useState, useEffect } from "react"
import { motion, AnimatePresence } from "framer-motion"
import type { CardState, BoardTransition } from "@/types/replay-types"
import { Tooltip, TooltipContent, TooltipTrigger, TooltipProvider } from "@/components/ui/tooltip"
import type { GameAction } from "@/types/game-types"
import { parseCardName } from "@/types/game-types"
import { GameLogText } from "@/utils/parse-game-log"
import {
  CARD_RATIO,
  CARD_TRANSITION,
  TOP_BAR_FULL,
  TOP_BAR_PEEK,
  CardImg,
  CardTooltipContent,
} from "./card-visuals"

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

export function BottomZoneBar({
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

export function TopZoneBar({
  hand,
  handCount,
  zones,
  transition,
  headerContent,
  idPrefix = "card",
}: {
  hand: CardState[]
  handCount: number
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
  const totalHandCount = Math.max(hand.length, handCount)
  const hiddenHandCount = Math.max(0, totalHandCount - hand.length)
  const handOffset = getHandOffset(totalHandCount, ZONE_CARD_W, handAreaWidth)

  return (
    <div
      className={isExpanded || browseZone ? "absolute inset-x-0 top-0 z-30" : "absolute inset-x-0 top-0 z-0"}
      onMouseEnter={() => setIsExpanded(true)}
      onMouseLeave={() => setIsExpanded(false)}
    >
      <div className={["overflow-hidden", "transition-[height,box-shadow] duration-300 ease-in-out", isExpanded ? "shadow-xl" : ""].join(" ")} style={{ height: isExpanded ? TOP_BAR_FULL : TOP_BAR_PEEK }}>
        <div className="flex border-t border-x border-sidebar-border/60 bg-background" style={{ height: TOP_BAR_FULL }}>
          <div className="shrink-0 flex flex-col justify-start px-4 pt-[6px] pb-3 border-r border-sidebar-border/40 pointer-events-auto relative z-20" style={{ width: PROMPT_W }}>
            {headerContent}
          </div>

          <div ref={handAreaRef} className="flex-1 min-w-0 overflow-clip px-4 py-2.5">
            <div className="flex items-center gap-1.5 mb-1.5">
              <span className="text-[10px] font-semibold text-muted-foreground uppercase tracking-wider">Hand</span>
              <span className="text-[10px] text-muted-foreground/60">({totalHandCount})</span>
            </div>
            {totalHandCount > 0 && (
              <div className="relative" style={{ height: ZONE_CARD_H }}>
                <AnimatePresence>
                  {hand.map((card, i) => (
                    <motion.div
                      key={card.cardId}
                      layoutId={`${idPrefix}-${card.lineageId}`}
                      layout
                      transition={CARD_TRANSITION}
                      className="absolute top-0 rounded-md overflow-hidden border border-sidebar-border/60"
                      style={{ left: i * handOffset, width: ZONE_CARD_W, height: ZONE_CARD_H, zIndex: totalHandCount - i }}
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
                  {Array.from({ length: hiddenHandCount }, (_, i) => {
                    const position = hand.length + i
                    return (
                      <motion.div
                        key={`hidden-hand-${i}`}
                        initial={{ opacity: 0 }}
                        animate={{ opacity: 1 }}
                        exit={{ opacity: 0 }}
                        transition={CARD_TRANSITION}
                        className="absolute top-0 rounded-md overflow-hidden border border-sidebar-border/60"
                        style={{
                          left: position * handOffset,
                          width: ZONE_CARD_W,
                          height: ZONE_CARD_H,
                          zIndex: totalHandCount - position,
                        }}
                      >
                        <img src="/backface.png" alt="Hidden card" className="w-full h-full object-cover" />
                      </motion.div>
                    )
                  })}
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
