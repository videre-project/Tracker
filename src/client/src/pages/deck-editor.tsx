/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useCallback, useEffect, useMemo, useState } from "react"
import { createPortal } from "react-dom"
import { useLocation, useNavigate, useParams } from "react-router-dom"
import { Loader2, Pencil } from "lucide-react"
import {
  DeckEditorLayout,
  getDisplayCardColors,
  getManaSymbolSvgPath,
  type DeckCardSearchResult,
  type DeckEditorCard,
  type DeckSidePanelView,
  type DeckSortMode,
} from "@videreproject/ui"

import { useDeckHistory } from "@/hooks/use-deck-history"
import { useDeckCardSearch } from "@/hooks/use-deck-card-search"
import { useSortableCards } from "@/hooks/use-sortable-cards"
import { buildDeckListText, getDeckFileName } from "@/utils/deck-list"
import type { DeckDetail, DeckSummary } from "@/hooks/use-decks"
import {
  useDeckDetail,
  useDecks,
  updateDeckArchetype,
} from "@/hooks/use-decks"

type DeckRouteState = {
  deckName?: string
  deckFormat?: string
  deckColors?: string[]
  deckArchetype?: string
  deckTimestamp?: string
  deckMainCount?: number
  deckSideCount?: number
}

function getDeckCounts(
  summary?: DeckSummary,
  detail?: DeckDetail | null,
  routeState?: DeckRouteState | null,
) {
  if (summary) {
    return {
      main: summary.mainboardCount,
      side: summary.sideboardCount,
    }
  }

  if (detail) {
    return {
      main: detail.mainboard.reduce((total, card) => total + card.quantity, 0),
      side: detail.sideboard.reduce((total, card) => total + card.quantity, 0),
    }
  }

  if (routeState?.deckMainCount !== undefined) {
    return {
      main: routeState.deckMainCount,
      side: routeState.deckSideCount ?? 0,
    }
  }

  return {
    main: 0,
    side: 0,
  }
}

function BreadcrumbManaSymbols({ colors }: { colors: readonly string[] }) {
  const visibleColors = getDisplayCardColors(colors)

  return (
    <span className="inline-flex h-4 translate-y-[2px] items-center gap-0.5 leading-none">
      {visibleColors.map((color, index) => (
        <img
          key={`${color}-${index}`}
          src={getManaSymbolSvgPath(color) ?? undefined}
          alt={color}
          className="block h-3.5 w-3.5 rounded-full bg-background shadow-sm ring-1 ring-background"
        />
      ))}
    </span>
  )
}

