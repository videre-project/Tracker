/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { Loader2, LockKeyhole } from "lucide-react"
import type { DateRange } from "react-day-picker"
import { DeckEditorVignette } from "@videreproject/ui"
import type { MetagameDeckItem } from "@/hooks/use-metagame-decks"

interface MetagameDeckPreviewProps {
  deck: MetagameDeckItem | null
  dateRange?: DateRange
  importing: boolean
  importError: string | null
  onOpenChange: (open: boolean) => void
  onClose: () => void
  onImport: () => void
}

function toEditorCards(deck: MetagameDeckItem) {
  return [...deck.mainboard.map(card => ({ card, zone: "Mainboard" as const })),
    ...deck.sideboard.map(card => ({ card, zone: "Sideboard" as const }))]
    .map(({ card, zone }, index) => ({
      index,
      originalIndex: index,
      catalogId: card.catalogId,
      name: card.name,
      quantity: card.quantity,
      cmc: card.cmc ?? 0,
      colors: card.colors ?? [],
      types: card.types ?? [],
      rarity: card.rarity ?? "common",
      zone,
      imageUrl: card.imageUrl,
    }))
}

export function MetagameDeckPreview({
  deck,
  dateRange,
  importing,
  importError,
  onOpenChange,
  onClose,
  onImport,
}: MetagameDeckPreviewProps) {
  return (
    <DeckEditorVignette
      open={deck != null}
      onOpenChange={onOpenChange}
      deckName={deck?.name ?? "Metagame deck"}
      archetype={deck?.name}
      colors={deck?.colors}
      timestamp={dateRange?.to?.toISOString()}
      mainCount={deck?.mainboardCount}
      sideCount={deck?.sideboardCount}
      cards={deck ? toEditorCards(deck) : []}
      cardsLoading={deck != null && !deck.detailsLoaded}
      onClose={onClose}
      onImport={onImport}
      importDisabled={importing || !deck?.detailsLoaded}
      sidePanelLockedContent={(
        <div className="flex max-w-56 flex-col items-center gap-3">
          <span className="flex h-11 w-11 items-center justify-center rounded-full bg-secondary text-muted-foreground">
            <LockKeyhole className="h-5 w-5" />
          </span>
          <div>
            <p className="text-sm font-medium text-foreground">Import to build with this deck</p>
            <p className="mt-1 text-xs leading-5 text-muted-foreground">
              Save this deck to Tracker to unlock card search, history, and editing features.
            </p>
          </div>
        </div>
      )}
      loadingOverlay={importing ? (
        <div className="pointer-events-none absolute inset-0 z-[60] flex items-center justify-center bg-background/35 backdrop-blur-[1px]">
          <div className="flex items-center gap-2 rounded-md border border-sidebar-border/70 bg-background px-4 py-3 text-sm shadow-lg">
            <Loader2 className="h-4 w-4 animate-spin" />
            Importing deck…
          </div>
        </div>
      ) : null}
      errorOverlay={importError ? (
        <div className="absolute bottom-4 left-1/2 z-[70] max-w-lg -translate-x-1/2 rounded-md border border-destructive/30 bg-destructive/95 px-4 py-3 text-sm text-destructive-foreground shadow-lg">
          {importError}
        </div>
      ) : null}
    />
  )
}
