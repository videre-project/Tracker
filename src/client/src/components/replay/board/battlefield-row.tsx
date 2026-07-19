/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import React, { useEffect, useMemo, useRef, useState } from "react"
import { AnimatePresence, motion } from "framer-motion"
import type { CardState, BoardTransition } from "@/types/replay-types"
import { CARD_LEFT_PCT, CARD_TRANSITION, CARD_W_PCT, STACK_MAX, CardImage } from "./card-visuals"

export const COMBAT_PHASES = new Set([
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
export function BattlefieldRow({
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
    | { type: "stack"; key: string; cards: CardState[] }
    | { type: "attached-group"; key: string; parent: CardState; attachments: CardState[] }

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

    // Flatten nested attachment relationships into a single rendered fan so
    // every descendant remains visible with its direct attachment nearest to
    // the parent, regardless of zone or attachment type.
    const nestedAttachments = new Map<number, CardState[]>()
    const collectAttachments = (
      parentId: number,
      path: Set<number> = new Set(),
    ): CardState[] => {
      const cached = nestedAttachments.get(parentId)
      if (cached) return cached
      if (path.has(parentId)) return []

      const nextPath = new Set(path)
      nextPath.add(parentId)
      const result: CardState[] = []
      for (const attachment of attachmentMap.get(parentId) ?? []) {
        // Put deeper attachments first so the direct attachment remains
        // closest to its parent in the rendered fan.
        result.push(...collectAttachments(attachment.cardId, nextPath))
        result.push(attachment)
      }
      nestedAttachments.set(parentId, result)
      return result
    }
    // Emit a card or attached-group entry depending on whether the card has attachments
    const emitCardEntry = (card: CardState, entryKey?: string): RenderEntry => {
      const attachments = collectAttachments(card.cardId)
      if (attachments && attachments.length > 0) {
        return { type: "attached-group", key: entryKey ?? String(card.cardId), parent: card, attachments }
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
      const stackGroupState = new Map<string, {currentIndex: number; baseKey: string}>()
      for (const card of nonCombatCards) {
        // A stackable card with attachments must remain its own render entry.
        // Otherwise the ordinary land/token stack would absorb the parent and
        // the attachment group would not have a distinct visual anchor.
        const hasAttachments = attachmentMap.has(card.cardId)
        const canStack = (card.isToken || card.isLand) && !hasAttachments
        const key = canStack ? card.name : `__unique_${card.cardId}`
        if (!nameGroups.has(key)) {
          nameGroups.set(key, [])
          groupOrder.push(key)
        }

        if (!canStack) {
          nameGroups.get(key)!.push(card)
          continue
        }

        const state = stackGroupState.get(key) ?? { currentIndex: 0, baseKey: key }
        let currentGroupKey = state.currentIndex === 0 ? state.baseKey : `${state.baseKey}__overflow_${state.currentIndex}`
        let group = nameGroups.get(currentGroupKey)
        if (!group) {
          group = []
          nameGroups.set(currentGroupKey, group)
          groupOrder.push(currentGroupKey)
        }

        if (group.length >= STACK_MAX) {
          const nextIndex = state.currentIndex + 1
          currentGroupKey = `${state.baseKey}__overflow_${nextIndex}`
          state.currentIndex = nextIndex
          let overflowGroup = nameGroups.get(currentGroupKey)
          if (!overflowGroup) {
            overflowGroup = []
            nameGroups.set(currentGroupKey, overflowGroup)
            groupOrder.push(currentGroupKey)
          }
          overflowGroup.push(card)
          stackGroupState.set(state.baseKey, state)
          continue
        }

        group.push(card)
        stackGroupState.set(state.baseKey, state)
      }

      // Build entries: combat cards first (ungrouped), then grouped non-combat
      const entries: RenderEntry[] = []
      for (const card of combatCards) {
        entries.push(emitCardEntry(card))
      }
      for (const name of groupOrder) {
        const group = nameGroups.get(name)!
        if (group.length === 1 &&
            (!group[0].isLand && !group[0].isToken ||
             attachmentMap.has(group[0].cardId))) {
          // Standalone cards and cards with attachments render individually
          const card = group[0]
          // Keep attachment-group keys anchored to the parent identity.
          // The key remains stable as the surrounding stack changes, preventing
          // Framer from remapping the attachment fan during regrouping.
          const attachmentKey = (card.isLand || card.isToken)
            ? `stack-${card.name}-${card.cardId}`
            : String(card.cardId)
          entries.push(emitCardEntry(card, attachmentKey))
        } else {
          // Stackable cards (lands, tokens) always render inside a stack
          // container — even with 1 card — so the container structure is
          // stable when more cards join (no flex slot add/remove).
          entries.push({ type: "stack", key: `stack-${name}-${group[0].cardId}`, cards: group })
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
        entries.push({ type: "stack", key: `combat-${i}`, cards: bucket })
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
                key={entry.key}
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
                key={entry.key}
                className="relative h-full flex-shrink-0"
                style={{ width: groupWidth, minWidth: groupWidth }}
              >
                {attachments.map((att, i) => (
                  <motion.div
                    key={att.cardId}
                    layoutId={`${idPrefix}-${att.lineageId}`}
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
                  // The parent is already on the battlefield. Avoid a shared zone projection.
                  // Reparenting from a stack into this wrapper uses position-only layout
                  // so the parent transition remains separate from the attachment entry.
                  layout="position"
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