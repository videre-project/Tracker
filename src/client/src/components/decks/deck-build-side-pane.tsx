/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useCallback, useEffect, useMemo, useRef, useState } from "react"
import { BarChart3, Coins, Filter, History, RotateCcw, Search } from "lucide-react"

import { CardFilterPanel } from "@/components/card-search/card-filter-panel"
import {
  DEFAULT_CARD_FILTERS,
  buildCardSearchQuery,
  getActiveCardFilterCount,
  type CardFilterState,
} from "@/components/card-search/card-search-model"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Skeleton } from "@/components/ui/skeleton"
import { type CardSearchResult, useDeckCardSearch } from "@/hooks/use-deck-card-search"
import { CardImage } from "@/components/card-image"
import { useCardTooltipHover } from "@/components/card-tooltip"
import type { DeckHistoryChange, DeckHistoryData } from "@/hooks/use-deck-history"
import { cn } from "@/lib/utils"
import { GameLogText } from "@/utils/parse-game-log"
import { getCardStatText } from "@/utils/card-stats"
import { getManaSymbolSvgPath } from "@/utils/mana-symbols"
import { getDisplayCardColors } from "@/utils/card-colors"

const RESULTS_SCROLLBAR_GUTTER_WIDTH = 12

export type SidePanelView = "cards" | "stats" | "history" | "rental"

function formatHistoryTime(dateString?: string) {
  if (!dateString) return "–"
  const d = new Date(dateString)
  return d.toLocaleString(undefined, {
    month: "short",
    day: "numeric",
    hour: "numeric",
    minute: "2-digit",
  })
}

function SearchResultCardRow({
  card,
  selected,
  onSelect,
}: {
  card: CardSearchResult
  selected: boolean
  onSelect: () => void
}) {
  const tooltipHandlers = useCardTooltipHover({
    catalogId: card.mtgoId,
    name: card.name,
  })

  const normalizedText = card.text
    ? card.text.replace(/\r\n/g, "\n").replace(/\\n/g, "\n")
    : ""
  const powerText = getCardStatText(card)

  return (
    <button
      {...tooltipHandlers}
      type="button"
      onClick={onSelect}
      aria-expanded={selected}
      className={cn(
        "relative block w-full rounded-md border border-transparent px-2.5 py-2 text-left ring-1 ring-inset transition-colors",
        selected
          ? "bg-secondary/70 ring-primary/70"
          : "bg-background/50 ring-sidebar-border/60 hover:bg-background/80 hover:ring-sidebar-border"
      )}
    >
      <div className="absolute right-2.5 top-2 flex shrink-0 items-center gap-0.5">
        {getDisplayCardColors(card.colors).map(color => (
          <img
            key={`${card.name}-${color}`}
            src={getManaSymbolSvgPath(color) ?? undefined}
            alt={color}
            className="h-4 w-4 rounded-full bg-background ring-1 ring-background"
          />
        ))}
      </div>

      <div className="flex min-w-0 items-center gap-2 overflow-hidden pr-8">
        <div className="min-w-0 truncate text-sm font-medium text-foreground">
          {card.name}
        </div>
        {card.setCode ? (
          <span
            className={cn(
              "shrink-0 rounded-md border px-1.5 py-0.5 text-center text-[11px] font-medium uppercase",
              "border-sidebar-border/70 bg-background/70 text-muted-foreground"
            )}
          >
            {card.setCode}
          </span>
        ) : null}
      </div>

      {selected ? (
        <div className="mt-1.5 min-w-0 whitespace-pre-wrap text-xs leading-5 text-muted-foreground">
          <span className="text-foreground/80">{card.type}</span>
          <br />
          <GameLogText
            text={normalizedText}
            className="text-muted-foreground/90"
            manaSymbolClassName="inline h-3.5 w-3.5 align-text-bottom mx-0.5"
          />
          {powerText ? (
            <>
              <span
                className={cn(
                  "float-right ml-1 mt-0.5 inline-flex items-center rounded-sm bg-background/70 px-1 py-0 text-[11px] font-medium leading-4 ring-1 ring-sidebar-border/65",
                  "text-foreground/85"
                )}
              >
                {powerText}
              </span>
              <span className="block h-0 clear-both" />
            </>
          ) : null}
        </div>
      ) : (
        <div
          className={cn(
            "mt-1.5 grid min-w-0 items-center gap-x-1 text-xs text-muted-foreground",
            powerText ? "grid-cols-[minmax(0,1fr)_auto]" : "grid-cols-1"
          )}
        >
          <div className="min-w-0 truncate">
            <span className="text-foreground/80">{card.type}</span>
            <span className="mx-1 text-muted-foreground/60">-</span>
            <GameLogText
              text={normalizedText}
              className="inline"
              manaSymbolClassName="inline h-3.5 w-3.5 align-text-bottom mx-0.5"
            />
          </div>
          {powerText ? (
            <span className="inline-flex items-center rounded-sm bg-background/70 px-1 py-0 text-[11px] font-medium leading-4 text-muted-foreground/90 ring-1 ring-sidebar-border/65">
              {powerText}
            </span>
          ) : null}
        </div>
      )}
    </button>
  )
}

