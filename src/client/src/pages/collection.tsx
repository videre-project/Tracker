"use client"

import { useState, useEffect, useRef, useCallback, useMemo } from "react"
import { useDeckSheet, getCardCropPosition, DeckSheetData as SheetData } from "@/hooks/use-deck-sheet"
import { useSortableCards, groupCardsBySortMode, getSortModeColumns, unrollCards, SortMode, SortableCardEntry } from "@/hooks/use-sortable-cards"
import { useDeckIdentifiers } from "@/hooks/use-decks"
import { Loader2, GripVertical, LayoutGrid, PanelRightClose, PanelRightOpen } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { cn } from "@/lib/utils"

const GAP = 16
const COLUMNS = 5
const OVERLAP = 0.88
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
  sheetIndex: number
  sheetUrl: string
  columns: number
  cardWidth: number
  cardHeight: number
  position: Position
  onDragStart: (index: number, e: React.MouseEvent | React.TouchEvent) => void
  isDragging: boolean
  zIndex: number
}

function SheetCard({
  index,
  sheetIndex,
  sheetUrl,
  columns,
  cardWidth,
  cardHeight,
  position,
  onDragStart,
  isDragging,
  zIndex
}: SheetCardProps) {
  const cropPos = getCardCropPosition(sheetIndex, columns, cardWidth, cardHeight)

  return (
    <div
      className={cn(
        "absolute cursor-grab select-none rounded-lg overflow-hidden",
        isDragging ? "cursor-grabbing shadow-xl ring-2 ring-primary/50 scale-105" : "hover:shadow-lg hover:ring-1 hover:ring-primary/30"
      )}
      style={{
        left: position.x,
        top: position.y,
        width: cardWidth,
        height: cardHeight,
        zIndex: isDragging ? 1000 : zIndex,
        transform: isDragging ? "scale(1.05)" : "scale(1)",
        transition: isDragging
          ? "transform 0.1s ease, box-shadow 0.2s ease"
          : "left 0.15s ease-out, top 0.15s ease-out, transform 0.2s ease, box-shadow 0.2s ease"
      }}
      onMouseDown={(e) => onDragStart(index, e)}
      onTouchStart={(e) => onDragStart(index, e)}
    >
      <div className="relative w-full h-full group">
        <div
          style={{
            width: cardWidth,
            height: cardHeight,
            backgroundImage: `url(${sheetUrl})`,
            backgroundPosition: `-${cropPos.x}px -${cropPos.y}px`,
            backgroundSize: "auto"
          }}
        />
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
  const offsetPerCard = cardHeight * (1 - OVERLAP)
  return {
    x: col * (cardWidth + GAP) + GAP,
    y: row * offsetPerCard + GAP + COLUMN_HEADER_HEIGHT
  }
}

function findNearestSlot(pos: Position, cardWidth: number, cardHeight: number, columns: number): GridSlot {
  const offsetPerCard = cardHeight * (1 - OVERLAP)
  const colWidth = cardWidth + GAP
  const col = Math.max(0, Math.min(columns - 1, Math.round((pos.x - GAP) / colWidth)))
  const row = Math.max(0, Math.round((pos.y - GAP) / offsetPerCard))
  return { col, row }
}

function getPileHeight(cardsInColumn: number, cardHeight: number): number {
  if (cardsInColumn === 0) return 0
  const offsetPerCard = cardHeight * (1 - OVERLAP)
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

interface DeckGridProps {
  title: string
  cards: SortableCardEntry[]
  data: SheetData | null
  loading: boolean
  sortMode: SortMode
  collapsed?: boolean
  forceScale?: number
  onNaturalWidthChange?: (width: number) => void
}

function DeckGrid({
  title,
  cards,
  data,
  loading,
  sortMode,
  collapsed = false,
  forceScale,
  onNaturalWidthChange
}: DeckGridProps) {
  const [imageReady, setImageReady] = useState(false)
  const scrollContainerRef = useRef<HTMLDivElement>(null)
  
  const [cardSlots, setCardSlots] = useState<Map<number, GridSlot>>(new Map())
  const [dragState, setDragState] = useState<{
    index: number
    startSlot: GridSlot
    startMousePos: Position
    currentPos: Position
  } | null>(null)
  const [cardOrder, setCardOrder] = useState<number[]>([])
  
  // Use passed scale (from parent) or default to 1 (if not yet calculated)
  const scale = forceScale ?? 1

  const unrolledCards = useMemo(() => {
    return cards.length > 0 ? unrollCards(cards) : []
  }, [cards])

  useEffect(() => {
    if (!data?.imageUrl) {
      setImageReady(false)
      return
    }
    const img = new Image()
    img.onload = () => setImageReady(true)
    img.onerror = () => {
      console.error("[DeckGrid] Failed to preload sheet image")
      setImageReady(false)
    }
    img.src = data.imageUrl
  }, [data?.imageUrl])

  // Report natural width to parent whenever layout changes
  useEffect(() => {
    if (cardSlots.size === 0 && !data) return
    
    // Calculate natural width (unscaled)
    const cardWidth = data?.cardWidth ?? DEFAULT_CARD_WIDTH
    let maxCol = 0
    cardSlots.forEach(slot => { if (slot.col > maxCol) maxCol = slot.col })
    const effectiveColumns = cardSlots.size > 0 ? maxCol + 1 : (data?.columns ?? COLUMNS)
    // Add padding to natural width
    const naturalGridWidth = effectiveColumns * cardWidth + (effectiveColumns + 1) * GAP + 4
    
    onNaturalWidthChange?.(naturalGridWidth)
  }, [data, cardSlots, collapsed, onNaturalWidthChange])

  useEffect(() => {
    const totalCards = unrolledCards.length
    if (totalCards > 0) {
      const newSlots = new Map<number, GridSlot>()
      
      if (collapsed) {
         unrolledCards.forEach((card, rowIndex) => {
           newSlots.set(card.index, { col: 0, row: rowIndex })
         })
      } else {
        const columns = getSortModeColumns(sortMode)
        const groups = groupCardsBySortMode(unrolledCards, sortMode)
        
        columns.forEach((colKey, colIndex) => {
          const cardsInGroup = groups.get(colKey) || []
          cardsInGroup.forEach((card, rowIndex) => {
            newSlots.set(card.index, { col: colIndex, row: rowIndex })
          })
        })

        let extraCol = columns.length
        groups.forEach((groupCards, key) => {
          if (!columns.includes(key)) {
            groupCards.forEach((card, rowIndex) => {
              if (!newSlots.has(card.index)) {
                newSlots.set(card.index, { col: extraCol, row: rowIndex })
              }
            })
            extraCol++
          }
        })
      }

      setCardSlots(newSlots)
      setCardOrder(prev => {
        if (prev.length === totalCards) return prev
        return Array.from({ length: totalCards }, (_, i) => i)
      })
    } else {
      setCardSlots(new Map())
      setCardOrder([])
    }
  }, [data, unrolledCards, sortMode, collapsed])

  const cardPositions = useMemo(() => {
    const cardWidth = data?.cardWidth ?? DEFAULT_CARD_WIDTH
    const cardHeight = data?.cardHeight ?? DEFAULT_CARD_HEIGHT
    const positions = new Map<number, Position>()
    cardSlots.forEach((slot, cardIndex) => {
      positions.set(cardIndex, getSlotPosition(slot.col, slot.row, cardWidth, cardHeight))
    })
    return positions
  }, [cardSlots, data])

  const insertCardInColumn = useCallback((cardIndex: number, targetCol: number, targetRow: number) => {
    setCardSlots(prev => {
      const newSlots = new Map(prev)
      const currentSlot = prev.get(cardIndex)
      const sourceCol = currentSlot?.col

      if (sourceCol !== undefined && sourceCol !== targetCol) {
        const sourceColumnCards: { index: number; row: number }[] = []
        prev.forEach((slot, idx) => {
          if (idx !== cardIndex && slot.col === sourceCol) {
            sourceColumnCards.push({ index: idx, row: slot.row })
          }
        })
        sourceColumnCards.sort((a, b) => a.row - b.row)
        sourceColumnCards.forEach(({ index }, row) => {
          newSlots.set(index, { col: sourceCol, row })
        })
      }

      const columnCards: { index: number; row: number }[] = []
      prev.forEach((slot, idx) => {
        if (idx !== cardIndex && slot.col === targetCol) {
          columnCards.push({ index: idx, row: slot.row })
        }
      })
      columnCards.sort((a, b) => a.row - b.row)
      const allCardsInColumn = [...columnCards.map(c => c.index)]
      
      let insertAt = 0
      for (let i = 0; i < columnCards.length; i++) {
        if (columnCards[i].row < targetRow) insertAt = i + 1
        else break
      }
      allCardsInColumn.splice(insertAt, 0, cardIndex)
      allCardsInColumn.forEach((idx, row) => {
        newSlots.set(idx, { col: targetCol, row })
      })
      return newSlots
    })
  }, [])

  const handleDragStart = useCallback((index: number, e: React.MouseEvent | React.TouchEvent) => {
    e.preventDefault()
    if (!data) return
    const clientX = 'touches' in e ? e.touches[0].clientX : e.clientX
    const clientY = 'touches' in e ? e.touches[0].clientY : e.clientY
    const slot = cardSlots.get(index) || { col: 0, row: 0 }
    const pos = getSlotPosition(slot.col, slot.row, data.cardWidth, data.cardHeight)

    setDragState({
      index,
      startSlot: slot,
      startMousePos: { x: clientX, y: clientY },
      currentPos: pos
    })
    setCardOrder(prev => {
      const newOrder = prev.filter(i => i !== index)
      newOrder.push(index)
      return newOrder
    })
  }, [cardSlots, data])

  const handleDragMove = useCallback((e: MouseEvent | TouchEvent) => {
    if (!dragState || !data) return
    const clientX = 'touches' in e ? e.touches[0].clientX : e.clientX
    const clientY = 'touches' in e ? e.touches[0].clientY : e.clientY
    const startPos = getSlotPosition(dragState.startSlot.col, dragState.startSlot.row, data.cardWidth, data.cardHeight)
    const deltaX = (clientX - dragState.startMousePos.x) / scale
    const deltaY = (clientY - dragState.startMousePos.y) / scale
    const newPos = {
      x: Math.max(0, startPos.x + deltaX),
      y: Math.max(0, startPos.y + deltaY)
    }
    setDragState(prev => prev ? { ...prev, currentPos: newPos } : null)
    const hoverSlot = findNearestSlot(newPos, data.cardWidth, data.cardHeight, data.columns)
    const currentSlot = cardSlots.get(dragState.index)
    if (currentSlot && (currentSlot.col !== hoverSlot.col || currentSlot.row !== hoverSlot.row)) {
      insertCardInColumn(dragState.index, hoverSlot.col, hoverSlot.row)
    }
  }, [dragState, data, scale, cardSlots, insertCardInColumn])

  const handleDragEnd = useCallback(() => setDragState(null), [])

  useEffect(() => {
    if (!dragState) return
    const handleMouseMove = (e: MouseEvent) => handleDragMove(e)
    const handleTouchMove = (e: TouchEvent) => handleDragMove(e)
    window.addEventListener("mousemove", handleMouseMove)
    window.addEventListener("mouseup", handleDragEnd)
    window.addEventListener("touchmove", handleTouchMove)
    window.addEventListener("touchend", handleDragEnd)
    return () => {
      window.removeEventListener("mousemove", handleMouseMove)
      window.removeEventListener("mouseup", handleDragEnd)
      window.removeEventListener("touchmove", handleTouchMove)
      window.removeEventListener("touchend", handleDragEnd)
    }
  }, [dragState, handleDragMove, handleDragEnd])

  const cardWidth = data?.cardWidth || DEFAULT_CARD_WIDTH
  const cardHeight = data?.cardHeight || DEFAULT_CARD_HEIGHT
  
  const actualColumns = useMemo(() => {
    if (cardSlots.size === 0) return data?.columns || COLUMNS
    let maxCol = 0
    cardSlots.forEach(slot => { if (slot.col > maxCol) maxCol = slot.col })
    return maxCol + 1
  }, [cardSlots, data?.columns])

  const cardsPerColumn = new Array(actualColumns).fill(0)
  cardSlots.forEach(slot => { if (slot.col < actualColumns) cardsPerColumn[slot.col]++ })

  const naturalWidth = cardWidth * actualColumns + GAP * (actualColumns + 1)
  const maxPileHeight = Math.max(...cardsPerColumn.map(count => getPileHeight(count, cardHeight)), cardHeight)
  const maxY = maxPileHeight + GAP * 2 + COLUMN_HEADER_HEIGHT
  const scaledHeight = maxY * scale
  
  const getCardPosition = (cardIndex: number): Position => {
    if (dragState?.index === cardIndex) return dragState.currentPos
    return cardPositions.get(cardIndex) || { x: 0, y: 0 }
  }

  if (!data && !loading && cards.length === 0) return null

  return (
    <div
      ref={scrollContainerRef}
      className="flex-1 overflow-y-scroll overflow-x-hidden bg-muted/30 relative min-h-0"
    >


        <div style={{ height: scaledHeight, position: "relative" }}>
          <div
             className="absolute top-0 left-0 origin-top-left"
             style={{
               width: naturalWidth, height: maxY, transform: `scale(${scale})`, touchAction: dragState ? "none" : "auto"
             }}
          >
             {cardSlots.size > 0 && (() => {
                const labels = getSortModeColumns(sortMode)
                const manaColors = ['W', 'U', 'B', 'R', 'G']
                return (
                  <div className="absolute top-0 left-0 flex" style={{ gap: GAP, paddingLeft: GAP, paddingTop: GAP / 2 }}>
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

                       const label = labels[col] || ''
                       const isMana = sortMode === 'colors' && manaColors.includes(label)
                       const isColorless = sortMode === 'colors' && label === 'C'
                       return (
                         <div key={col} className="flex flex-col items-center justify-center text-xs font-medium text-muted-foreground bg-muted/50 rounded-md px-1"
                              style={{ width: cardWidth, height: COLUMN_HEADER_HEIGHT - GAP / 2 }}>
                             <span className="truncate max-w-full flex items-center gap-1" title={label}>
                                {isMana ? <img src={`/mana-symbols/${label}.svg`} alt={label} className="h-4 w-4"/> : isColorless ? <span className="text-sm">◇</span> : label}
                             </span>
                             <span className="text-[10px] opacity-60">{count}</span>
                         </div>
                       )
                    })}
                  </div>
                )
             })()}

             {cardOrder.map((cardIndex) => {
               const pos = getCardPosition(cardIndex)
               const zIndex = cardSlots.get(cardIndex)?.row ?? 0
               if (data && imageReady) {
                 const card = unrolledCards.find(c => c.index === cardIndex)
                 const sheetIndex = card?.originalIndex ?? cardIndex
                 return <SheetCard key={cardIndex} index={cardIndex} sheetIndex={sheetIndex} sheetUrl={data.imageUrl} columns={data.columns} cardWidth={data.cardWidth} cardHeight={data.cardHeight} position={pos} onDragStart={handleDragStart} isDragging={dragState?.index === cardIndex} zIndex={zIndex} />
               }
               return <SkeletonCard key={cardIndex} cardWidth={DEFAULT_CARD_WIDTH} cardHeight={DEFAULT_CARD_HEIGHT} position={pos} zIndex={zIndex} />
             })}
          </div>
      </div>
    </div>
  )
}


