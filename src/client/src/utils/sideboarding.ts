/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import type { SideboardingCard } from "@videreproject/ui"
import type { SideboardChangeDTO } from "@/types/api"

export function getSideboardingDiff(changes?: SideboardChangeDTO[] | null) {
  const added = new Map<number, SideboardingCard>()
  const removed = new Map<number, SideboardingCard>()

  for (const change of changes ?? []) {
    const quantity = change.quantity ?? 0
    const catalogId = change.catalogId ?? 0
    if (quantity === 0 || catalogId <= 0) continue

    const cards = quantity > 0 ? added : removed
    const direction = quantity > 0 ? "in" : "out"
    const amount = Math.abs(quantity)
    const existing = cards.get(catalogId)
    if (existing) {
      existing.quantity += amount
      continue
    }

    cards.set(catalogId, {
      key: `${direction}:${catalogId}`,
      name: change.name?.trim() || `Card ID #${catalogId}`,
      quantity: amount,
      catalogId,
    })
  }

  const byName = (a: SideboardingCard, b: SideboardingCard) =>
    a.name.localeCompare(b.name) || (a.catalogId ?? 0) - (b.catalogId ?? 0)

  return {
    in: Array.from(added.values()).sort(byName),
    out: Array.from(removed.values()).sort(byName),
  }
}