function MiniDiffCardItem({ catalogId, borderClass }: { catalogId: number; borderClass: string }) {
  const tooltipHandlers = useCardTooltipHover({ catalogId })

  return (
    <div
      {...tooltipHandlers}
      className={cn(
        "relative w-full h-[26px] overflow-hidden border-t first:border-t-0 bg-black/40 cursor-pointer",
        borderClass
      )}
    >
      <CardImage
        catalogId={catalogId}
        alt=""
        className="w-full h-auto object-top select-none pointer-events-none"
      />
    </div>
  )
}

function MiniCardDiffColumns({ changes }: { changes: DeckHistoryChange[] }) {
  const additions = useMemo(() => {
    const list: number[] = []
    for (const c of changes) {
      if (c.quantityDelta > 0) {
        for (let i = 0; i < c.quantityDelta; i++) {
          list.push(c.catalogId)
        }
      }
    }
    return list
  }, [changes])

  const removals = useMemo(() => {
    const list: number[] = []
    for (const c of changes) {
      if (c.quantityDelta < 0) {
        for (let i = 0; i < Math.abs(c.quantityDelta); i++) {
          list.push(c.catalogId)
        }
      }
    }
    return list
  }, [changes])

  const hasChanges = additions.length > 0 || removals.length > 0
  if (!hasChanges) return null

  return (
    <div className="-mx-2.5 -mb-2.5 mt-1.5 border-t border-sidebar-border/50 bg-background/40 rounded-b-lg px-2.5 pt-1.5 pb-0 overflow-hidden">
      <div className="grid grid-cols-2 gap-3 w-full divide-x divide-sidebar-border/30 pb-0 items-stretch">
        {/* Additions Column */}
        <div className="flex flex-col gap-1.5 min-w-0 pr-0.5 pb-0 h-full">
          <div className="text-[10px] font-semibold text-emerald-400/90 uppercase tracking-wider">
            <span className="truncate">Added ({additions.length})</span>
          </div>
          {additions.length > 0 ? (
            <div className="flex flex-col -space-y-px rounded-t border border-b-0 border-emerald-500/40 shadow-sm overflow-hidden ring-1 ring-emerald-500/20">
              {additions.map((catalogId, idx) => (
                <MiniDiffCardItem
                  key={`${catalogId}-${idx}`}
                  catalogId={catalogId}
                  borderClass="border-emerald-500/20"
                />
              ))}
            </div>
          ) : (
            <div className="flex-1 min-h-[26px] w-full rounded-t border border-b-0 border-dashed border-sidebar-border/40 bg-background/20 flex items-center justify-center text-[10px] text-muted-foreground/30 font-medium">
              None
            </div>
          )}
        </div>

        {/* Removals Column */}
        <div className="flex flex-col gap-1.5 min-w-0 pl-3 pb-0 h-full">
          <div className="text-[10px] font-semibold text-rose-400/90 uppercase tracking-wider">
            <span className="truncate">Removed ({removals.length})</span>
          </div>
          {removals.length > 0 ? (
            <div className="flex flex-col -space-y-px rounded-t border border-b-0 border-rose-500/40 shadow-sm overflow-hidden ring-1 ring-rose-500/20">
              {removals.map((catalogId, idx) => (
                <MiniDiffCardItem
                  key={`${catalogId}-${idx}`}
                  catalogId={catalogId}
                  borderClass="border-rose-500/20"
                />
              ))}
            </div>
          ) : (
            <div className="flex-1 min-h-[26px] w-full rounded-t border border-b-0 border-dashed border-sidebar-border/40 bg-background/20 flex items-center justify-center text-[10px] text-muted-foreground/30 font-medium">
              None
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

export function DeckBuildSidePane({
  view,
  onViewChange,
  isCollapsed,
  historyData,
  historyLoading,
  historyError,
  selectedRevisionId,
  onSelectRevision,
}: {
  view: SidePanelView
  onViewChange: (view: SidePanelView) => void
  isCollapsed: boolean
  historyData?: DeckHistoryData | null
  historyLoading?: boolean
  historyError?: string | null
  selectedRevisionId?: number | null
  onSelectRevision?: (revisionId: number | null) => void
}) {
  const [cardSearch, setCardSearch] = useState("")
  const [cardFilters, setCardFilters] = useState<CardFilterState>(DEFAULT_CARD_FILTERS)
  const [filtersOpen, setFiltersOpen] = useState(false)
  const [selectedCardName, setSelectedCardName] = useState<string | null>(null)
  const [showResultsFade, setShowResultsFade] = useState(false)
  const [resultsScrollbarWidth, setResultsScrollbarWidth] = useState(0)
  const resultsScrollRef = useRef<HTMLDivElement>(null)
  const filterButtonRef = useRef<HTMLButtonElement>(null)
  const filterPanelRef = useRef<HTMLDivElement>(null)
  const activeView = view

  const effectiveSearchQuery = useMemo(
    () => buildCardSearchQuery(cardSearch, cardFilters),
    [cardFilters, cardSearch]
  )
  const {
    results: visibleSearchResults,
    loading: searchLoading,
    error: searchError,
  } = useDeckCardSearch(effectiveSearchQuery)

  const activeFilterCount = useMemo(
    () => getActiveCardFilterCount(cardFilters),
    [cardFilters]
  )

  useEffect(() => {
    if (view !== "cards") {
      setFiltersOpen(false)
    }
  }, [view])

  useEffect(() => {
    if (isCollapsed) {
      setFiltersOpen(false)
    }
  }, [isCollapsed])

  useEffect(() => {
    if (!filtersOpen) return

    const handlePointerDown = (event: Event) => {
      const target = event.target as Node | null
      if (!target) return

      const panel = filterPanelRef.current
      const button = filterButtonRef.current
      if (panel?.contains(target) || button?.contains(target)) return

      setFiltersOpen(false)
    }

    document.addEventListener("pointerdown", handlePointerDown)

    return () => {
      document.removeEventListener("pointerdown", handlePointerDown)
    }
  }, [filtersOpen])

  useEffect(() => setSelectedCardName(null), [effectiveSearchQuery])

  const updateResultsFade = useCallback(() => {
    const container = resultsScrollRef.current
    if (!container) return

    const hasOverflow = container.scrollHeight > container.clientHeight + 1
    const isAtBottom = container.scrollTop + container.clientHeight >= container.scrollHeight - 1
    const measuredScrollbarWidth = Math.max(0, container.offsetWidth - container.clientWidth)
    const scrollbarWidth = hasOverflow
      ? Math.max(RESULTS_SCROLLBAR_GUTTER_WIDTH, measuredScrollbarWidth)
      : 0

    setShowResultsFade(hasOverflow && !isAtBottom)
    setResultsScrollbarWidth(current =>
      current === scrollbarWidth ? current : scrollbarWidth
    )
  }, [])

  useEffect(() => {
    const container = resultsScrollRef.current
    if (!container) return

    container.addEventListener("scroll", updateResultsFade)
    const observer = new ResizeObserver(updateResultsFade)
    observer.observe(container)
    updateResultsFade()

    return () => {
      container.removeEventListener("scroll", updateResultsFade)
      observer.disconnect()
    }
  }, [updateResultsFade, visibleSearchResults.length])

  const updateCardFilters = useCallback((updates: Partial<CardFilterState>) => {
    setCardFilters(current => ({ ...current, ...updates }))
  }, [])

  const tabClass = (tab: SidePanelView) => cn(
    "h-8 justify-start border-sidebar-border/70 px-2.5 text-xs",
    activeView === tab
      ? "bg-secondary text-secondary-foreground"
      : "bg-background/60 text-muted-foreground"
  )
  const toggleCardSelection = useCallback((cardName: string) => {
    setSelectedCardName(current => (current === cardName ? null : cardName))
  }, [])

  return (
    <div className={cn(
      "relative min-h-0 shrink-0 self-stretch",
      isCollapsed ? "hidden" : "hidden xl:flex"
    )}>
      <aside className="flex h-full min-h-0 w-80 shrink-0 flex-col overflow-hidden rounded-lg border border-sidebar-border/60 bg-card/65">
        <div className="border-b border-sidebar-border/60 p-3">
          <div className="grid grid-cols-2 gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => onViewChange("cards")}
              className={tabClass("cards")}
            >
              <Search className="h-4 w-4" />
              Cards
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled
              title="Deck statistics are not available yet"
              className={tabClass("stats")}
            >
              <BarChart3 className="h-4 w-4" />
              Stats
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => onViewChange("history")}
              className={tabClass("history")}
            >
              <History className="h-4 w-4" />
              History
            </Button>
            <Button
              variant="outline"
              size="sm"
              disabled
              title="Rental tools are not available yet"
              className={tabClass("rental")}
            >
              <Coins className="h-4 w-4" />
              Rental
            </Button>
          </div>
        </div>

        <div className="flex min-h-0 flex-1 flex-col overflow-hidden p-3">
          {activeView === "history" ? (
            <div className="flex min-h-0 flex-1 flex-col gap-3">
              <div className="flex items-center justify-between shrink-0">
                <div>
                  <h3 className="text-xs font-semibold text-foreground">Revision History</h3>
                  <p className="text-[11px] text-muted-foreground">
                    {historyData?.revisions?.length ?? 0} revision(s) recorded
                  </p>
                </div>
                {selectedRevisionId != null && (
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => onSelectRevision?.(null)}
                    className="h-7 px-2 text-xs gap-1 text-muted-foreground hover:text-foreground"
                    title="Reset to current deck state"
                  >
                    <RotateCcw className="h-3.5 w-3.5" />
                    Reset
                  </Button>
                )}
              </div>

              <div className="flex-1 min-h-0 overflow-y-auto pr-1 space-y-2.5">
                {historyLoading ? (
                  <div className="flex flex-col gap-2">
                    {[0, 1, 2].map(i => (
                      <div key={i} className="h-20 rounded-md border border-sidebar-border/50 bg-background/35 p-3 space-y-2">
                        <Skeleton className="h-3.5 w-36" />
                        <Skeleton className="h-3 w-48" />
                      </div>
                    ))}
                  </div>
                ) : historyError ? (
                  <div className="rounded-md bg-destructive/10 p-3 text-xs text-destructive border border-destructive/20">
                    {historyError}
                  </div>
                ) : !historyData?.revisions?.length ? (
                  <div className="rounded-md border border-sidebar-border/60 bg-background/45 p-4 text-center text-xs text-muted-foreground">
                    No historical revisions recorded for this deck.
                  </div>
                ) : (
                   historyData.revisions.map((rev, index) => {
                    const isLatest = index === 0
                    const isInitial = index === historyData.revisions.length - 1
                    const isSelected = selectedRevisionId === rev.revisionId || (selectedRevisionId === null && isLatest)
                    const diffs = rev.changesFromPrevious

                    return (
                      <div
                        key={rev.revisionId}
                        onClick={() => onSelectRevision?.(isLatest ? null : rev.revisionId)}
                        className={cn(
                          "group relative cursor-pointer rounded-lg border px-2.5 py-1.5 transition-all text-left overflow-hidden",
                          isSelected
                            ? "border-primary bg-secondary/60 shadow-sm ring-1 ring-primary/40"
                            : "border-sidebar-border/60 bg-background/50 hover:bg-background/80 hover:border-sidebar-border"
                        )}
                      >
                        <div className="flex items-center justify-between gap-2">
                          <div className="flex items-center gap-1.5 min-w-0">
                            <span className={cn(
                              "rounded px-1 py-0.5 text-[10px] font-semibold uppercase tracking-wider leading-none shrink-0 -ml-1",
                              isLatest
                                ? "bg-primary/20 text-primary border border-primary/30"
                                : "bg-muted text-muted-foreground"
                            )}>
                              {isLatest ? "Latest" : `v${historyData.revisions.length - index}`}
                            </span>
                            {isInitial && (
                              <span className="text-xs leading-none text-muted-foreground truncate">
                                Initial version
                              </span>
                            )}
                          </div>
                          <span className="text-xs font-medium leading-none text-muted-foreground truncate shrink-0">
                            {formatHistoryTime(rev.timestamp || rev.observedAt)}
                          </span>
                        </div>

                        <MiniCardDiffColumns changes={diffs} />
                      </div>
                    )
                  })
                )}
              </div>
            </div>
          ) : activeView === "cards" ? (
            <div className="flex min-h-0 flex-1 flex-col gap-3">
              <div className="shrink-0">
                <div>
                  <div className="relative">
                    <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                    <Input
                      id="deck-card-search"
                      aria-label="Card search"
                      value={cardSearch}
                      onChange={event => setCardSearch(event.target.value)}
                      placeholder="Search cards"
                      className="h-9 border-sidebar-border/70 bg-background/65 pl-8 pr-11 text-sm shadow-none"
                    />
                    <button
                      type="button"
                      ref={filterButtonRef}
                      onClick={() => setFiltersOpen(current => !current)}
                      className={cn(
                        "absolute right-0 top-0 flex h-9 w-9 items-center justify-center rounded-r-md border-l border-sidebar-border/60 text-muted-foreground transition-colors hover:bg-muted/55 hover:text-foreground",
                        (activeFilterCount > 0 || filtersOpen) && "bg-secondary text-secondary-foreground hover:bg-secondary"
                      )}
                      aria-label={filtersOpen ? "Close card query builder" : "Open card query builder"}
                      aria-expanded={filtersOpen}
                    >
                      <Filter className="h-4 w-4" />
                      {activeFilterCount > 0 ? (
                        <span className="absolute right-1 top-1 h-1.5 w-1.5 rounded-full bg-primary" />
                      ) : null}
                    </button>
                  </div>
                </div>
              </div>

              <div className="flex min-h-0 flex-1 flex-col">
                <div className="relative min-h-0 flex-1">
                  <div
                    ref={resultsScrollRef}
                    className="h-full min-h-0 overflow-y-auto overflow-x-hidden pb-3"
                    style={{
                      width: resultsScrollbarWidth > 0
                        ? `calc(100% + ${resultsScrollbarWidth}px)`
                        : "100%",
                    }}
                  >
                    {searchLoading ? (
                      <div className="flex flex-col gap-2 px-px pb-1">
                        {[0, 1, 2, 3].map(index => (
                          <div
                            key={index}
                            className="h-[74px] rounded-md border border-transparent bg-background/35 ring-1 ring-inset ring-sidebar-border/50"
                          >
                            <div className="space-y-2 px-2.5 py-2">
                              <Skeleton className="h-4 w-40" />
                              <Skeleton className="h-3 w-56" />
                            </div>
                          </div>
                        ))}
                      </div>
                    ) : searchError ? (
                      <div className="mx-px rounded-md border border-transparent bg-destructive/10 p-4 text-center text-xs text-destructive ring-1 ring-inset ring-destructive/30">
                        Card search failed: {searchError}
                      </div>
                    ) : !effectiveSearchQuery ? (
                      <div className="mx-px rounded-md border border-transparent bg-background/45 p-4 text-center text-xs text-muted-foreground ring-1 ring-inset ring-sidebar-border/60">
                        Enter a query or apply filters.
                      </div>
                    ) : visibleSearchResults.length > 0 ? (
                      <div className="flex flex-col gap-2 px-px pb-1">

                        {visibleSearchResults.map(card => (
                          <SearchResultCardRow
                            key={card.name}
                            card={card}
                            selected={selectedCardName === card.name}
                            onSelect={() => toggleCardSelection(card.name)}
                          />
                        ))}
                      </div>
                    ) : (
                      <div className="mx-px rounded-md border border-transparent bg-background/45 p-4 text-center text-xs text-muted-foreground ring-1 ring-inset ring-sidebar-border/60">
                        No cards match the current search.
                      </div>
                    )}
                  </div>
                  {showResultsFade ? (
                    <div
                      className="pointer-events-none absolute bottom-0 left-px right-px z-10 h-8"
                      style={{
                        background: "linear-gradient(to top, hsl(var(--card)), transparent)",
                      }}
                    />
                  ) : null}
                </div>
              </div>

            </div>
          ) : null}
        </div>
      </aside>

      <div
        ref={filterPanelRef}
        role="dialog"
        aria-modal="false"
        aria-hidden={!filtersOpen}
        aria-labelledby="deck-card-filters-title"
        className={cn(
          "absolute bottom-0 right-full top-0 z-30 mr-3 flex w-[min(28rem,calc(100vw-43rem))] min-w-80 flex-col overflow-hidden rounded-lg border border-sidebar-border/70 bg-card shadow-xl transition-all duration-200 ease-out",
          filtersOpen
            ? "translate-x-0 opacity-100"
            : "pointer-events-none translate-x-3 opacity-0"
        )}
      >
        <CardFilterPanel
          filters={cardFilters}
          activeFilterCount={activeFilterCount}
          onUpdate={updateCardFilters}
          onClear={() => setCardFilters(DEFAULT_CARD_FILTERS)}
          onClose={() => setFiltersOpen(false)}
          titleId="deck-card-filters-title"
        />
      </div>
    </div>
  )
}
