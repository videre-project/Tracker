/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { Loader2, LockKeyhole } from "lucide-react"
import { useEffect, useMemo, useState } from "react"
import { DeckEditorVignette, type DeckCardEntry } from "@videreproject/ui"
import { buildDeckListText, getDeckFileName, toEditorCards } from "@/utils/deck-list"

export interface ExternalDeck {
  name: string
  archetype?: string
  colors?: string[]
  mainboard: DeckCardEntry[]
  sideboard: DeckCardEntry[]
  detailsLoaded: boolean
}

interface ExternalDeckPreviewProps {
  deck: ExternalDeck | null
  timestamp?: string
  importing: boolean
  importError: string | null
  detailsError?: string | null
  onRetry?: () => void
  onOpenChange: (open: boolean) => void
  onClose: () => void
  onImport: () => void
}

export function ExternalDeckPreview({
  deck,
  timestamp,
  importing,
  importError,
  detailsError,
  onRetry,
  onOpenChange,
  onClose,
  onImport,
}: ExternalDeckPreviewProps) {
  const cards = useMemo(() => deck ? toEditorCards(deck) : [], [deck])
  const [copied, setCopied] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)
  useEffect(() => {
    setCopied(false)
    setActionError(null)
  }, [deck])
  useEffect(() => {
    if (!copied) return
    const timeout = window.setTimeout(() => setCopied(false), 1600)
    return () => window.clearTimeout(timeout)
  }, [copied])

  const copyList = async () => {
    if (!deck?.detailsLoaded) return
    setActionError(null)
    const text = buildDeckListText(deck)
    try {
      if (navigator.clipboard) {
        await navigator.clipboard.writeText(text)
      } else {
        const textarea = document.createElement("textarea")
        textarea.value = text
        textarea.style.position = "fixed"
        textarea.style.opacity = "0"
        document.body.append(textarea)
        try {
          textarea.select()
          if (!document.execCommand("copy")) throw new Error("Could not copy this deck.")
        } finally {
          textarea.remove()
        }
      }
      setCopied(true)
    } catch {
      setActionError("Could not copy this deck. You can export it instead.")
    }
  }

  const exportList = () => {
    if (!deck?.detailsLoaded) return
    const url = URL.createObjectURL(new Blob([buildDeckListText(deck)], { type: "text/plain;charset=utf-8" }))
    const anchor = document.createElement("a")
    anchor.href = url
    anchor.download = getDeckFileName(deck.name)
    document.body.append(anchor)
    anchor.click()
    anchor.remove()
    URL.revokeObjectURL(url)
  }

  return (
    <DeckEditorVignette
      open={deck != null}
      onOpenChange={onOpenChange}
      deckName={deck?.name ?? "Deck"}
      archetype={deck?.archetype}
      colors={deck?.colors}
      timestamp={timestamp}
      mainCount={deck?.mainboard.reduce((sum, card) => sum + card.quantity, 0)}
      sideCount={deck?.sideboard.reduce((sum, card) => sum + card.quantity, 0)}
      cards={cards}
      cardsLoading={deck != null && !deck.detailsLoaded && !detailsError}
      onClose={onClose}
      onCopyList={() => void copyList()}
      onExportList={exportList}
      canExport={Boolean(deck?.detailsLoaded)}
      copiedList={copied}
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
      errorOverlay={importError || actionError || detailsError ? (
        <div role="alert" className="absolute bottom-4 left-1/2 z-[70] max-w-lg -translate-x-1/2 rounded-md border border-destructive/30 bg-destructive/95 px-4 py-3 text-sm text-destructive-foreground shadow-lg">
          {importError || actionError || detailsError}
          {detailsError && onRetry && (
            <button type="button" className="ml-3 underline underline-offset-2" onClick={onRetry}>Retry</button>
          )}
        </div>
      ) : null}
    />
  )
}
