/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

"use client"

import { useCallback, useEffect, useMemo, useState } from "react"
import { createPortal } from "react-dom"
import {
  CollectionLayout,
  type CollectionSelection,
  type CollectionSortDirection,
  type CollectionSortMode,
  type CollectionViewMode,
} from "@videreproject/ui"
import { Loader2 } from "lucide-react"

import { CardFilterPanel } from "@videreproject/ui"
import {
  DEFAULT_CARD_FILTERS,
  buildCardSearchQuery,
  getActiveCardFilterCount,
  type CardFilterState,
} from "@videreproject/ui"
import { useCollectionCards } from "@/hooks/use-collection"
import { useCollectionSelection } from "@/hooks/use-collection-selection"
import { getApiUrl } from "@/utils/api-config"

type CollectionSearchResponse = {
  query: string
  catalogIds: number[]
}

const DEFAULT_SORT_DIRECTION: Record<CollectionSortMode, CollectionSortDirection> = {
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
  const [sortDirection, setSortDirection] = useState<CollectionSortDirection>(DEFAULT_SORT_DIRECTION.name)
  const [viewMode, setViewMode] = useState<CollectionViewMode>("cards")
  const [selectedItem, setSelectedItem] = useState<CollectionSelection | null>(null)
  const resolvedSelection = useCollectionSelection(selectedItem)

  const activeFilterCount = useMemo(
    () => getActiveCardFilterCount(collectionFilters),
    [collectionFilters],
  )
  const effectiveCollectionQuery = useMemo(
    () => buildCardSearchQuery(search, collectionFilters),
    [collectionFilters, search],
  )
  const collectionFilterActive = effectiveCollectionQuery.length > 0

  const updateCollectionFilters = useCallback((patch: Partial<CardFilterState>) => {
    setCollectionFilters(current => ({ ...current, ...patch }))
  }, [])

  useEffect(() => {
    if (!collectionFilterActive || !snapshot) {
      setFilterCatalogIds(null)
      setFilterError(null)
      setFilterLoading(false)
      return
    }

    const controller = new AbortController()
    setFilterLoading(true)
    setFilterError(null)
    setFilterCatalogIds(null)

    const timeout = window.setTimeout(async () => {
      try {
        const response = await fetch(getApiUrl("/api/collection/cards/search"), {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ query: effectiveCollectionQuery }),
          signal: controller.signal,
        })
        if (!response.ok) {
          let message = `HTTP ${response.status}`
          try {
            const body = await response.json()
            message = body.message ?? body.error ?? message
          } catch {
            // The status code is sufficient when the server did not return JSON.
          }
          throw new Error(message)
        }
        const result = await response.json() as CollectionSearchResponse
        setFilterCatalogIds(new Set(result.catalogIds))
      } catch (requestError) {
        if (requestError instanceof Error && requestError.name === "AbortError") return
        setFilterCatalogIds(new Set())
        setFilterError(requestError instanceof Error ? requestError.message : "Collection search failed")
      } finally {
        if (!controller.signal.aborted) setFilterLoading(false)
      }
    }, 250)

    return () => {
      window.clearTimeout(timeout)
      controller.abort()
    }
  }, [collectionFilterActive, effectiveCollectionQuery, snapshot])

  const visibleCards = useMemo(() => (
    collectionFilterActive
      ? filterCatalogIds ? cards.filter(card => filterCatalogIds.has(card.catalogId)) : []
      : cards
  ), [cards, collectionFilterActive, filterCatalogIds])

  const visibleProducts = useMemo(() => (
    collectionFilterActive
      ? filterCatalogIds ? products.filter(product => filterCatalogIds.has(product.catalogId)) : []
      : products
  ), [collectionFilterActive, filterCatalogIds, products])

  useEffect(() => {
    if (!selectedItem || !collectionFilterActive || !filterCatalogIds) return
    if (!filterCatalogIds.has(selectedItem.item.catalogId)) setSelectedItem(null)
  }, [collectionFilterActive, filterCatalogIds, selectedItem])

  const breadcrumbContextHost = typeof document === "undefined"
    ? null
    : document.getElementById("page-header-context")

  return (
    <>
      {breadcrumbContextHost ? createPortal(
        <div className="inline-flex min-w-0 items-center gap-2 text-xs text-muted-foreground">
          {snapshot ? (
            <>
              <span>{snapshot.uniqueCount.toLocaleString()} unique</span>
              <span className="h-1 w-1 rounded-full bg-muted-foreground/45" />
              <span>{snapshot.totalQuantity.toLocaleString()} total</span>
            </>
          ) : loading ? (
            <Loader2 className="h-3.5 w-3.5 animate-spin" aria-label="Loading collection" />
          ) : (
            <span>Collection unavailable</span>
          )}
        </div>,
        breadcrumbContextHost,
      ) : null}
      <CollectionLayout
        className="h-[calc(100vh-2.5rem)]"
        cards={visibleCards}
        products={visibleProducts}
        filterItems={false}
        viewMode={viewMode}
        onViewModeChange={setViewMode}
        search={search}
        onSearchChange={setSearch}
        sortMode={sortMode}
        sortDirection={sortDirection}
        onSortModeChange={mode => {
          setSortMode(mode)
          setSortDirection(DEFAULT_SORT_DIRECTION[mode])
        }}
        onSortDirectionChange={setSortDirection}
        selection={resolvedSelection.selection}
        onSelectionChange={setSelectedItem}
        onCloseDetails={() => setSelectedItem(null)}
        loading={loading || filterLoading || resolvedSelection.loading}
        error={filterError
          ? `Collection search failed: ${filterError}`
          : resolvedSelection.error
            ? `Collection details failed: ${resolvedSelection.error}`
            : viewMode === "cards" ? error : null}
        activeFilterCount={activeFilterCount}
        filterOpen={filtersOpen}
        onFilterOpenChange={setFiltersOpen}
        filterContent={(
          <CardFilterPanel
            filters={collectionFilters}
            activeFilterCount={activeFilterCount}
            onUpdate={updateCollectionFilters}
            onClear={() => setCollectionFilters(DEFAULT_CARD_FILTERS)}
            onClose={() => setFiltersOpen(false)}
            title="Collection query"
            closeLabel="Close collection query builder"
            className="max-h-[min(42rem,calc(100vh-7rem))]"
          />
        )}
      />
    </>
  )
}
