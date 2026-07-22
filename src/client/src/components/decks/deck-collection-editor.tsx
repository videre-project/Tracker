/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import {
  type MouseEvent as ReactMouseEvent,
  type TouchEvent as ReactTouchEvent,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react"
import { useSearchParams } from "react-router-dom"
import { GripVertical, LayoutGrid, Loader2, PanelRightClose, PanelRightOpen } from "lucide-react"

import { Button } from "@/components/ui/button"
import { CardImage } from "@/components/card-image"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { useDeckIdentifiers } from "@/hooks/use-decks"
import {
  getSortModeColumns,
  groupCardsBySortMode,
  sortCardsBySortMode,
  type SortableCardEntry,
  type SortMode,
  unrollCards,
  useSortableCards,
} from "@/hooks/use-sortable-cards"
import { cn } from "@/lib/utils"
import { COLORLESS_CARD_COLOR, VIDERE_CARD_COLORS } from "@/utils/card-colors"
import { getStackPeekOffset } from "@/utils/card-layout"
import { getManaSymbolSvgPath } from "@/utils/mana-symbols"

const GAP = 16
const COLUMNS = 5
const COLUMN_HEADER_HEIGHT = 40
const DEFAULT_CARD_WIDTH = 214
const DEFAULT_CARD_HEIGHT = 300

interface Position {
  x: number
  y: number
}

interface GridSlot {
  col: number
  row: number
}

interface SheetCardProps {
  index: number
  catalogId: number
  cardWidth: number
  cardHeight: number
  position: Position
  onDragStart: (index: number, e: React.MouseEvent | React.TouchEvent) => void
  isDragging: boolean
  zIndex: number
  diffDelta?: number
  suppressBottomBorder?: boolean
  suppressTopBorder?: boolean
}

function SheetCard({
  index,
  catalogId,
  cardWidth,
  cardHeight,
  position,
  onDragStart,
  isDragging,
  zIndex,
  diffDelta,
  suppressBottomBorder,
  suppressTopBorder,
}: SheetCardProps) {
  const dragTransform = "translateZ(0) scale(1.05)"
  const isAdded = diffDelta != null && diffDelta > 0
  const isRemoved = diffDelta != null && diffDelta < 0

  return (
    <div
      className={cn(
        "absolute cursor-grab select-none rounded-lg overflow-hidden",
        isDragging ? "cursor-grabbing shadow-xl ring-2 ring-primary/50" : "hover:ring-1 hover:ring-primary/30"
      )}
      style={{
        left: position.x,
        top: position.y,
        width: cardWidth,
        height: cardHeight,
        zIndex: isDragging ? 1000 : zIndex,
        // Only promote to compositor layer while actually dragging or during
        // a CSS transition. Keeping will-change on every idle card is very
        // expensive because the browser allocates a separate GPU layer per card.
        willChange: isDragging ? "transform" : "auto",
        transform: isDragging ? dragTransform : "none",
        transition: isDragging
          ? "transform 0.1s ease, box-shadow 0.2s ease"
          : "left 0.15s ease-out, top 0.15s ease-out, box-shadow 0.2s ease"
      }}
      onMouseDown={(e) => onDragStart(index, e)}
      onTouchStart={(e) => onDragStart(index, e)}
    >
      <div className="relative w-full h-full group">
        <CardImage
          catalogId={catalogId}
          alt=""
          width={cardWidth}
          height={cardHeight}
          draggable={false}
          style={{ display: "block", width: cardWidth, height: cardHeight }}
        />

        {diffDelta != null && diffDelta !== 0 && (
          <div
            className={cn(
              "absolute inset-0 pointer-events-none z-30 rounded-lg border-2",
              isAdded
                ? "border-emerald-400 shadow-[0_0_12px_rgba(16,185,129,0.4)]"
                : "border-rose-400 shadow-[0_0_12px_rgba(244,63,94,0.4)]",
              suppressTopBorder && "border-t-0 rounded-t-none",
              suppressBottomBorder && "border-b-0 rounded-b-none"
            )}
          />
        )}

        <div className={`
          absolute top-2 right-2 p-1 rounded
          bg-black/40 backdrop-blur-sm
          opacity-0 group-hover:opacity-100 transition-opacity duration-200
        `}>
          <GripVertical className="w-3 h-3 text-white/70" />
        </div>
      </div>
    </div>
  )
}

function SkeletonCard({ cardWidth, cardHeight, position, zIndex }: { cardWidth: number, cardHeight: number, position: Position, zIndex: number }) {
  return (
    <div
      className="absolute overflow-hidden rounded-lg"
      style={{
        left: position.x,
        top: position.y,
        width: cardWidth,
        height: cardHeight,
        zIndex,
        transition: "left 0.15s ease-out, top 0.15s ease-out",
        backgroundImage: `url(/m15-frame.png)`,
        backgroundSize: "cover",
        backgroundPosition: "center",
        backgroundColor: "hsl(var(--muted))",
        filter: "sepia(20%) saturate(150%) hue-rotate(190deg) brightness(0.7)"
      }}
    >
      <div
        className="absolute inset-0 bg-gradient-to-r from-transparent via-white/10 to-transparent animate-shimmer pointer-events-none"
        style={{ backgroundSize: "200% 100%", borderRadius: 8 }}
      />
    </div>
  )
}

function getSlotPosition(col: number, row: number, cardWidth: number, cardHeight: number): Position {
  const offsetPerCard = getStackPeekOffset(cardHeight)
  return {
    x: col * (cardWidth + GAP) + GAP,
    y: row * offsetPerCard + GAP + COLUMN_HEADER_HEIGHT
  }
}

function findNearestSlot(pos: Position, cardWidth: number, cardHeight: number, columns: number): GridSlot {
  const offsetPerCard = getStackPeekOffset(cardHeight)
  const colWidth = cardWidth + GAP
  const col = Math.max(0, Math.min(columns - 1, Math.round((pos.x - GAP) / colWidth)))
  const row = Math.max(0, Math.round((pos.y - GAP) / offsetPerCard))
  return { col, row }
}

function getPileHeight(cardsInColumn: number, cardHeight: number): number {
  if (cardsInColumn === 0) return 0
  const offsetPerCard = getStackPeekOffset(cardHeight)
  return cardHeight + offsetPerCard * (cardsInColumn - 1)
}

function calculateDeckStats(cards: SortableCardEntry[]) {
  const totalCards = cards.reduce((acc, c) => acc + c.quantity, 0)

  let creatures = 0
  let lands = 0
  let totalCmc = 0
  let nonLandCount = 0

  cards.forEach(c => {
    if (c.types.includes('Creature')) creatures += c.quantity
    if (c.types.includes('Land')) lands += c.quantity
    if (!c.types.includes('Land')) {
      nonLandCount += c.quantity
      totalCmc += c.cmc * c.quantity
    }
  })

  const spells = totalCards - creatures - lands
  const avgCmc = nonLandCount > 0
    ? (totalCmc / nonLandCount).toFixed(2)
    : '0'

  return { totalCards, creatures, lands, spells, avgCmc }
}

interface DeckStatsProps {
  mainboard: SortableCardEntry[]
  sideboard: SortableCardEntry[]
}

function DeckStats({ mainboard, sideboard }: DeckStatsProps) {
  const mainStats = useMemo(() => calculateDeckStats(mainboard), [mainboard])
  const sideStats = useMemo(() => calculateDeckStats(sideboard), [sideboard])

  return (
    <Card className="border-sidebar-border/60">
       <CardContent className="px-4 py-2 bg-muted/20 flex gap-8">
          <table className="text-xs text-muted-foreground">
            <tbody>
              <tr>
                 <td className="pr-2 font-semibold text-foreground">Main ({mainStats.totalCards})</td>
                <td className="pr-2">Creatures: <span className="font-medium text-foreground">{mainStats.creatures}</span></td>
                <td className="pr-2">Spells: <span className="font-medium text-foreground">{mainStats.spells}</span></td>
                <td className="pr-2">Lands: <span className="font-medium text-foreground">{mainStats.lands}</span></td>
                <td>Avg CMC: <span className="font-medium text-foreground">{mainStats.avgCmc}</span></td>
              </tr>
            </tbody>
          </table>

          {sideStats.totalCards > 0 && (
            <div className="flex items-center gap-2 text-xs text-muted-foreground border-l pl-4 border-border/50">
               <span className="font-semibold text-foreground">Side ({sideStats.totalCards})</span>
            </div>
          )}
       </CardContent>
    </Card>
  )
}

export type DiffMapEntry = {
  delta: number
  zone: string
  name: string
  cmc?: number
  colors?: string[]
  types?: string[]
  rarity?: string
}

interface DeckGridProps {
  title: string
  cards: SortableCardEntry[]
  allCards?: SortableCardEntry[]
  loading: boolean
  sortMode: SortMode
  collapsed?: boolean
  forceScale?: number
  onNaturalWidthChange?: (width: number) => void
  diffMap?: Map<number, DiffMapEntry>
}

function getEffectiveCardsForZone(
  cards: SortableCardEntry[],
  allCards: SortableCardEntry[] = [],
  zone: 'Mainboard' | 'Sideboard',
  diffMap?: Map<number, DiffMapEntry>
): SortableCardEntry[] {
  if (!diffMap || diffMap.size === 0) return cards

  const ghostEntries: SortableCardEntry[] = []
  let nextGhostIndex = 900000

  diffMap.forEach((diff, catalogId) => {
    if (diff.delta < 0) {
      const isSide = diff.zone?.toLowerCase().includes("side")
      const isTargetSide = zone === 'Sideboard'
      if (isSide === isTargetSide) {
        const ghostCount = Math.abs(diff.delta)
        const template = allCards.find(c => c.catalogId === catalogId) || cards.find(c => c.catalogId === catalogId)

        for (let i = 0; i < ghostCount; i++) {
          const types = (template?.types && template.types.length > 0)
            ? template.types
            : ((diff.types && diff.types.length > 0) ? diff.types : ["Unknown"])

          ghostEntries.push({
            index: nextGhostIndex++,
            originalIndex: nextGhostIndex,
            catalogId,
            name: diff.name,
            quantity: 1,
            cmc: template?.cmc ?? diff.cmc ?? 0,
            colors: template?.colors ?? diff.colors ?? [],
            types,
            rarity: template?.rarity ?? diff.rarity ?? "common",
            zone,
          })
        }
      }
    }
  })

  return ghostEntries.length > 0 ? [...cards, ...ghostEntries] : cards
}

function DeckGrid({
  title,
  cards,
  allCards,
  loading,
  sortMode,
  collapsed = false,
  forceScale,
  onNaturalWidthChange,
  diffMap,
}: DeckGridProps) {
  const scrollContainerRef = useRef<HTMLDivElement>(null)

  // Use a ref for drag state so mousemove handlers never need to be re-attached.
  // Only state that React needs to re-render is extracted separately.
  const dragStateRef = useRef<{
    index: number
    startSlot: GridSlot
    startMousePos: Position
    basePos: Position
  } | null>(null)
  const [draggingIndex, setDraggingIndex] = useState<number | null>(null)
  const [dragCurrentPos, setDragCurrentPos] = useState<Position>({ x: 0, y: 0 })

  const zone = title.toLowerCase().includes("side") ? 'Sideboard' : 'Mainboard'
  const effectiveCards = useMemo(() => {
    return getEffectiveCardsForZone(cards, allCards, zone, diffMap)
  }, [cards, allCards, zone, diffMap])

  const unrolledCards = useMemo(() => {
    const sorted = sortCardsBySortMode(effectiveCards, sortMode)
    return unrollCards(sorted)
  }, [effectiveCards, sortMode])

  const groupedCards = useMemo(() => {
    return groupCardsBySortMode(unrolledCards, sortMode)
  }, [unrolledCards, sortMode])

  const actualColumns = useMemo(() => {
    if (collapsed) return 1
    return getSortModeColumns(sortMode, unrolledCards).length || COLUMNS
  }, [collapsed, sortMode, unrolledCards])

  const [draggedSlots, setDraggedSlots] = useState<Map<number, GridSlot> | null>(null)

  const defaultSlots = useMemo(() => {
    const newSlots = new Map<number, GridSlot>()
    if (collapsed) {
      unrolledCards.forEach((card, index) => {
        newSlots.set(card.index, { col: 0, row: index })
      })
    } else {
      let currentColumn = 0
      groupedCards.forEach((pile) => {
        pile.forEach((card, rowIndex) => {
          newSlots.set(card.index, { col: currentColumn, row: rowIndex })
        })
        currentColumn++
      })
    }
    return newSlots
  }, [collapsed, groupedCards, unrolledCards])

  const cardSlots = draggedSlots || defaultSlots

  const cardsPerColumn = useMemo(() => {
    const counts = new Array(actualColumns).fill(0)

    if (collapsed) {
      counts[0] = unrolledCards.length
      return counts
    }

    cardSlots.forEach((slot) => {
      if (slot.col >= 0 && slot.col < actualColumns) {
        counts[slot.col] = Math.max(counts[slot.col], slot.row + 1)
      }
    })
    return counts
  }, [actualColumns, cardSlots, collapsed, unrolledCards.length])

  const effectiveCardSlots = useMemo(() => {
    if (draggingIndex == null) return cardSlots

    const offsetPerCard = getStackPeekOffset(DEFAULT_CARD_HEIGHT)
    const colWidth = DEFAULT_CARD_WIDTH + GAP
    const hoverCol = Math.max(0, Math.min(actualColumns - 1, Math.round((dragCurrentPos.x - GAP) / colWidth)))
    const hoverRowRaw = Math.max(0, Math.round((dragCurrentPos.y - GAP - COLUMN_HEADER_HEIGHT) / offsetPerCard))

    const next = new Map(cardSlots)
    const srcSlot = cardSlots.get(draggingIndex)
    if (!srcSlot) return cardSlots

    const targetColCards = Array.from(cardSlots.entries())
      .filter(([idx, s]) => idx !== draggingIndex && s.col === hoverCol)
      .sort((a, b) => a[1].row - b[1].row)

    const hoverRow = Math.min(hoverRowRaw, targetColCards.length)

    targetColCards.forEach(([idx], rIndex) => {
      const effectiveRow = rIndex >= hoverRow ? rIndex + 1 : rIndex
      next.set(idx, { col: hoverCol, row: effectiveRow })
    })

    if (srcSlot.col !== hoverCol) {
      const srcColCards = Array.from(cardSlots.entries())
        .filter(([idx, s]) => idx !== draggingIndex && s.col === srcSlot.col)
        .sort((a, b) => a[1].row - b[1].row)

      srcColCards.forEach(([idx], rIndex) => {
        next.set(idx, { col: srcSlot.col, row: rIndex })
      })
    }

    return next
  }, [actualColumns, cardSlots, draggingIndex, dragCurrentPos])

  const cardPositions = useMemo(() => {
    const positions = new Map<number, Position>()
    const cardWidth = DEFAULT_CARD_WIDTH
    const cardHeight = DEFAULT_CARD_HEIGHT

    unrolledCards.forEach((card) => {
      const slot = effectiveCardSlots.get(card.index)
      if (slot) {
        positions.set(card.index, getSlotPosition(slot.col, slot.row, cardWidth, cardHeight))
      }
    })

    return positions
  }, [effectiveCardSlots, unrolledCards])

  // Build a fast O(1) lookup from card index → card, so we don't scan
  // unrolledCards inside the render map.
  const cardByIndex = useMemo(() => {
    const m = new Map<number, (typeof unrolledCards)[number]>()
    unrolledCards.forEach(c => m.set(c.index, c))
    return m
  }, [unrolledCards])

  // Build a fast O(1) slot-neighbour lookup: (col, row) → cardIndex.
  // Used for diff border suppression so we don't do O(n) Array.from().find()
  // per card during render.
  const slotKey = (col: number, row: number) => `${col}|${row}`
  const slotToCardIndex = useMemo(() => {
    const m = new Map<string, number>()
    cardSlots.forEach((slot, idx) => m.set(slotKey(slot.col, slot.row), idx))
    return m
  }, [cardSlots])

  const cardWidth = DEFAULT_CARD_WIDTH
  const cardHeight = DEFAULT_CARD_HEIGHT

  const naturalWidth = cardWidth * actualColumns + GAP * (actualColumns + 1)
  const maxPileHeight = Math.max(...cardsPerColumn.map(count => getPileHeight(count, cardHeight)), cardHeight)
  const maxY = maxPileHeight + GAP * 2 + COLUMN_HEADER_HEIGHT

  useEffect(() => {
    onNaturalWidthChange?.(naturalWidth)
  }, [naturalWidth, onNaturalWidthChange])

  const scale = forceScale ?? 1

  const scaleToPixel = useCallback((value: number) => (
    value === 0 ? 0 : Math.max(1, Math.round(value * scale))
  ), [scale])

  const scaledWidth = scaleToPixel(naturalWidth)
  const scaledHeight = scaleToPixel(maxY)
  const scaledCardWidth = scaleToPixel(cardWidth)
  const scaledCardHeight = scaleToPixel(cardHeight)

  // Memoize column labels so they aren't recomputed on every render.
  const columnLabels = useMemo(
    () => getSortModeColumns(sortMode, unrolledCards),
    [sortMode, unrolledCards]
  )

  const cardInstanceKeys = useMemo(() => {
    const keys = new Map<number, string>()
    const catalogCopyCounts = new Map<number, number>()

    unrolledCards.forEach((card) => {
      const count = catalogCopyCounts.get(card.catalogId) ?? 0
      catalogCopyCounts.set(card.catalogId, count + 1)
      keys.set(card.index, `${card.catalogId}-${count}`)
    })

    return keys
  }, [unrolledCards])

  const cardOrder = useMemo(() => {
    return unrolledCards.map((c) => c.index)
  }, [unrolledCards])

  // Stable ref so mousemove/mouseup handlers are attached exactly once.
  const cardPositionsRef = useRef(cardPositions)
  cardPositionsRef.current = cardPositions
  const cardSlotsRef = useRef(cardSlots)
  cardSlotsRef.current = cardSlots
  const scaleRef = useRef(scale)
  scaleRef.current = scale
  const actualColumnsRef = useRef(actualColumns)
  actualColumnsRef.current = actualColumns

  const handleDragStart = useCallback((index: number, e: ReactMouseEvent | ReactTouchEvent) => {
    const clientX = 'touches' in e ? e.touches[0].clientX : e.clientX
    const clientY = 'touches' in e ? e.touches[0].clientY : e.clientY
    const startSlot = cardSlotsRef.current.get(index)
    if (!startSlot) return

    const basePos = cardPositionsRef.current.get(index) || { x: 0, y: 0 }

    dragStateRef.current = {
      index,
      startSlot,
      startMousePos: { x: clientX, y: clientY },
      basePos,
    }
    setDraggingIndex(index)
    setDragCurrentPos(basePos)
  }, [])

  // Attach listeners once and read state from refs — no re-attachment on every
  // mousemove, which was causing the listener churn.
  useEffect(() => {
    const onMove = (e: MouseEvent | TouchEvent) => {
      if (!dragStateRef.current) return
      const { startMousePos, basePos } = dragStateRef.current
      const clientX = 'touches' in e ? e.touches[0].clientX : e.clientX
      const clientY = 'touches' in e ? e.touches[0].clientY : e.clientY
      const s = scaleRef.current
      setDragCurrentPos({
        x: basePos.x + (clientX - startMousePos.x) / s,
        y: basePos.y + (clientY - startMousePos.y) / s,
      })
    }

    const onEnd = () => {
      const ds = dragStateRef.current
      if (!ds) return

      const pos = dragStateRef.current ? {
        x: ds.basePos.x,
        y: ds.basePos.y,
      } : { x: 0, y: 0 }

      // Reconstruct current drag position from the last setDragCurrentPos value
      // by reading it from the ref we'll update below.
      const currentPos = currentPosRef.current
      const cols = actualColumnsRef.current
      const slots = cardSlotsRef.current

      const offsetPerCard = getStackPeekOffset(DEFAULT_CARD_HEIGHT)
      const colWidth = DEFAULT_CARD_WIDTH + GAP
      const targetCol = Math.max(0, Math.min(cols - 1, Math.round((currentPos.x - GAP) / colWidth)))
      const targetRowRaw = Math.max(0, Math.round((currentPos.y - GAP - COLUMN_HEADER_HEIGHT) / offsetPerCard))

      setDraggedSlots(() => {
        const next = new Map(slots)
        const srcSlot = slots.get(ds.index)
        if (!srcSlot) return slots

        const targetColCards = Array.from(next.entries())
          .filter(([idx, s]) => idx !== ds.index && s.col === targetCol)
          .sort((a, b) => a[1].row - b[1].row)

        const targetRow = Math.min(targetRowRaw, targetColCards.length)
        targetColCards.splice(targetRow, 0, [ds.index, { col: targetCol, row: targetRow }])

        targetColCards.forEach(([idx], rIndex) => {
          next.set(idx, { col: targetCol, row: rIndex })
        })

        if (srcSlot.col !== targetCol) {
          const srcColCards = Array.from(next.entries())
            .filter(([idx, s]) => idx !== ds.index && s.col === srcSlot.col)
            .sort((a, b) => a[1].row - b[1].row)

          srcColCards.forEach(([idx], rIndex) => {
            next.set(idx, { col: srcSlot.col, row: rIndex })
          })
        }

        return next
      })

      dragStateRef.current = null
      setDraggingIndex(null)
    }

    window.addEventListener('mousemove', onMove)
    window.addEventListener('mouseup', onEnd)
    window.addEventListener('touchmove', onMove)
    window.addEventListener('touchend', onEnd)

    return () => {
      window.removeEventListener('mousemove', onMove)
      window.removeEventListener('mouseup', onEnd)
      window.removeEventListener('touchmove', onMove)
      window.removeEventListener('touchend', onEnd)
    }
  // Empty deps: attach once, read everything through refs.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Keep a ref to the latest dragCurrentPos so the onEnd handler can read it.
  const currentPosRef = useRef(dragCurrentPos)
  currentPosRef.current = dragCurrentPos

  if (!loading && cards.length === 0) return null

  return (
    <div
      ref={scrollContainerRef}
      className="flex-1 overflow-y-scroll overflow-x-hidden bg-muted/30 relative min-h-0"
    >
      <div style={{ height: scaledHeight, position: "relative" }}>
        <div
          className="absolute top-0 left-0 origin-top-left"
          style={{
            width: scaledWidth, height: scaledHeight, touchAction: draggingIndex != null ? "none" : "auto"
          }}
        >
          {cardSlots.size > 0 && (
            <div
              className="absolute top-0 left-0 flex origin-top-left"
              style={{ gap: GAP, paddingLeft: GAP, paddingTop: GAP / 2, transform: `scale(${scale})` }}
            >
              {cardsPerColumn.map((count, col) => {
                if (collapsed && col === 0) {
                  return (
                    <div key={col} className="flex flex-col items-center justify-center text-xs font-medium text-muted-foreground bg-muted/50 rounded-md px-1"
                         style={{ width: cardWidth, height: COLUMN_HEADER_HEIGHT - GAP / 2 }}>
                      <span className="truncate max-w-full">{title}</span>
                      <span className="text-[10px] opacity-60">{count}</span>
                    </div>
                  )
                }

                const label = columnLabels[col] || ''
                const isMana = sortMode === 'colors' && [...VIDERE_CARD_COLORS, COLORLESS_CARD_COLOR].some(color => color === label)
                return (
                  <div key={col} className="flex flex-col items-center justify-center text-xs font-medium text-muted-foreground bg-muted/50 rounded-md px-1"
                       style={{ width: cardWidth, height: COLUMN_HEADER_HEIGHT - GAP / 2 }}>
                    <span className="truncate max-w-full flex items-center gap-1" title={label}>
                      {isMana ? <img src={getManaSymbolSvgPath(label) ?? undefined} alt={label} className="h-4 w-4"/> : label}
                    </span>
                    <span className="text-[10px] opacity-60">{count}</span>
                  </div>
                )
              })}
            </div>
          )}

          {cardOrder.map((cardIndex) => {
            // O(1) lookup via pre-built Map instead of O(n) find() per card.
            const card = cardByIndex.get(cardIndex)

            const slot = cardSlots.get(cardIndex)
            const zIndex = slot?.row ?? 0

            // Position: if this card is being dragged, use live drag position.
            const rawPos = draggingIndex === cardIndex
              ? dragCurrentPos
              : (cardPositions.get(cardIndex) ?? { x: 0, y: 0 })
            const pos = {
              x: scaleToPixel(rawPos.x),
              y: scaleToPixel(rawPos.y),
            }

            if (card) {
              const diff = diffMap?.get(card.catalogId)
              let suppressBottomBorder = false
              let suppressTopBorder = false

              // O(1) neighbour lookups via pre-built slot map.
              if (slot && diff?.delta) {
                const belowIdx = slotToCardIndex.get(slotKey(slot.col, slot.row + 1))
                if (belowIdx != null) {
                  const cardBelowDiff = diffMap?.get(cardByIndex.get(belowIdx)?.catalogId ?? -1)
                  if (cardBelowDiff?.delta != null && Math.sign(cardBelowDiff.delta) === Math.sign(diff.delta)) {
                    suppressBottomBorder = true
                  }
                }

                const aboveIdx = slotToCardIndex.get(slotKey(slot.col, slot.row - 1))
                if (aboveIdx != null) {
                  const cardAboveDiff = diffMap?.get(cardByIndex.get(aboveIdx)?.catalogId ?? -1)
                  if (cardAboveDiff?.delta != null && Math.sign(cardAboveDiff.delta) === Math.sign(diff.delta)) {
                    suppressTopBorder = true
                  }
                }
              }

              const instanceKey = cardInstanceKeys.get(cardIndex) ?? cardIndex

              return (
                <SheetCard
                  key={instanceKey}
                  index={cardIndex}
                  catalogId={card.catalogId}
                  cardWidth={scaledCardWidth}
                  cardHeight={scaledCardHeight}
                  position={pos}
                  onDragStart={handleDragStart}
                  isDragging={draggingIndex === cardIndex}
                  zIndex={zIndex}
                  diffDelta={diff?.delta}
                  suppressBottomBorder={suppressBottomBorder}
                  suppressTopBorder={suppressTopBorder}
                />
              )
            }
            return <SkeletonCard key={cardIndex} cardWidth={scaledCardWidth} cardHeight={scaledCardHeight} position={pos} zIndex={zIndex} />
          })}
        </div>
      </div>
    </div>
  )
}

export interface DeckCollectionEditorProps {
  deckRevisionId?: string
  overrideCards?: SortableCardEntry[]
  className?: string
  editorTitle?: string
  hideDeckSelector?: boolean
  showDeckStats?: boolean
  showFixedDeckLabel?: boolean
  hideEditorHeader?: boolean
  sortMode?: SortMode
  onSortModeChange?: (mode: SortMode) => void
  sideboardCollapsed?: boolean
  onSideboardCollapsedChange?: (collapsed: boolean) => void
  onSortableCardsChange?: (cards: SortableCardEntry[], loading: boolean) => void
  diffMap?: Map<number, DiffMapEntry>
}

export function DeckCollectionEditor({
  deckRevisionId: routeDeckRevisionId,
  overrideCards,
  className,
  editorTitle = "Deck",
  hideDeckSelector = false,
  showDeckStats = true,
  showFixedDeckLabel = true,
  hideEditorHeader = false,
  sortMode: controlledSortMode,
  onSortModeChange,
  sideboardCollapsed: controlledSideboardCollapsed,
  onSideboardCollapsedChange,
  onSortableCardsChange,
  diffMap,
}: DeckCollectionEditorProps = {}) {
  const [searchParams, setSearchParams] = useSearchParams()
  const deckFromSearch = searchParams.get("deck") || ""

  const [selectedDeckRevisionId, setSelectedDeckRevisionId] = useState<string>(
    routeDeckRevisionId || deckFromSearch
  )

  const [internalSortMode, setInternalSortMode] = useState<SortMode>("cmc")
  const sortMode = controlledSortMode ?? internalSortMode

  const [internalSideboardCollapsed, setInternalSideboardCollapsed] = useState(false)
  const isSideboardCollapsed = controlledSideboardCollapsed ?? internalSideboardCollapsed

  const { cards: fetchedCards, loading: sortableLoading, fetchSortableCards, reset: resetSortable } = useSortableCards()
  const { identifiers: decks } = useDeckIdentifiers()

  const allCards = useMemo(() => {
    if (overrideCards && overrideCards.length > 0) return overrideCards
    return fetchedCards
  }, [overrideCards, fetchedCards])

  const [containerWidth, setContainerWidth] = useState(0)
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!containerRef.current) return
    const observer = new ResizeObserver(() => {
      requestAnimationFrame(() => {
        if (!containerRef.current) return
        setContainerWidth(containerRef.current.clientWidth)
      })
    })
    observer.observe(containerRef.current)
    return () => observer.disconnect()
  }, [])

  useEffect(() => {
    const targetId = routeDeckRevisionId || deckFromSearch
    if (targetId && targetId !== selectedDeckRevisionId) {
      setSelectedDeckRevisionId(targetId)
    }
  }, [deckFromSearch, routeDeckRevisionId, selectedDeckRevisionId])

  const handleDeckChange = useCallback((revisionId: string) => {
    if (routeDeckRevisionId) return

    setSelectedDeckRevisionId(revisionId)
    setSearchParams(prev => {
      const next = new URLSearchParams(prev)
      if (revisionId) next.set("deck", revisionId)
      else next.delete("deck")
      return next
    }, { replace: true })
  }, [routeDeckRevisionId, setSearchParams])

  const handleSortModeChange = useCallback((mode: SortMode) => {
    onSortModeChange?.(mode)

    if (controlledSortMode === undefined) {
      setInternalSortMode(mode)
    }
  }, [controlledSortMode, onSortModeChange])

  const toggleSideboard = useCallback(() => {
    const nextCollapsed = !isSideboardCollapsed
    onSideboardCollapsedChange?.(nextCollapsed)

    if (controlledSideboardCollapsed === undefined) {
      setInternalSideboardCollapsed(nextCollapsed)
    }
  }, [controlledSideboardCollapsed, isSideboardCollapsed, onSideboardCollapsedChange])

  const selectedDeckName = useMemo(() => {
    if (!selectedDeckRevisionId) return ""
    return decks.find(
      deck => deck.revisionId.toString() === selectedDeckRevisionId
    )?.name || ""
  }, [decks, selectedDeckRevisionId])

  const mainboardCards = useMemo(() => allCards.filter(c => c.zone === 'Mainboard' || !c.zone), [allCards])
  const sideboardCards = useMemo(() => allCards.filter(c => c.zone === 'Sideboard'), [allCards])

  useEffect(() => {
    onSortableCardsChange?.(allCards, sortableLoading)
  }, [allCards, onSortableCardsChange, sortableLoading])

  const initializedRef = useRef<string | null>(null)
  const fetchAbortRef = useRef<AbortController | null>(null)

  useEffect(() => {
    const fetchKey = `fetch-${selectedDeckRevisionId || "none"}`
    if (initializedRef.current === fetchKey) return
    initializedRef.current = fetchKey

    fetchAbortRef.current?.abort()
    const abortController = new AbortController()
    fetchAbortRef.current = abortController

    if (!selectedDeckRevisionId) {
      resetSortable()
      return
    }

    const fetchParallel = async () => {
      console.log(`[Collection] Fetching data for ${selectedDeckName || selectedDeckRevisionId} (${selectedDeckRevisionId})...`)
      if (abortController.signal.aborted) return

      try {
        const deckId = selectedDeckRevisionId
        await fetchSortableCards(selectedDeckName, deckId || undefined)
      } catch (e) {
        if ((e as Error).name !== 'AbortError') throw e
      }
    }
    fetchParallel()

    return () => abortController.abort()
  }, [fetchSortableCards, resetSortable, selectedDeckName, selectedDeckRevisionId])

  const effectiveMainCards = useMemo(() => {
    return getEffectiveCardsForZone(mainboardCards, allCards, 'Mainboard', diffMap)
  }, [mainboardCards, allCards, diffMap])
  const mainUnrolledCards = useMemo(() => unrollCards(effectiveMainCards), [effectiveMainCards])
  const mainActualColumns = useMemo(() => {
    return getSortModeColumns(sortMode, mainUnrolledCards).length || COLUMNS
  }, [sortMode, mainUnrolledCards])
  const mainNaturalWidth = DEFAULT_CARD_WIDTH * mainActualColumns + GAP * (mainActualColumns + 1)

  const effectiveSideCards = useMemo(() => {
    return getEffectiveCardsForZone(sideboardCards, allCards, 'Sideboard', diffMap)
  }, [sideboardCards, allCards, diffMap])
  const sideUnrolledCards = useMemo(() => unrollCards(effectiveSideCards), [effectiveSideCards])
  const sideActualColumns = useMemo(() => {
    if (isSideboardCollapsed) return 1
    return getSortModeColumns(sortMode, sideUnrolledCards).length || COLUMNS
  }, [isSideboardCollapsed, sortMode, sideUnrolledCards])
  const sideNaturalWidth = sideboardCards.length > 0
    ? (DEFAULT_CARD_WIDTH * sideActualColumns + GAP * (sideActualColumns + 1))
    : 0

  const totalNatural = mainNaturalWidth + sideNaturalWidth

  const sharedScale = useMemo(() => {
     if (containerWidth === 0 || totalNatural === 0) return 1
     return Math.min(1, containerWidth / totalNatural)
  }, [containerWidth, totalNatural])

  const sideContainerWidth = sideboardCards.length > 0 ? sideNaturalWidth * sharedScale : 0

  return (
    <div className={cn("flex flex-col h-full p-4 gap-6 min-w-0 overflow-hidden", className)}>
      {showDeckStats && allCards.length > 0 && (
        <DeckStats mainboard={mainboardCards} sideboard={sideboardCards} />
      )}

      <Card className="border-sidebar-border/60 flex-1 flex flex-col min-h-0 overflow-hidden">
        {!hideEditorHeader && (
          <CardHeader className="p-3 flex flex-row items-center gap-3 space-y-0 border-b border-border/50 flex-shrink-0">
            <CardTitle className="text-base font-medium flex items-center gap-2">
              <LayoutGrid className="w-4 h-4" />
              {editorTitle}
            </CardTitle>

            <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
              <span>Main</span>
              <span className="font-medium text-foreground">{mainboardCards.reduce((a, c) => a + c.quantity, 0)}</span>
              {sideboardCards.length > 0 && (
                <>
                  <span className="opacity-40">/</span>
                  <span>Side</span>
                  <span className="font-medium text-foreground">{sideboardCards.reduce((a, c) => a + c.quantity, 0)}</span>
                </>
              )}
            </div>

            {hideDeckSelector && showFixedDeckLabel ? (
              <div className="ml-auto flex min-w-0 items-center gap-1.5 text-xs">
                <span className="text-muted-foreground">Deck:</span>
                <span className="max-w-56 truncate font-medium text-foreground">
                  {selectedDeckName || "Loading deck..."}
                </span>
              </div>
            ) : !hideDeckSelector ? (
              <div className="flex items-center gap-2 ml-auto">
                <span className="text-xs text-muted-foreground">Deck:</span>
                <Select value={selectedDeckRevisionId} onValueChange={handleDeckChange}>
                  <SelectTrigger className="h-7 min-w-[140px] text-xs">
                    <SelectValue placeholder="Select a deck..." />
                  </SelectTrigger>
                  <SelectContent>
                    {decks.map((deck) => (
                      <SelectItem
                        key={deck.revisionId}
                        value={deck.revisionId.toString()}
                      >
                        {deck.format} - {deck.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            ) : null}

            <div className={cn("flex items-center gap-2", hideDeckSelector && !showFixedDeckLabel && "ml-auto")}>
              <span className="text-xs text-muted-foreground">Sort:</span>
              <Select value={sortMode} onValueChange={(v) => handleSortModeChange(v as SortMode)}>
                <SelectTrigger className="h-7 w-[90px] text-xs"><SelectValue placeholder="Sort..." /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="cmc">CMC</SelectItem>
                  <SelectItem value="colors">Colors</SelectItem>
                  <SelectItem value="types">Types</SelectItem>
                  <SelectItem value="rarity">Rarity</SelectItem>
                </SelectContent>
              </Select>
            </div>

            {sortableLoading && <Loader2 className="w-4 h-4 animate-spin text-muted-foreground" />}

            {sideboardCards.length > 0 && (
              <Button variant="ghost" size="icon" className="h-8 w-8" onClick={toggleSideboard}>
                {isSideboardCollapsed ? <PanelRightOpen className="h-4 w-4" /> : <PanelRightClose className="h-4 w-4" />}
              </Button>
            )}
          </CardHeader>
        )}

        <div ref={containerRef} className="flex-1 flex min-h-0 relative">
          {allCards.length === 0 && sortableLoading && (
            <div className="absolute inset-0 flex items-center justify-center z-50 bg-background/50 backdrop-blur-[1px]">
              <div className="flex flex-col items-center gap-3">
                <Loader2 className="w-8 h-8 animate-spin text-muted-foreground" />
                <span className="text-sm text-muted-foreground">Rendering deck...</span>
              </div>
            </div>
          )}
          <div className="flex-1 min-w-0 flex flex-col min-h-0">
            {!(selectedDeckRevisionId || routeDeckRevisionId || deckFromSearch || allCards.length > 0) && (
              <div className="flex-1 flex items-center justify-center">
                <span className="text-sm text-muted-foreground">Select a deck to view</span>
              </div>
            )}
            {(selectedDeckRevisionId || routeDeckRevisionId || deckFromSearch || allCards.length > 0) && (
              <DeckGrid
                title="Mainboard"
                cards={mainboardCards}
                allCards={allCards}
                loading={sortableLoading}
                sortMode={sortMode}
                forceScale={sharedScale}
                diffMap={diffMap}
              />
            )}
          </div>

          {sideboardCards.length > 0 && (
            <div
              className="flex-shrink-0 flex flex-col min-h-0 border-l border-border/50 transition-[width] duration-300 ease-in-out"
              style={{ width: sideContainerWidth }}
            >
              <DeckGrid
                title="Sideboard"
                cards={sideboardCards}
                allCards={allCards}
                loading={sortableLoading}
                sortMode={sortMode}
                collapsed={isSideboardCollapsed}
                forceScale={sharedScale}
                diffMap={diffMap}
              />
            </div>
          )}
        </div>
      </Card>
    </div>
  )
}
