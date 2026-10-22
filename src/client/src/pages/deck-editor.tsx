/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useApplicationSettings } from "@/hooks/use-application-settings"
import { type ComponentType, useCallback, useEffect, useMemo, useRef, useState } from "react"
import { createPortal } from "react-dom"
import { useLocation, useNavigate, useOutletContext, useParams } from "react-router-dom"
import { Loader2, Minus, Pencil, Plus, Trash2 } from "lucide-react"
import {
  DeckEditorLayout,
  getDisplayCardColors,
  type DeckCardSearchResult,
  type DeckContextMenuPosition,
  type DeckCardDragEndPayload,
  type DeckEditorCard,
  type DeckSidePanelView,
  type DeckSortMode,
  type DeckZone,
} from "@videreproject/ui"

import { useDeckHistory } from "@/hooks/use-deck-history"
import { useDeckCardSearch } from "@/hooks/use-deck-card-search"
import { DeckStatisticsSidebar } from "@/components/deck-statistics"
import { buildDeckListText, getDeckFileName, toEditorCards } from "@/utils/deck-list"
import type { DeckDetail } from "@/hooks/use-decks"
import {
  addDeckCard,
  deleteDeck,
  removeDeckCard,
  moveDeckCards,
  useDeckDetail,
  updateDeckArchetype,
  type DeckCardMutation,
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

type CardContextMenu =
  | { kind: "search"; card: DeckCardSearchResult; position: DeckContextMenuPosition }
  | { kind: "deck"; card: DeckEditorCard; position: DeckContextMenuPosition }

function getDeckCounts(
  detail?: DeckDetail | null,
  routeState?: DeckRouteState | null,
) {
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

function isBasicCard(card: DeckEditorCard | DeckCardSearchResult) {
  return card.types?.includes("Basic") ?? false
}

function CardMenuQuantityRow({
  icon: Icon,
  label,
  quantities,
  maxQuantity,
  destructive = false,
  onQuantity,
}: {
  icon: ComponentType<{ className?: string }>
  label: string
  quantities: readonly number[]
  maxQuantity?: number
  destructive?: boolean
  onQuantity: (quantity: number) => void
}) {
  // Destructive uses a brighter red in dark mode: the theme's --destructive is
  // a dark red there, which reads as disabled on the dark popover.
  return (
    <div
      className={
        destructive
          ? "flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-xs text-destructive dark:text-red-400"
          : "flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-xs"
      }
    >
      <span className="flex w-28 shrink-0 items-center gap-2">
        <Icon
          className={
            destructive
              ? "h-3.5 w-3.5 shrink-0"
              : "h-3.5 w-3.5 shrink-0 text-muted-foreground"
          }
        />
        <span className="truncate">{label}</span>
      </span>
      <span className="flex shrink-0 items-center gap-1">
        {quantities.map(quantity => (
          <button
            key={quantity}
            type="button"
            role="menuitem"
            aria-label={`${label} ${quantity}`}
            disabled={maxQuantity != null && quantity > maxQuantity}
            onClick={() => onQuantity(quantity)}
            className={
              destructive
                ? "h-7 w-7 rounded border border-destructive/40 text-sm font-medium tabular-nums transition-colors hover:bg-destructive/15 disabled:cursor-not-allowed disabled:opacity-30 dark:border-red-400/40 dark:hover:bg-red-400/15"
                : "h-7 w-7 rounded border border-sidebar-border/60 bg-background/60 text-sm font-medium tabular-nums transition-colors hover:bg-accent disabled:cursor-not-allowed disabled:opacity-30"
            }
          >
            {quantity}
          </button>
        ))}
      </span>
    </div>
  )
}

export default function DeckEditor() {
  const { preferences } = useApplicationSettings()
  const { deckRevisionId = "" } = useParams()
  const activeRevisionRef = useRef(deckRevisionId)
  activeRevisionRef.current = deckRevisionId
  const navigate = useNavigate()
  const location = useLocation()
  const routeState = location.state as DeckRouteState | null
  const [copiedDeckList, setCopiedDeckList] = useState(false)
  const [sidePanelView, setSidePanelView] = useState<DeckSidePanelView>(() => new URLSearchParams(location.search).get("panel") === "stats" ? "stats" : "cards")
  const [sortMode, setSortMode] = useState<DeckSortMode>(preferences.deckSortMode)
  const [isSideboardCollapsed, setIsSideboardCollapsed] = useState(!preferences.showSideboard)
  const [customArchetype, setCustomArchetype] = useState<string | null>(null)
  const [archetypeError, setArchetypeError] = useState<string | null>(null)
  const [isSavingArchetype, setIsSavingArchetype] = useState(false)
  const [searchQuery, setSearchQuery] = useState("")
  const [emphasizedCardName, setEmphasizedCardName] = useState<string | null>(null)
  const [hoveredDeckCardName, setHoveredDeckCardName] = useState<string | null>(null)
  const [cardOrder, setCardOrder] = useState<Partial<Record<DeckZone, readonly string[]>>>({})
  const handleCardOrderChange = useCallback((zone: DeckZone, names: readonly string[]) => {
    setCardOrder(current => current[zone]?.length === names.length && current[zone]?.every((name, index) => name === names[index])
      ? current : { ...current, [zone]: names })
  }, [])
  const [cardMenu, setCardMenu] = useState<CardContextMenu | null>(null)
  const cardMenuRef = useRef<HTMLDivElement | null>(null)
  const backdropRef = useRef<HTMLDivElement | null>(null)
  const [mutationError, setMutationError] = useState<string | null>(null)
  const [isMutating, setIsMutating] = useState(false)
  const [pendingMove, setPendingMove] = useState<{
    cards: DeckEditorCard[]
    revisionId: number | null
    sourceRevisionId: string
  } | null>(null)

  const visiblePendingMove = pendingMove &&
    (pendingMove.sourceRevisionId === deckRevisionId || pendingMove.revisionId?.toString() === deckRevisionId)
      ? pendingMove
      : null

  const { detail, loading: detailLoading, error: detailError } = useOutletContext<ReturnType<typeof useDeckDetail>>()

  const [historyDeck, setHistoryDeck] = useState<string | null>(null)
  useEffect(() => {
    if (sidePanelView === "history") setHistoryDeck(deckRevisionId)
  }, [sidePanelView, deckRevisionId])
  const {
    history: historyData,
    loading: historyLoading,
    error: historyError,
    selectedRevisionId,
    setSelectedRevisionId,
    selectedRevisionCards,
    diffMap,
  } = useDeckHistory(sidePanelView === "history" || historyDeck === deckRevisionId ? deckRevisionId : null)
  const mutationRevisionId = detail?.currentRevisionId
  const canEdit = mutationRevisionId === detail?.revisionId && detail != null && !detailLoading && selectedRevisionId == null && !isMutating

  useEffect(() => {
    if (pendingMove && !visiblePendingMove) {
      setPendingMove(null)
      setIsMutating(false)
      return
    }
    if (pendingMove?.revisionId == null) return
    if ((detail?.revisionId ?? 0) < pendingMove.revisionId && !detailError) return
    setPendingMove(null)
    setIsMutating(false)
  }, [pendingMove, visiblePendingMove, detail?.revisionId, detailError])

  const {
    results: searchResults,
    loading: searchLoading,
    error: searchError,
  } = useDeckCardSearch(searchQuery)

  useEffect(() => {
    setCardMenu(null)
    setCustomArchetype(null)
    setArchetypeError(null)
    setIsSavingArchetype(false)
    setMutationError(null)
    setIsMutating(false)
  }, [deckRevisionId])

  const deckName = detail?.name ?? routeState?.deckName ?? "Deck"
  const rawArchetype = customArchetype ?? detail?.archetype ?? routeState?.deckArchetype

  const handleArchetypeChange = useCallback(
    async (nextArchetype: string) => {
      if (!deckRevisionId) return
      setIsSavingArchetype(true)
      setArchetypeError(null)
      try {
        await updateDeckArchetype(deckRevisionId, nextArchetype)
        if (activeRevisionRef.current !== deckRevisionId) return
        setCustomArchetype(nextArchetype || null)
      } catch (err) {
        if (activeRevisionRef.current !== deckRevisionId) return
        const message = err instanceof Error ? err.message : "Failed to update archetype"
        setArchetypeError(message)
        throw err
      } finally {
        if (activeRevisionRef.current === deckRevisionId) setIsSavingArchetype(false)
      }
    },
    [deckRevisionId],
  )

  const colors = getDisplayCardColors(detail?.colors?.length ? detail.colors : routeState?.deckColors)
  const timestamp = detail?.timestamp ?? routeState?.deckTimestamp
  const counts = getDeckCounts(detail, routeState)
  const deckListText = useMemo(() => buildDeckListText(detail), [detail])
  const canExportDeckList = Boolean(detail)
  const loadingHeader = detailLoading && !detail && !routeState?.deckName
  const archetype = rawArchetype ?? (loadingHeader ? "" : "Unclassified deck")
  const deckFormat = detail?.format ?? routeState?.deckFormat

  useEffect(() => {
    if (historyData && detail && detail.currentRevisionId !== detail.revisionId && selectedRevisionId == null)
      setSelectedRevisionId(detail.revisionId)
  }, [historyData, detail, selectedRevisionId, setSelectedRevisionId])

  useEffect(() => {
    if (!cardMenu) return
    // Non-pointer closes (keyboard, blur, resize).
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setCardMenu(null)
    }
    const closeOnBlur = () => setCardMenu(null)
    const closeOnResize = () => setCardMenu(null)
    window.addEventListener("blur", closeOnBlur)
    window.addEventListener("keydown", onKeyDown)
    window.addEventListener("resize", closeOnResize)

    // Pointer-close via a NATIVE listener on the backdrop element. React
    // synthetic handlers on the backdrop do not reliably fire in the WebView2
    // host, but a native listener on the element does (same mechanism as the
    // tooltip's display:contents wrapper). We react to the earliest pointer
    // events (pointerdown / pointerup) so the menu (and its highlight)
    // dismisses as soon as the pointer moves, not ~200ms later on `click`.
    const backdrop = backdropRef.current
    const closeOnPress = (event: Event) => {
      // Chromium opens the menu on right-button down; its opening release is not a dismissal.
      if (event.type === "pointerup" && (event as PointerEvent).button === 2) return
      setCardMenu(null)
    }
    backdrop?.addEventListener("pointerdown", closeOnPress, { capture: true })
    backdrop?.addEventListener("pointerup", closeOnPress, { capture: true })
    backdrop?.addEventListener("mousedown", closeOnPress, { capture: true })

    return () => {
      window.removeEventListener("blur", closeOnBlur)
      window.removeEventListener("keydown", onKeyDown)
      window.removeEventListener("resize", closeOnResize)
      backdrop?.removeEventListener("pointerdown", closeOnPress, { capture: true })
      backdrop?.removeEventListener("pointerup", closeOnPress, { capture: true })
      backdrop?.removeEventListener("mousedown", closeOnPress, { capture: true })
    }
  }, [cardMenu])

  const completeMutation = useCallback((revisionId: number) => {
    if (activeRevisionRef.current !== deckRevisionId) return
    setSelectedRevisionId(null)
    setCardMenu(null)
    navigate(`/decks/${revisionId}`, { replace: true })
  }, [deckRevisionId, navigate, setSelectedRevisionId])

  const addCard = useCallback(async (mutation: DeckCardMutation) => {
    if (!canEdit || mutationRevisionId == null) return
    setIsMutating(true)
    setMutationError(null)
    try {
      const result = await addDeckCard(mutationRevisionId, mutation)
      completeMutation(result.revisionId)
    } catch (error) {
      if (activeRevisionRef.current === deckRevisionId) {
        setMutationError(error instanceof Error ? error.message : "Failed to add card")
      }
    } finally {
      if (activeRevisionRef.current === deckRevisionId) setIsMutating(false)
    }
  }, [completeMutation, deckRevisionId, mutationRevisionId, canEdit])

  const removeCard = useCallback(async (mutation: DeckCardMutation) => {
    if (!canEdit || mutationRevisionId == null) return
    setIsMutating(true)
    setMutationError(null)
    try {
      const result = await removeDeckCard(mutationRevisionId, mutation)
      completeMutation(result.revisionId)
    } catch (error) {
      if (activeRevisionRef.current === deckRevisionId) {
        setMutationError(error instanceof Error ? error.message : "Failed to remove card")
      }
    } finally {
      if (activeRevisionRef.current === deckRevisionId) setIsMutating(false)
    }
  }, [completeMutation, deckRevisionId, mutationRevisionId, canEdit])

  const searchCardMutation = useCallback(
    (card: DeckCardSearchResult, zone: DeckCardMutation["zone"], quantity: number) => {
      const metadata = searchResults.find(result => result.mtgoId === card.mtgoId)
      return {
        catalogId: card.mtgoId,
        name: card.name,
        zone,
        quantity,
        cmc: metadata?.cmc,
        rarity: metadata?.rarity,
        colors: card.colors ?? [],
        types: card.types ?? [],
      }
    },
    [searchResults],
  )

  const deckCardMutation = useCallback(
    (card: DeckEditorCard, zone: DeckCardMutation["zone"], quantity: number) => ({
      catalogId: card.catalogId,
      name: card.name,
      zone,
      quantity,
      cmc: card.cmc,
      colors: card.colors,
      types: card.types,
      rarity: card.rarity,
    }),
    [],
  )

  const handleDeleteDeck = useCallback(async () => {
    if (!canEdit || mutationRevisionId == null) return
    if (!window.confirm(`Delete “${deckName}” from Tracker and MTGO? This cannot be undone.`)) {
      return
    }
    setIsMutating(true)
    setMutationError(null)
    try {
      await deleteDeck(mutationRevisionId)
      if (activeRevisionRef.current !== deckRevisionId) return
      navigate("/decks", { replace: true })
    } catch (error) {
      if (activeRevisionRef.current === deckRevisionId) {
        setMutationError(error instanceof Error ? error.message : "Failed to delete deck")
      }
    } finally {
      if (activeRevisionRef.current === deckRevisionId) setIsMutating(false)
    }
  }, [deckName, deckRevisionId, mutationRevisionId, canEdit, navigate])

  const navigateBack = useCallback(() => {
    navigate(deckFormat?.trim()
      ? `/decks?format=${encodeURIComponent(deckFormat.trim())}`
      : "/decks")
  }, [deckFormat, navigate])

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

  const boardCards = useMemo(() => selectedRevisionId != null
    ? selectedRevisionCards : toEditorCards(detail), [selectedRevisionId, selectedRevisionCards, detail])

  const handleDragEnd = useCallback(
    async (payload: DeckCardDragEndPayload) => {
      const destinationZone = payload.toZone
      if (
        !destinationZone ||
        destinationZone === payload.zone ||
        !canEdit ||
        mutationRevisionId == null
      ) {
        return
      }

      const quantities = new Map<number, number>()
      for (const movedCard of payload.movedCards) {
        quantities.set(
          movedCard.catalogId,
          (quantities.get(movedCard.catalogId) ?? 0) + 1,
        )
      }

      setIsMutating(true)
      setMutationError(null)
      try {
        const nextCards = boardCards.map(card => ({ ...card }))
        for (const [catalogId, quantity] of quantities) {
          const source = nextCards.find(card => card.catalogId === catalogId && card.zone === payload.zone)
          if (!source || source.quantity < quantity) {
            throw new Error("The source zone no longer contains the selected cards.")
          }
          source.quantity -= quantity
          const target = nextCards.find(card => card.catalogId === catalogId && card.zone === destinationZone)
          if (target) target.quantity += quantity
          else nextCards.push({ ...source, zone: destinationZone, quantity })
        }
        const movedCards = nextCards.filter(card => card.quantity > 0)
          .map((card, index) => ({ ...card, index, originalIndex: index }))
        setPendingMove({ cards: movedCards, revisionId: null, sourceRevisionId: deckRevisionId })
        const result = await moveDeckCards(
          mutationRevisionId,
          payload.zone,
          destinationZone,
          [...quantities].map(([catalogId, quantity]) => ({ catalogId, quantity })),
        )
        if (activeRevisionRef.current !== deckRevisionId) return
        setPendingMove({ cards: movedCards, revisionId: result.revisionId, sourceRevisionId: deckRevisionId })
        completeMutation(result.revisionId)
      } catch (error) {
        if (activeRevisionRef.current !== deckRevisionId) return
        setPendingMove(null)
        setIsMutating(false)
        setMutationError(error instanceof Error ? error.message : "Failed to move card")
        throw error
      }
    },
    [
      boardCards,
      completeMutation,
      deckRevisionId,
      mutationRevisionId,
      canEdit,
    ],
  )

  const cardsLoading = selectedRevisionId != null ? historyLoading : detailLoading && !detail

  // Total copies of the right-clicked card in its zone (each row carries a
  // quantity field, so sum it rather than counting rows). Caps the remove
  // quantity: can't remove more than the deck holds.
  const removeMaxQuantity =
    cardMenu?.kind === "deck"
      ? boardCards
          .filter(
            card =>
              card.catalogId === cardMenu.card.catalogId &&
              card.zone === cardMenu.card.zone,
          )
          .reduce((total, card) => total + card.quantity, 0)
      : undefined

  // Total copies of a card already in a zone (sum the quantity field),
  // used to cap adds at 4 total.
  const countInZone = (catalogId: number, zone: DeckCardMutation["zone"]) =>
    boardCards
      .filter(card => card.catalogId === catalogId && card.zone === zone)
      .reduce((total, card) => total + card.quantity, 0)

  // Cap add quantities: non-basic cards are limited to 4 total copies across
  // the whole deck (main + side combined); basic lands are uncapped.
  // Undefined means no cap (all buttons enabled).
  const activeMenuCard = cardMenu?.card
  const activeCatalogId =
    activeMenuCard == null
      ? undefined
      : "catalogId" in activeMenuCard
        ? activeMenuCard.catalogId
        : activeMenuCard.mtgoId
  const activeIsBasic = activeMenuCard ? isBasicCard(activeMenuCard) : false
  const addMaxQuantity =
    activeCatalogId != null && !activeIsBasic
      ? Math.max(
          0,
          4 -
            countInZone(activeCatalogId, "Mainboard") -
            countInZone(activeCatalogId, "Sideboard"),
        )
      : undefined

  // Only offer remove quantities the deck actually holds (no greyed-out buttons).
  const removeQuantities =
    removeMaxQuantity == null
      ? [1, 2, 3, 4]
      : [1, 2, 3, 4].filter(quantity => quantity <= removeMaxQuantity)

  const breadcrumbContextHost =
    typeof document === "undefined" ? null : document.getElementById("page-header-context")

  const breadcrumb =
    loadingHeader ? (
      <Loader2 className="h-3.5 w-3.5 animate-spin text-muted-foreground" aria-label="Loading deck" />
    ) : (
      <div className="group inline-flex items-center gap-2.5">
        <button
          type="button"
          className="h-4 w-4 p-0 text-muted-foreground/40 transition-colors group-hover:text-muted-foreground"
          title="Edit deck name and colors"
          aria-label="Edit deck name and colors"
        >
          <Pencil className="h-3.5 w-3.5 shrink-0 translate-y-px" />
        </button>
        <button
          type="button"
          onClick={handleDeleteDeck}
          disabled={!canEdit}
          className="h-4 w-4 p-0 text-muted-foreground/40 transition-colors hover:text-destructive disabled:cursor-not-allowed disabled:opacity-30"
          title="Delete deck"
          aria-label="Delete deck"
        >
          <Trash2 className="h-3.5 w-3.5 shrink-0 translate-y-px" />
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
        mainCount={visiblePendingMove
          ? visiblePendingMove.cards.filter(card => card.zone === "Mainboard").reduce((sum, card) => sum + card.quantity, 0)
          : counts.main}
        sideCount={visiblePendingMove
          ? visiblePendingMove.cards.filter(card => card.zone === "Sideboard").reduce((sum, card) => sum + card.quantity, 0)
          : counts.side}
        loadingHeader={loadingHeader}
        cards={visiblePendingMove?.cards ?? boardCards}
        cardsLoading={cardsLoading}
        onDragEnd={canEdit ? handleDragEnd : undefined}
        diffMap={diffMap}
        sortMode={sortMode}
        onSortModeChange={setSortMode}
        sideboardCollapsed={isSideboardCollapsed}
        onSideboardCollapsedChange={setIsSideboardCollapsed}
        defaultToolsCollapsed={!preferences.showDeckTools}
        sidePanelView={sidePanelView}
        emphasizedCardName={emphasizedCardName}
        onCardOrderChange={handleCardOrderChange}
        onDeckCardHoverChange={setHoveredDeckCardName}
        statsContent={<DeckStatisticsSidebar key={`${selectedRevisionId ?? deckRevisionId}`}
          onCardEmphasisChange={setEmphasizedCardName}
          cardOrder={cardOrder}
          hoveredDeckCardName={hoveredDeckCardName}
          revisionId={selectedRevisionId ?? Number(deckRevisionId)}
          initialScope={selectedRevisionId != null || new URLSearchParams(location.search).get("scope") === "revision" ? "revision" : "deck"} />}
        onSidePanelViewChange={setSidePanelView}
        historyData={historyData}
        historyLoading={historyLoading}
        historyError={historyError ?? detailError}
        selectedRevisionId={selectedRevisionId}
        onSelectRevision={id => {
          if (id == null && detail && detail.currentRevisionId !== detail.revisionId)
            navigate(`/decks/${detail.currentRevisionId}`, { replace: true })
          else setSelectedRevisionId(id)
        }}
        searchResults={searchResults}
        searchLoading={searchLoading}
        searchError={searchError}
        onSearchQueryChange={setSearchQuery}
        onSearchCardContextMenu={(card, position) => {
          if (canEdit) {
            setCardMenu({ kind: "search", card, position })
          }
        }}
        onDeckCardContextMenu={(card, position) => {
          if (canEdit) {
            setCardMenu({ kind: "deck", card, position })
          }
        }}
        highlightedDeckCard={
          cardMenu?.kind === "deck" ? { catalogId: cardMenu.card.catalogId } : null
        }
        highlightedSearchCardId={
          cardMenu?.kind === "search" ? cardMenu.card.mtgoId : null
        }
        onBack={navigateBack}
        onCopyList={copyDeckList}
        onExportList={downloadDeckList}
        canExport={canExportDeckList}
        copiedList={copiedDeckList}
        importDisabled
        onArchetypeChange={canEdit ? handleArchetypeChange : undefined}
        archetypeSaving={isSavingArchetype}
        archetypeError={archetypeError}
      />
      {cardMenu ? (
        <>
          {/* Click-off backdrop: dismisses the menu (and its highlight) on any
              click. Native window/document listeners don't fire reliably in the
              WebView2 host, so this uses React synthetic handlers. We attach
              pointerdown, mousedown, AND click because this host may deliver
              only some of them; whichever fires dismisses the menu. It sits
              just below the menu (z-100) so the menu stays clickable. */}
          <div
            ref={backdropRef}
            className="fixed inset-0 z-[99]"
            onPointerDown={() => setCardMenu(null)}
            onMouseDown={() => setCardMenu(null)}
            onClick={() => setCardMenu(null)}
          />
        <div
          ref={cardMenuRef}
          role="menu"
          aria-label="Card actions"
          className="fixed z-[100] min-w-56 overflow-hidden rounded-md border border-sidebar-border bg-popover p-1 text-popover-foreground shadow-xl"
          style={{
            left: Math.max(8, Math.min(cardMenu.position.x, window.innerWidth - 240)),
            top: Math.max(8, Math.min(cardMenu.position.y, window.innerHeight - 160)),
          }}
        >
          {cardMenu.kind === "search" ? (
            <>
              <CardMenuQuantityRow
                icon={Plus}
                label="Mainboard"
                quantities={[1, 2, 3, 4]}
                maxQuantity={addMaxQuantity}
                onQuantity={quantity => void addCard(searchCardMutation(cardMenu.card, "Mainboard", quantity))}
              />
              <CardMenuQuantityRow
                icon={Plus}
                label="Sideboard"
                quantities={[1, 2, 3, 4]}
                maxQuantity={addMaxQuantity}
                onQuantity={quantity => void addCard(searchCardMutation(cardMenu.card, "Sideboard", quantity))}
              />
            </>
          ) : (
            <>
              <CardMenuQuantityRow
                icon={Plus}
                label="Mainboard"
                quantities={[1, 2, 3, 4]}
                maxQuantity={addMaxQuantity}
                onQuantity={quantity => void addCard(deckCardMutation(cardMenu.card, "Mainboard", quantity))}
              />
              <CardMenuQuantityRow
                icon={Plus}
                label="Sideboard"
                quantities={[1, 2, 3, 4]}
                maxQuantity={addMaxQuantity}
                onQuantity={quantity => void addCard(deckCardMutation(cardMenu.card, "Sideboard", quantity))}
              />
              <CardMenuQuantityRow
                icon={Minus}
                label={cardMenu.card.zone}
                quantities={removeQuantities}
                destructive
                onQuantity={quantity => void removeCard(deckCardMutation(cardMenu.card, cardMenu.card.zone, quantity))}
              />
            </>
          )}
        </div>
        </>
      ) : null}
      {mutationError ? (
        <div className="fixed bottom-5 left-1/2 z-[110] -translate-x-1/2 rounded-md border border-destructive/40 bg-destructive px-3 py-2 text-sm text-destructive-foreground shadow-xl">
          {mutationError}
        </div>
      ) : null}
    </>
  )
}