export default function Collection() {
  const { data, loading, error, fetchSheet, reset } = useDeckSheet()
  const { cards: allCards, loading: sortableLoading, fetchSortableCards, reset: resetSortable } = useSortableCards()
  const { identifiers: decks, loading: decksLoading } = useDeckIdentifiers()
  const [selectedDeckHash, setSelectedDeckHash] = useState<string>("")
  const [sortMode, setSortMode] = useState<SortMode>('cmc')
  const [isSideboardCollapsed, setIsSideboardCollapsed] = useState(true)

  // Track natural widths for parent-driven scaling
  const [mainNaturalWidth, setMainNaturalWidth] = useState(0)
  const [sideNaturalWidth, setSideNaturalWidth] = useState(0)
  const [containerWidth, setContainerWidth] = useState(0)
  const containerRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!containerRef.current) return
    const observer = new ResizeObserver(entries => {
      // Use requestAnimationFrame to debounce slightly/prevent loop errors
      requestAnimationFrame(() => {
        if (!containerRef.current) return
        setContainerWidth(containerRef.current.clientWidth)
      })
    })
    observer.observe(containerRef.current)
    return () => observer.disconnect()
  }, [])

  const selectedDeckName = useMemo(() => {
    if (!selectedDeckHash) return ""
    return decks.find(d => d.hash === selectedDeckHash)?.name || ""
  }, [decks, selectedDeckHash])

  const mainboardCards = useMemo(() => allCards.filter(c => c.zone === 'Mainboard' || !c.zone), [allCards])
  const sideboardCards = useMemo(() => allCards.filter(c => c.zone === 'Sideboard'), [allCards])

  const initializedRef = useRef<string | boolean>(false)
  const fetchAbortRef = useRef<AbortController | null>(null)

  useEffect(() => {
    const fetchKey = `fetch-${selectedDeckHash || "none"}`
    if (initializedRef.current === fetchKey) return
    initializedRef.current = fetchKey as any
    
    fetchAbortRef.current?.abort()
    const abortController = new AbortController()
    fetchAbortRef.current = abortController

    if (!selectedDeckHash) {
      reset()
      resetSortable()
      return 
    }

    reset()
    resetSortable()

    const fetchParallel = async () => {
      console.log(`[Collection] Fetching data for ${selectedDeckName} (${selectedDeckHash})...`)
      if (abortController.signal.aborted) return
      
      try {
        const deckId = selectedDeckHash
        await fetchSortableCards(selectedDeckName, deckId || undefined)
        if (!abortController.signal.aborted) {
          await fetchSheet(selectedDeckName, deckId, COLUMNS, 300)
        }
      } catch (e) {
        if ((e as Error).name !== 'AbortError') throw e
      }
    }
    fetchParallel()

    return () => abortController.abort()
  }, [fetchSheet, fetchSortableCards, selectedDeckName, selectedDeckHash])

  const sharedScale = useMemo(() => {
     if (containerWidth === 0 || mainNaturalWidth === 0) return 1
     // Total natural width required. 
     // Note: we might have a pixel for the border.
     const totalNatural = mainNaturalWidth + (sideboardCards.length > 0 ? sideNaturalWidth : 0)
     // Prevent divide by zero
     if (totalNatural === 0) return 1
     return Math.min(1, containerWidth / totalNatural)
  }, [containerWidth, mainNaturalWidth, sideNaturalWidth, sideboardCards.length])

  // Calculated width for the sideboard container
  const sideContainerWidth = sideboardCards.length > 0 ? sideNaturalWidth * sharedScale : 0

  return (
    <div className="flex flex-col h-full p-4 gap-6 min-w-0 overflow-hidden">
      {allCards.length > 0 && (
        <DeckStats mainboard={mainboardCards} sideboard={sideboardCards} />
      )}

      <Card className="border-sidebar-border/60 flex-1 flex flex-col min-h-0 overflow-hidden">
        {/* Shared header */}
        <CardHeader className="p-3 flex flex-row items-center gap-3 space-y-0 border-b border-border/50 flex-shrink-0">
          <CardTitle className="text-base font-medium flex items-center gap-2">
            <LayoutGrid className="w-4 h-4" />
            Deck
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

          <div className="flex items-center gap-2 ml-auto">
            <span className="text-xs text-muted-foreground">Deck:</span>
            <Select value={selectedDeckHash} onValueChange={setSelectedDeckHash}>
              <SelectTrigger className="h-7 min-w-[140px] text-xs">
                <SelectValue placeholder="Select a deck..." />
              </SelectTrigger>
              <SelectContent>
                {decks.map((deck) => (
                  <SelectItem key={deck.hash} value={deck.hash}>
                    {deck.format} - {deck.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="flex items-center gap-2">
            <span className="text-xs text-muted-foreground">Sort:</span>
            <Select value={sortMode} onValueChange={(v) => setSortMode(v as SortMode)}>
              <SelectTrigger className="h-7 w-[90px] text-xs"><SelectValue placeholder="Sort..." /></SelectTrigger>
              <SelectContent>
                <SelectItem value="cmc">CMC</SelectItem>
                <SelectItem value="colors">Colors</SelectItem>
                <SelectItem value="types">Types</SelectItem>
                <SelectItem value="rarity">Rarity</SelectItem>
              </SelectContent>
            </Select>
          </div>

          {(loading || sortableLoading) && <Loader2 className="w-4 h-4 animate-spin text-muted-foreground" />}

          {sideboardCards.length > 0 && (
            <Button variant="ghost" size="icon" className="h-8 w-8" onClick={() => setIsSideboardCollapsed(prev => !prev)}>
              {isSideboardCollapsed ? <PanelRightOpen className="h-4 w-4" /> : <PanelRightClose className="h-4 w-4" />}
            </Button>
          )}
        </CardHeader>

        {/* Side-by-side grid panes */}
        <div ref={containerRef} className="flex-1 flex min-h-0 relative">
          {/* Shared Loading Overlay */}
          {!data && (loading || sortableLoading) && (
            <div className="absolute inset-0 flex items-center justify-center z-50 bg-background/50 backdrop-blur-[1px]">
              <div className="flex flex-col items-center gap-3">
                <Loader2 className="w-8 h-8 animate-spin text-muted-foreground" />
                <span className="text-sm text-muted-foreground">Rendering deck...</span>
              </div>
            </div>
          )}
          {/* Mainboard pane */}
          <div className="flex-1 min-w-0 flex flex-col min-h-0">
            {!selectedDeckHash && (
              <div className="flex-1 flex items-center justify-center">
                <span className="text-sm text-muted-foreground">Select a deck to view</span>
              </div>
            )}
            {selectedDeckHash && (
              <DeckGrid
                title="Mainboard"
                cards={mainboardCards}
                data={data}
                loading={loading || sortableLoading}
                sortMode={sortMode}
                onNaturalWidthChange={setMainNaturalWidth}
                forceScale={sharedScale}
              />
            )}
          </div>

          {/* Sideboard pane */}
          {sideboardCards.length > 0 && (
            <div
              className="flex-shrink-0 flex flex-col min-h-0 border-l border-border/50 transition-[width] duration-300 ease-in-out"
              style={{ width: sideContainerWidth }}
            >
              <DeckGrid
                title="Sideboard"
                cards={sideboardCards}
                data={data}
                loading={loading || sortableLoading}
                sortMode={sortMode}
                collapsed={isSideboardCollapsed}
                onNaturalWidthChange={setSideNaturalWidth}
                forceScale={sharedScale}
              />
            </div>
          )}
        </div>
      </Card>
    </div>
  )
}
