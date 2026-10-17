/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import type { ReplayData, ReplaySnapshot } from "@videreproject/ui"
import type { GameInteractionData } from "@/types/api"

type ReplayResponse = Omit<ReplayData, "snapshots"> & {
  snapshots: (Omit<ReplaySnapshot, "interaction"> & {
    interaction?: GameInteractionData | null
  })[]
}

/** Adapts compact recorded flags to the shared replay UI's card view models. */
export function readReplayData(data: ReplayResponse): ReplayData {
  const cards = new Map(data.cards.map(card => [card.cardId, card]))
  return {
    ...data,
    snapshots: data.snapshots.map(snapshot => {
      const interaction = snapshot.interaction
      if (interaction == null) return { ...snapshot, interaction }

      const selectable = new Set(interaction.selectable)
      const targetable = new Set(interaction.targetable)
      const targeted = new Set(interaction.targeted)
      const casting = new Set(interaction.casting)
      const dimmed = new Set(interaction.dimmed)
      return {
        ...snapshot,
        interaction: {
          mode: interaction.mode ?? "Undefined",
          promptText: interaction.promptText ?? null,
          players: (interaction.players ?? []).map(player => ({
            playerIndex: player.playerIndex!,
            isTargetable: player.isTargetable!,
            isTargeted: player.isTargeted!,
            isActive: player.isActive!,
          })),
          cards: [
            ...[...cards.values()].map(card => ({
              logicalAddress: "",
              cardId: card.cardId,
              sourceId: card.sourceId ?? -1,
              hasActions: false,
              actions: [],
              isSelectable: selectable.has(card.cardId),
              isTargetable: targetable.has(card.cardId),
              isTargeted: targeted.has(card.cardId),
              isBeingCast: casting.has(card.cardId),
              isActive: !dimmed.has(card.cardId),
            })),
            ...(interaction.abilities ?? []).map(ability => ({
              ...ability,
              logicalAddress: ability.logicalAddress!,
              cardId: -1,
              sourceId: -1,
              hasActions: false,
              actions: [],
              isSelectable: ability.isSelectable!,
              isTargetable: ability.isTargetable!,
              isTargeted: ability.isTargeted!,
            })),
          ],
        },
      }
    }),
  }
}
