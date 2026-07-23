/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import {
  type CSSProperties,
  type RefObject,
  useCallback,
  useEffect,
  useRef,
  useState,
} from "react"
import { Loader2 } from "lucide-react"

import { useCardImage } from "@/hooks/use-card-image"
import { useCardTooltipHover } from "@/components/card-tooltip"
import type { CollectionCardEntry, CollectionProductEntry } from "@/hooks/use-collection"
import { cn } from "@/lib/utils"
import { getApiUrl } from "@/utils/api-config"
import { getStackPeekOffset } from "@/utils/card-layout"
import type {
  CollectionGridItem,
  CollectionViewMode,
  SelectedCollectionItem,
} from "./collection-types"
import { formatCollectionPrice } from "./collection-utils"

const COLLECTION_GRID_GAP = 10
const COLLECTION_MIN_CARD_WIDTH = 118
const COLLECTION_CARD_RATIO = 5 / 7
const COLLECTION_ROW_OVERSCAN = 3
function useElementSize<T extends HTMLElement>(ref: RefObject<T | null>) {
  const [size, setSize] = useState({ width: 0, height: 0, scrollbarWidth: 0 })

  useEffect(() => {
    const element = ref.current
    if (!element) return

    const updateSize = () => {
      setSize({
        width: element.clientWidth,
        height: element.clientHeight,
        scrollbarWidth: Math.max(0, element.offsetWidth - element.clientWidth),
      })
    }

    updateSize()
    if (typeof ResizeObserver === "undefined") return

    const observer = new ResizeObserver(updateSize)
    observer.observe(element)
    return () => observer.disconnect()
  }, [ref])

  return size
}

function CollectionPriceBadge({
  price,
  quantity,
  top,
}: {
  price?: number | null
  quantity: number
  top: number
}) {
  const eachLabel = formatCollectionPrice(price)
  const totalLabel = formatCollectionPrice(
    typeof price === "number" && Number.isFinite(price)
      ? price * quantity
      : null
  )

  if (!eachLabel || !totalLabel) return null

  return (
    <div
      className="pointer-events-none absolute right-2.5 z-20 min-w-[3.4rem] rounded-sm bg-black/85 px-1.5 py-1 text-white shadow-sm ring-1 ring-white/15"
      style={{ top }}
    >
      <div className="flex items-baseline justify-between gap-1.5 leading-none">
        <span className="text-[8px] font-medium uppercase text-white/55">total</span>
        <span className="text-[12px] font-semibold tabular-nums">{totalLabel}</span>
      </div>
      <div className="mt-0.5 flex items-baseline justify-between gap-1.5 border-t border-white/10 pt-0.5 leading-none">
        <span className="text-[8px] font-medium uppercase text-white/45">ea</span>
        <span className="text-[10px] font-medium tabular-nums text-white/75">{eachLabel}</span>
      </div>
    </div>
  )
}

export function CollectionCardImage({
  catalogId,
  name,
}: {
  catalogId: number
  name: string
}) {
  const src = useCardImage(catalogId)
  const [source, setSource] = useState<"cdn" | "fallback" | "failed">("cdn")

  useEffect(() => {
    setSource("cdn")
  }, [catalogId])

  const fallbackSrc = catalogId > 0
    ? getApiUrl(`/api/collection/cards/${catalogId}/image`)
    : null
  const imageSrc = source === "fallback" ? fallbackSrc : source === "cdn" ? src : null

  if (imageSrc && source !== "failed") {
    return (
      <img
        src={imageSrc}
        alt={name}
        loading="lazy"
        decoding="async"
        className="h-full w-full object-cover"
        onError={() => {
          setSource(current => current === "cdn" && fallbackSrc ? "fallback" : "failed")
        }}
      />
    )
  }

  return (
    <div className="flex h-full w-full items-center justify-center bg-muted/70 p-2">
      <span className="text-center text-[10px] leading-tight text-muted-foreground">
        {name}
      </span>
    </div>
  )
}

