import React, { useState, useCallback, useMemo } from "react"
import { LayoutGroup, motion } from "framer-motion"
import type { BoardState, CardState, BoardTransition } from "@/types/replay-types"
import { EMPTY_TRANSITION } from "@/types/replay-types"
import type { GameAction } from "@/types/game-types"

import { BattlefieldRow, COMBAT_PHASES } from "./board/battlefield-row"
import {
  CARD_TRANSITION,
  FlyingCardLayer,
  TOP_BAR_PEEK,
  getPlayerZoneCards,
  isCreature,
} from "./board/card-visuals"
import {
  RevealedZoneOverlay,
  StackOverlay,
  type StackHoverRelation,
} from "./board/overlays"
import { ManaPoolBox, PhaseLadder, PlayerAvatar } from "./board/player-status"
import { BottomZoneBar, TopZoneBar } from "./board/zone-bars"
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
