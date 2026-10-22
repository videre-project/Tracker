/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useMemo, type ComponentProps } from "react"
import type { DateRange } from "react-day-picker"
import type { MetagameDeckItem } from "@/hooks/use-metagame-decks"
import { ExternalDeckPreview } from "./external-deck-preview"

type MetagameDeckPreviewProps = Omit<ComponentProps<typeof ExternalDeckPreview>, "deck" | "timestamp"> & {
  deck: MetagameDeckItem | null
  dateRange?: DateRange
}

export function MetagameDeckPreview({ deck, dateRange, ...props }: MetagameDeckPreviewProps) {
  const preview = useMemo(() => deck ? { ...deck, archetype: deck.name } : null, [deck])
  return <ExternalDeckPreview {...props}
    deck={preview}
    timestamp={dateRange?.to?.toISOString()} />
}
