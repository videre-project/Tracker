/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useEffect, useMemo, useState } from "react"
import { useNavigate } from "react-router-dom"
import { CARD_COLORS } from "@videreproject/constants"
import type { TournamentPlayerDetails } from "@videreproject/ui"
import { importDeck } from "@/hooks/use-decks"
import { ExternalDeckPreview } from "./external-deck-preview"

export function TournamentDeckPreview({ deck, format, timestamp, onClose }: {
  deck: TournamentPlayerDetails["deck"]
  format: string
  timestamp?: string
  onClose: () => void
}) {
  const navigate = useNavigate()
  const [importing, setImporting] = useState(false)
  const [importError, setImportError] = useState<string | null>(null)
  useEffect(() => setImportError(null), [deck])

  const preview = useMemo(() => {
    if (!deck) return null
    const convert = (cards: typeof deck.mainboard) => cards.map(card => ({
      catalogId: card.id, name: card.name, quantity: card.quantity,
      cmc: card.manaValue ?? 0, colors: card.colors ?? [],
      types: card.typeLine?.split("—")[0].trim().split(/\s+/).filter(Boolean) ?? [],
      imageUrl: card.imageUrl,
    }))
    return {
      name: deck.name || deck.archetype || "Event deck",
      archetype: deck.archetype ?? undefined,
      colors: CARD_COLORS.map(color => color.symbol).filter(color =>
        [...deck.mainboard, ...deck.sideboard].some(card => card.colors?.includes(color))),
      mainboard: convert(deck.mainboard), sideboard: convert(deck.sideboard),
      detailsLoaded: true,
    }
  }, [deck])

  const importSelectedDeck = async () => {
    if (!preview || importing) return
    setImporting(true)
    setImportError(null)
    try {
      const result = await importDeck({
        name: preview.name, format, archetype: preview.archetype,
        mainboard: preview.mainboard, sideboard: preview.sideboard,
      })
      onClose()
      navigate(`/decks/${result.revisionId}`, {
        state: {
          deckName: preview.name, deckFormat: format, deckColors: preview.colors,
          deckArchetype: preview.archetype, deckTimestamp: new Date().toISOString(),
          deckMainCount: preview.mainboard.reduce((sum, card) => sum + card.quantity, 0),
          deckSideCount: preview.sideboard.reduce((sum, card) => sum + card.quantity, 0),
        },
      })
    } catch (reason) {
      setImportError(reason instanceof Error ? reason.message : "Could not import this deck.")
    } finally {
      setImporting(false)
    }
  }

  return <ExternalDeckPreview deck={preview} timestamp={timestamp}
    importing={importing} importError={importError}
    onImport={() => void importSelectedDeck()}
    onOpenChange={open => { if (!open && !importing) onClose() }}
    onClose={() => { if (!importing) onClose() }} />
}