export default function DeckEditor() {
  const { deckRevisionId = "" } = useParams()
  const navigate = useNavigate()
  const location = useLocation()
  const routeState = location.state as DeckRouteState | null
  const [copiedDeckList, setCopiedDeckList] = useState(false)
  const [sidePanelView, setSidePanelView] = useState<DeckSidePanelView>("cards")
  const [sortMode, setSortMode] = useState<DeckSortMode>("cmc")
  const [isSideboardCollapsed, setIsSideboardCollapsed] = useState(true)
  const [isDeckToolsCollapsed, setIsDeckToolsCollapsed] = useState(false)
  const [customArchetype, setCustomArchetype] = useState<string | null>(null)
  const [archetypeError, setArchetypeError] = useState<string | null>(null)
  const [isSavingArchetype, setIsSavingArchetype] = useState(false)
  const [searchQuery, setSearchQuery] = useState("")

  const { decks, loading: summariesLoading } = useDecks()
  const { detail, loading: detailLoading } = useDeckDetail(deckRevisionId)

  const {
    history: historyData,
    loading: historyLoading,
    error: historyError,
    selectedRevisionId,
    setSelectedRevisionId,
    selectedRevisionCards,
    diffMap,
  } = useDeckHistory(deckRevisionId)

  const {
    cards: sortableCards,
    loading: sortableLoading,
    fetchSortableCards,
    reset: resetSortable,
  } = useSortableCards()

  const {
    results: searchResults,
    loading: searchLoading,
    error: searchError,
  } = useDeckCardSearch(searchQuery)

  // Fetch sortable cards for the active (or selected history) revision
  const activeRevisionId = selectedRevisionId
    ? String(selectedRevisionId)
    : deckRevisionId

  useEffect(() => {
    if (!activeRevisionId) {
      resetSortable()
      return
    }
    // When viewing a history revision, board uses override cards from history
    if (selectedRevisionId != null) return
    void fetchSortableCards("", activeRevisionId)
  }, [activeRevisionId, fetchSortableCards, resetSortable, selectedRevisionId])

  const allDecks = useMemo(() => Object.values(decks).flat(), [decks])
  const summary = useMemo(
    () => allDecks.find(deck => deck.revisionId.toString() === deckRevisionId),
    [allDecks, deckRevisionId],
  )

  const deckName = summary?.name ?? detail?.name ?? routeState?.deckName ?? "Deck"
  const rawArchetype =
    customArchetype ??
    summary?.archetype ??
    detail?.archetype ??
    routeState?.deckArchetype

  const handleArchetypeChange = useCallback(
    async (nextArchetype: string) => {
      if (!deckRevisionId) return
      setIsSavingArchetype(true)
      setArchetypeError(null)
      try {
        await updateDeckArchetype(deckRevisionId, nextArchetype)
        setCustomArchetype(nextArchetype || null)
      } catch (err) {
        const message = err instanceof Error ? err.message : "Failed to update archetype"
        setArchetypeError(message)
        throw err
      } finally {
        setIsSavingArchetype(false)
      }
    },
    [deckRevisionId],
  )

  const colors = getDisplayCardColors(
    summary?.colors?.length
      ? summary.colors
      : detail?.colors?.length
        ? detail.colors
        : routeState?.deckColors,
  )
  const timestamp = summary?.timestamp ?? detail?.timestamp ?? routeState?.deckTimestamp
  const counts = getDeckCounts(summary, detail, routeState)
  const deckListText = useMemo(() => buildDeckListText(detail), [detail])
  const canExportDeckList = Boolean(detail)
  const hasHeaderData = Boolean(summary || detail || routeState?.deckName)
  const isArchetypeLoading =
    rawArchetype === undefined && (summariesLoading || detailLoading)
  const isDateLoading = timestamp === undefined && (summariesLoading || detailLoading)
  const isCountsLoading =
    !summary &&
    !detail &&
    routeState?.deckMainCount === undefined &&
    (summariesLoading || detailLoading)
  const loadingHeader =
    ((summariesLoading || detailLoading) && !hasHeaderData) ||
    isArchetypeLoading ||
    isDateLoading ||
    isCountsLoading
  const archetype = rawArchetype ?? (loadingHeader ? "" : "Unclassified deck")
  const canNavigateBack = location.key !== "default"

  const navigateBack = useCallback(() => {
    if (canNavigateBack) {
      navigate(-1)
      return
    }
    navigate("/decks")
  }, [canNavigateBack, navigate])

  const copyDeckList = useCallback(async () => {
    if (!canExportDeckList) return

    try {
      await navigator.clipboard.writeText(deckListText)
    } catch {
      const textarea = document.createElement("textarea")
      textarea.value = deckListText
      textarea.setAttribute("readonly", "")
      textarea.style.position = "fixed"
      textarea.style.opacity = "0"
      document.body.append(textarea)
      textarea.select()
      document.execCommand("copy")
      textarea.remove()
    }

    setCopiedDeckList(true)
    window.setTimeout(() => setCopiedDeckList(false), 1600)
  }, [canExportDeckList, deckListText])

  const downloadDeckList = useCallback(() => {
    if (!canExportDeckList) return

    const blob = new Blob([deckListText], { type: "text/plain;charset=utf-8" })
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement("a")
    anchor.href = url
    anchor.download = getDeckFileName(deckName)
    document.body.append(anchor)
    anchor.click()
    anchor.remove()
    URL.revokeObjectURL(url)
  }, [canExportDeckList, deckListText, deckName])

  // Prefer history revision cards when selected; otherwise live sortable fetch
  const boardCards: DeckEditorCard[] = useMemo(() => {
    if (selectedRevisionId != null && selectedRevisionCards.length > 0) {
      return selectedRevisionCards as DeckEditorCard[]
    }
    return sortableCards as DeckEditorCard[]
  }, [selectedRevisionCards, selectedRevisionId, sortableCards])

  const cardsLoading =
    selectedRevisionId != null
      ? historyLoading
      : sortableLoading || (detailLoading && boardCards.length === 0)

  const mappedSearchResults: DeckCardSearchResult[] = useMemo(
    () =>
      searchResults.map(card => ({
        id: card.id,
        mtgoId: card.mtgoId,
        setCode: card.setCode,
        name: card.name,
        type: card.type,
        text: card.text,
        colors: card.colors,
        imageUrl: card.imageUrl,
        power: card.power,
        toughness: card.toughness,
        loyalty: card.loyalty,
        defense: card.defense,
      })),
    [searchResults],
  )

  const breadcrumbContextHost =
    typeof document === "undefined" ? null : document.getElementById("page-header-context")

  const breadcrumb =
    loadingHeader ? (
      <Loader2 className="h-3.5 w-3.5 animate-spin text-muted-foreground" aria-label="Loading deck" />
    ) : (
      <div className="group inline-flex items-center gap-2.5">
        <BreadcrumbManaSymbols colors={colors} />
        <button
          type="button"
          className="h-4 w-4 p-0 text-muted-foreground/40 transition-colors group-hover:text-muted-foreground"
          title="Edit deck name and colors"
          aria-label="Edit deck name and colors"
        >
          <Pencil className="h-3.5 w-3.5 shrink-0 translate-y-px" />
        </button>
      </div>
    )

  return (
    <>
      {breadcrumbContextHost ? createPortal(breadcrumb, breadcrumbContextHost) : null}
      <DeckEditorLayout
        className="h-[calc(100vh-2.5rem)]"
        deckName={deckName}
        archetype={archetype}
        colors={colors}
        timestamp={timestamp}
        mainCount={counts.main}
        sideCount={counts.side}
        loadingHeader={loadingHeader}
        cards={boardCards}
        cardsLoading={cardsLoading}
        diffMap={diffMap}
        sortMode={sortMode}
        onSortModeChange={setSortMode}
        sideboardCollapsed={isSideboardCollapsed}
        onSideboardCollapsedChange={setIsSideboardCollapsed}
        toolsCollapsed={isDeckToolsCollapsed}
        onToolsCollapsedChange={setIsDeckToolsCollapsed}
        sidePanelView={sidePanelView}
        onSidePanelViewChange={setSidePanelView}
        historyData={historyData}
        historyLoading={historyLoading}
        historyError={historyError}
        selectedRevisionId={selectedRevisionId}
        onSelectRevision={setSelectedRevisionId}
        searchResults={mappedSearchResults}
        searchLoading={searchLoading}
        searchError={searchError}
        onSearchQueryChange={setSearchQuery}
        onBack={navigateBack}
        onCopyList={copyDeckList}
        onExportList={downloadDeckList}
        canExport={canExportDeckList}
        copiedList={copiedDeckList}
        importDisabled
        onArchetypeChange={handleArchetypeChange}
        archetypeSaving={isSavingArchetype}
        archetypeError={archetypeError}
      />
    </>
  )
}