function CollectionCardTile({
  card,
  quantityTop,
  showPrice,
  selected,
  onSelect,
  position,
}: {
  card: CollectionCardEntry
  quantityTop: number
  showPrice: boolean
  selected: boolean
  onSelect: () => void
  position: CSSProperties
}) {
  const tooltipHandlers = useCardTooltipHover({
    catalogId: card.catalogId,
    name: card.name,
  })

  const priceLabel = showPrice ? formatCollectionPrice(card.price) : null
  const totalPriceLabel = showPrice && typeof card.price === "number" && Number.isFinite(card.price)
    ? formatCollectionPrice(card.price * card.quantity)
    : null

  return (
    <button
      {...tooltipHandlers}
      type="button"
      onClick={onSelect}
      className={cn(
        "group absolute overflow-hidden rounded-md border border-sidebar-border/60 bg-muted/20 p-0 text-left shadow-sm transition-[border-color,box-shadow] hover:border-sidebar-accent/70 hover:shadow-[0_0_0_1px_rgba(255,255,255,0.15)] focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring",
        selected && "border-primary/70 shadow-[0_0_0_1px_hsl(var(--primary)/0.75)] hover:border-primary/70 hover:shadow-[0_0_0_1px_hsl(var(--primary)/0.75)]"
      )}
      style={position}
      title={priceLabel && totalPriceLabel
        ? `${card.quantity}x ${card.name} - ${totalPriceLabel} tix total, ${priceLabel} tix each`
        : `${card.quantity}x ${card.name}`}
    >
      <CollectionCardImage catalogId={card.catalogId} name={card.name} />
      <div
        className="pointer-events-none absolute left-2.5 z-20 rounded-sm bg-black/80 px-1.5 py-0.5 text-[11px] font-bold leading-none text-white"
        style={{ top: quantityTop }}
      >
        {card.quantity}
      </div>
      {showPrice ? (
        <CollectionPriceBadge price={card.price} quantity={card.quantity} top={quantityTop} />
      ) : null}
    </button>
  )
}

export function CollectionProductImage({
  product,
}: {
  product: CollectionProductEntry
}) {
  if (product.imageUrl) {
    return (
      <img
        src={product.imageUrl}
        alt={product.name}
        loading="lazy"
        decoding="async"
        className="h-full w-full object-contain p-1.5"
      />
    )
  }

  return (
    <div className="flex h-full w-full items-center justify-center bg-muted/70 p-2">
      <span className="text-center text-[10px] leading-tight text-muted-foreground">
        {product.name}
      </span>
    </div>
  )
}

function CollectionProductTile({
  product,
  quantityTop,
  showPrice,
  selected,
  onSelect,
  position,
}: {
  product: CollectionProductEntry
  quantityTop: number
  showPrice: boolean
  selected: boolean
  onSelect: () => void
  position: CSSProperties
}) {
  const priceLabel = showPrice ? formatCollectionPrice(product.price) : null
  const totalPriceLabel = showPrice && typeof product.price === "number" && Number.isFinite(product.price)
    ? formatCollectionPrice(product.price * product.quantity)
    : null

  return (
    <button
      type="button"
      onClick={onSelect}
      className={cn(
        "group absolute overflow-hidden rounded-md border border-sidebar-border/60 bg-muted/20 p-0 text-left shadow-sm transition-[border-color,box-shadow] hover:border-sidebar-accent/70 hover:shadow-[0_0_0_1px_rgba(255,255,255,0.15)] focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring",
        selected && "border-primary/70 shadow-[0_0_0_1px_hsl(var(--primary)/0.75)] hover:border-primary/70 hover:shadow-[0_0_0_1px_hsl(var(--primary)/0.75)]"
      )}
      style={position}
      title={priceLabel && totalPriceLabel
        ? `${product.quantity}x ${product.name} - ${totalPriceLabel} tix total, ${priceLabel} tix each`
        : `${product.quantity}x ${product.name}`}
    >
      <CollectionProductImage product={product} />
      <div
        className="pointer-events-none absolute left-2.5 z-20 rounded-sm bg-black/80 px-1.5 py-0.5 text-[11px] font-bold leading-none text-white"
        style={{ top: quantityTop }}
      >
        {product.quantity}
      </div>
      {showPrice ? (
        <CollectionPriceBadge price={product.price} quantity={product.quantity} top={quantityTop} />
      ) : null}
    </button>
  )
}

