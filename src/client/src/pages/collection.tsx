"use client"

import { useCallback, useEffect, useMemo, useState } from "react"
import { createPortal } from "react-dom"
import { ArrowDown01, ArrowDown10, ArrowDownAZ, ArrowDownZA, Filter, Loader2, Search } from "lucide-react"

import { CardFilterPanel } from "@/components/card-search/card-filter-panel"
import {
  DEFAULT_CARD_FILTERS,
  buildCardSearchQuery,
  getActiveCardFilterCount,
  type CardFilterState,
} from "@/components/card-search/card-search-model"
import { CollectionPriceHistoryPanel } from "@/components/collection/collection-details-panel"
import { VirtualCollectionGrid } from "@/components/collection/collection-grid"
import type {
  CollectionSearchResponse,
  CollectionSortDirection,
  CollectionSortMode,
  CollectionViewMode,
  RenderedCollectionPanel,
  SelectedCollectionItem,
} from "@/components/collection/collection-types"
import { compareCollectionNumbers } from "@/components/collection/collection-utils"
import { Button } from "@/components/ui/button"
import { Card } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { useCollectionCards } from "@/hooks/use-collection"
import { cn } from "@/lib/utils"
import { getApiUrl } from "@/utils/api-config"
const DEFAULT_COLLECTION_SORT_DIRECTION: Record<CollectionSortMode, CollectionSortDirection> = {
  name: "asc",
  quantity: "desc",
  price: "desc",
}
export default function Collection() {
  const { snapshot, cards, products, loading, error } = useCollectionCards()
  const [search, setSearch] = useState("")
  const [collectionFilters, setCollectionFilters] = useState<CardFilterState>(DEFAULT_CARD_FILTERS)
  const [filtersOpen, setFiltersOpen] = useState(false)
  const [filterCatalogIds, setFilterCatalogIds] = useState<Set<number> | null>(null)
  const [filterLoading, setFilterLoading] = useState(false)
  const [filterError, setFilterError] = useState<string | null>(null)
  const [sortMode, setSortMode] = useState<CollectionSortMode>("name")
  const [sortDirection, setSortDirection] = useState<CollectionSortDirection>(
    DEFAULT_COLLECTION_SORT_DIRECTION.name
  )
  const [viewMode, setViewMode] = useState<CollectionViewMode>("cards")
  const [selectedItem, setSelectedItem] = useState<SelectedCollectionItem | null>(null)
  const [renderedPanel, setRenderedPanel] = useState<RenderedCollectionPanel | null>(null)
  const breadcrumbContextHost = typeof document === "undefined"
    ? null
    : document.getElementById("page-header-context")
  const activeFilterCount = useMemo(
    () => getActiveCardFilterCount(collectionFilters),
    [collectionFilters]
  )
  const effectiveCollectionQuery = useMemo(
    () => buildCardSearchQuery(search, collectionFilters),
    [collectionFilters, search]
  )
  const collectionFilterActive = effectiveCollectionQuery.length > 0

  const updateCollectionFilters = useCallback((patch: Partial<CardFilterState>) => {
    setCollectionFilters(current => ({
      ...current,
      ...patch,
    }))
  }, [])

  const clearCollectionFilters = useCallback(() => {
    setCollectionFilters(DEFAULT_CARD_FILTERS)
  }, [])

  const handleSortModeChange = useCallback((value: string) => {
    const nextSortMode = value as CollectionSortMode
    setSortMode(nextSortMode)
    setSortDirection(DEFAULT_COLLECTION_SORT_DIRECTION[nextSortMode])
  }, [])

  const toggleSortDirection = useCallback(() => {
    setSortDirection(current => current === "asc" ? "desc" : "asc")
  }, [])

  const handleSelectItem = useCallback((selection: SelectedCollectionItem) => {
    setSelectedItem(current => current?.item.catalogId === selection.item.catalogId ? null : selection)
  }, [])

  const closeSelectedItem = useCallback(() => {
    setSelectedItem(null)
  }, [])

  useEffect(() => {
    if (selectedItem) {
      setRenderedPanel({ ...selectedItem, open: false })
      const frame = window.requestAnimationFrame(() => {
        setRenderedPanel(current => current ? { ...current, open: true } : current)
      })

      return () => window.cancelAnimationFrame(frame)
    }

    setRenderedPanel(current => current ? { ...current, open: false } : current)
    const timeout = window.setTimeout(() => {
      setRenderedPanel(null)
    }, 180)

    return () => window.clearTimeout(timeout)
  }, [selectedItem])

  useEffect(() => {
    if (!collectionFilterActive || !snapshot) {
      setFilterCatalogIds(null)
      setFilterError(null)
      setFilterLoading(false)
      return
    }

    const abortController = new AbortController()
    setFilterLoading(true)
    setFilterError(null)
    setFilterCatalogIds(null)

    const timeout = window.setTimeout(async () => {
      try {
        const response = await fetch(getApiUrl("/api/collection/cards/search"), {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({ query: effectiveCollectionQuery }),
          signal: abortController.signal,
        })

        if (!response.ok) {
          let message = `HTTP ${response.status}`
          try {
            const body = await response.json()
            message = body.message ?? body.error ?? message
          } catch {
          }

          throw new Error(message)
        }

        const result = await response.json() as CollectionSearchResponse
        setFilterCatalogIds(new Set(result.catalogIds))
      } catch (err) {
        if (err instanceof Error && err.name === "AbortError") return
        setFilterCatalogIds(new Set())
        setFilterError(err instanceof Error ? err.message : "Collection search failed")
      } finally {
        if (!abortController.signal.aborted) {
          setFilterLoading(false)
        }
      }
    }, 250)

    return () => {
      window.clearTimeout(timeout)
      abortController.abort()
    }
  }, [collectionFilterActive, effectiveCollectionQuery, snapshot])

  const SortDirectionIcon = sortMode === "name"
    ? sortDirection === "asc" ? ArrowDownAZ : ArrowDownZA
    : sortDirection === "asc" ? ArrowDown01 : ArrowDown10
  const sortDirectionLabel = sortMode === "name"
    ? sortDirection === "asc" ? "Sort A to Z" : "Sort Z to A"
    : sortMode === "price"
      ? sortDirection === "asc" ? "Sort cheapest first" : "Sort priciest first"
      : sortDirection === "asc" ? "Sort low to high" : "Sort high to low"

  const visibleCards = useMemo(() => {
    const filtered = collectionFilterActive
      ? filterCatalogIds
        ? cards.filter(card => filterCatalogIds.has(card.catalogId))
        : []
      : cards

    return [...filtered].sort((a, b) => {
      if (sortMode === "quantity") {
        return compareCollectionNumbers(a.quantity, b.quantity, sortDirection) ||
          a.name.localeCompare(b.name)
      }

      if (sortMode === "price") {
        return compareCollectionNumbers(a.price, b.price, sortDirection) ||
          a.name.localeCompare(b.name)
      }

      return a.name.localeCompare(b.name) * (sortDirection === "asc" ? 1 : -1)
    })
  }, [cards, collectionFilterActive, filterCatalogIds, sortDirection, sortMode])

  const visibleProducts = useMemo(() => {
    const filtered = collectionFilterActive
      ? filterCatalogIds
        ? products.filter(product => filterCatalogIds.has(product.catalogId))
        : []
      : products

    return [...filtered].sort((a, b) => {
      if (sortMode === "quantity") {
        return compareCollectionNumbers(a.quantity, b.quantity, sortDirection) ||
          a.name.localeCompare(b.name)
      }

      if (sortMode === "price") {
        return compareCollectionNumbers(a.price, b.price, sortDirection) ||
          a.name.localeCompare(b.name)
      }

      return a.name.localeCompare(b.name) * (sortDirection === "asc" ? 1 : -1)
    })
  }, [products, collectionFilterActive, filterCatalogIds, sortDirection, sortMode])

  useEffect(() => {
    if (!selectedItem || !collectionFilterActive || !filterCatalogIds) return

    if (!filterCatalogIds.has(selectedItem.item.catalogId)) {
      setSelectedItem(null)
    }
  }, [collectionFilterActive, filterCatalogIds, selectedItem])

  const renderBreadcrumbCollectionContext = () => (
    <div className="inline-flex min-w-0 items-center gap-2 text-xs text-muted-foreground">
      {snapshot ? (
        <>
          <span>{snapshot.uniqueCount.toLocaleString()} unique</span>
          <span className="h-1 w-1 rounded-full bg-muted-foreground/45" />
          <span>{snapshot.totalQuantity.toLocaleString()} total</span>
        </>
      ) : (
        loading ? (
          <Loader2 className="h-3.5 w-3.5 animate-spin" aria-label="Loading collection" />
        ) : (
          <span>Collection unavailable</span>
        )
      )}
    </div>
  )

  return (
    <div className="flex h-[calc(100vh-2.5rem)] min-h-0 flex-col gap-2 overflow-hidden px-4 pb-4 pt-1">
      {breadcrumbContextHost ? createPortal(
        renderBreadcrumbCollectionContext(),
        breadcrumbContextHost
      ) : null}
      <div className="flex shrink-0 flex-wrap items-center gap-2">
        <div
          role="tablist"
          aria-label="Collection view"
          className="inline-flex h-8 shrink-0 items-center rounded-md border border-sidebar-border/70 bg-background/70 p-0.5"
        >
          {([
            { value: "cards", label: "Cards" },
            { value: "products", label: "Products" },
          ] as const).map(({ value, label }) => (
            <button
              key={value}
              type="button"
              role="tab"
              aria-selected={viewMode === value}
              className={cn(
                "h-7 rounded-sm px-3 text-xs font-medium leading-none transition-colors",
                viewMode === value
                  ? "bg-secondary/80 text-secondary-foreground"
                  : "text-muted-foreground hover:text-foreground"
              )}
              onClick={() => setViewMode(value)}
            >
              {label}
            </button>
          ))}
        </div>

        <div className="relative min-w-[16rem] flex-1">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            value={search}
            onChange={event => setSearch(event.target.value)}
            placeholder={viewMode === "cards" ? "Search cards" : "Search products"}
            className="h-8 border-sidebar-border/70 bg-background/70 pl-8 pr-10 text-sm shadow-none"
          />
          <Popover open={filtersOpen} onOpenChange={setFiltersOpen}>
            <PopoverTrigger asChild>
              <button
                type="button"
                className={cn(
                  "absolute right-0 top-0 flex h-8 w-8 items-center justify-center rounded-r-md border-l border-sidebar-border/60 text-muted-foreground transition-colors hover:bg-muted/55 hover:text-foreground",
                  (activeFilterCount > 0 || filtersOpen) && "bg-secondary text-secondary-foreground hover:bg-secondary"
                )}
                aria-label={filtersOpen ? "Close collection query builder" : "Open collection query builder"}
                aria-expanded={filtersOpen}
              >
                <Filter className="h-4 w-4" />
                {activeFilterCount > 0 ? (
                  <span className="absolute right-1 top-1 h-1.5 w-1.5 rounded-full bg-primary" />
                ) : null}
              </button>
            </PopoverTrigger>
            <PopoverContent
              align="end"
              sideOffset={6}
              className="w-[min(28rem,calc(100vw-2rem))] overflow-hidden border-sidebar-border/70 bg-card p-0"
            >
              <CardFilterPanel
                filters={collectionFilters}
                activeFilterCount={activeFilterCount}
                onUpdate={updateCollectionFilters}
                onClear={clearCollectionFilters}
                onClose={() => setFiltersOpen(false)}
                title="Collection query"
                closeLabel="Close collection query builder"
                className="max-h-[min(42rem,calc(100vh-7rem))]"
              />
            </PopoverContent>
          </Popover>
        </div>

        <div className="flex items-center gap-2">
          <span className="text-xs text-muted-foreground">Sort</span>
          <Select value={sortMode} onValueChange={handleSortModeChange}>
            <SelectTrigger className="h-8 w-[116px] border-sidebar-border/70 bg-background/70 text-xs">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="name">Name</SelectItem>
              <SelectItem value="quantity">Quantity</SelectItem>
              <SelectItem value="price">Price</SelectItem>
            </SelectContent>
          </Select>
          <Button
            type="button"
            variant="outline"
            size="icon"
            className="h-8 w-8 border-sidebar-border/70 bg-background/70 text-muted-foreground hover:bg-muted/60 hover:text-foreground"
            onClick={toggleSortDirection}
            aria-label={sortDirectionLabel}
            title={sortDirectionLabel}
          >
            <SortDirectionIcon className="h-4 w-4" />
          </Button>
        </div>
      </div>

      {filterError ? (
        <div className="shrink-0 rounded-md border border-destructive/30 bg-destructive/10 px-3 py-2 text-xs text-destructive">
          Collection search failed: {filterError}
        </div>
      ) : null}

      <div className="flex min-h-0 flex-1 gap-2 overflow-hidden">
        <Card className="flex min-h-0 flex-1 flex-col overflow-hidden border-sidebar-border/60">
          {viewMode === "cards" && error ? (
            <div className="m-3 rounded-md border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive">
              {error}
            </div>
          ) : null}

          {loading && !snapshot ? (
            <div className="flex min-h-0 flex-1 items-center justify-center">
              <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" aria-label="Loading collection" />
            </div>
          ) : (
            <VirtualCollectionGrid
              items={viewMode === "products" ? visibleProducts : visibleCards}
              viewMode={viewMode}
              loading={loading || filterLoading}
              showPrice={sortMode === "price"}
              selectedCatalogId={selectedItem?.item.catalogId ?? null}
              onSelectItem={handleSelectItem}
            />
          )}
        </Card>

        {renderedPanel ? (
          <div
            className={cn(
              "h-full min-h-0 shrink-0 overflow-hidden transition-[width,opacity,transform] duration-[180ms] ease-out",
              renderedPanel.open
                ? "w-96 translate-x-0 opacity-100"
                : "w-0 translate-x-2 opacity-0"
            )}
            aria-hidden={!renderedPanel.open}
          >
            <CollectionPriceHistoryPanel
              selection={renderedPanel}
              onClose={closeSelectedItem}
            />
          </div>
        ) : null}
      </div>
    </div>
  )
}