export function VirtualCollectionGrid({
  items,
  viewMode,
  loading,
  showPrice,
  selectedCatalogId,
  onSelectItem,
}: {
  items: CollectionGridItem[]
  viewMode: CollectionViewMode
  loading: boolean
  showPrice: boolean
  selectedCatalogId: number | null
  onSelectItem: (selection: SelectedCollectionItem) => void
}) {
  const scrollRef = useRef<HTMLDivElement>(null)
  const { width, height, scrollbarWidth } = useElementSize(scrollRef)
  const [scrollTop, setScrollTop] = useState(0)
  const layoutWidth = width + scrollbarWidth

  const columns = Math.max(
    1,
    Math.floor((layoutWidth + COLLECTION_GRID_GAP) / (COLLECTION_MIN_CARD_WIDTH + COLLECTION_GRID_GAP))
  )
  const itemWidth = Math.max(
    96,
    Math.floor((layoutWidth - COLLECTION_GRID_GAP * (columns + 1)) / columns)
  )
  const itemHeight = Math.round(itemWidth / COLLECTION_CARD_RATIO)
  const quantityTop = Math.round(getStackPeekOffset(itemHeight))
  const rowHeight = itemHeight + COLLECTION_GRID_GAP
  const totalRows = Math.ceil(items.length / columns)
  const startRow = Math.max(0, Math.floor(scrollTop / rowHeight) - COLLECTION_ROW_OVERSCAN)
  const endRow = Math.min(
    totalRows,
    Math.ceil((scrollTop + height) / rowHeight) + COLLECTION_ROW_OVERSCAN
  )
  const startIndex = startRow * columns
  const endIndex = Math.min(items.length, endRow * columns)
  const visibleItems = items.slice(startIndex, endIndex)
  const totalHeight = totalRows * rowHeight + COLLECTION_GRID_GAP

  const handleScroll = useCallback(() => {
    setScrollTop(scrollRef.current?.scrollTop ?? 0)
  }, [])

  useEffect(() => {
    setScrollTop(scrollRef.current?.scrollTop ?? 0)
  }, [viewMode, items.length])

  if (loading && items.length === 0) {
    return (
      <div className="flex min-h-0 flex-1 items-center justify-center">
        <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" aria-label="Loading collection" />
      </div>
    )
  }

  if (!loading && items.length === 0) {
    return (
      <div className="flex min-h-0 flex-1 items-center justify-center text-sm text-muted-foreground">
        No {viewMode} match the current collection filters.
      </div>
    )
  }

  return (
    <div
      ref={scrollRef}
      onScroll={handleScroll}
      className="min-h-0 flex-1 overflow-y-auto overflow-x-hidden bg-muted/10 p-0"
    >
      <div className="relative" style={{ height: Math.max(totalHeight, height), width: layoutWidth }}>
        {visibleItems.map((item, visibleIndex) => {
          const index = startIndex + visibleIndex
          const row = Math.floor(index / columns)
          const column = index % columns
          const position = {
            left: COLLECTION_GRID_GAP + column * (itemWidth + COLLECTION_GRID_GAP),
            top: COLLECTION_GRID_GAP + row * rowHeight,
            width: itemWidth,
            height: itemHeight,
          }

          return viewMode === "products" ? (
            <CollectionProductTile
              key={`product-${item.catalogId}-${index}`}
              product={item as CollectionProductEntry}
              quantityTop={quantityTop}
              showPrice={showPrice}
              selected={selectedCatalogId === item.catalogId}
              onSelect={() => onSelectItem({ item, viewMode })}
              position={position}
            />
          ) : (
            <CollectionCardTile
              key={`card-${item.catalogId}-${index}`}
              card={item as CollectionCardEntry}
              quantityTop={quantityTop}
              showPrice={showPrice}
              selected={selectedCatalogId === item.catalogId}
              onSelect={() => onSelectItem({ item, viewMode })}
              position={position}
            />
          )
        })}
      </div>
    </div>
  )
}
