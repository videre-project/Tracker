/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useState,
  type ReactNode,
  type MouseEvent as ReactMouseEvent,
} from "react"
import { createPortal } from "react-dom"

import { CardImage } from "@/components/card-image"
import { getApiUrl } from "@/utils/api-config"

interface CardTooltipData {
  catalogId: number
  name?: string
  otherFaceCatalogId?: number | null
}

interface CardTooltipPosition {
  x: number
  y: number
}

interface CardTooltipContextType {
  showTooltip: (data: CardTooltipData, position: CardTooltipPosition) => void
  updatePosition: (position: CardTooltipPosition) => void
  hideTooltip: () => void
}

const CardTooltipContext = createContext<CardTooltipContextType | null>(null)

// Global client-side cache for card detail lookups (catalogId -> details)
const cardDetailsCache = new Map<number, { otherFaceCatalogId?: number | null }>()
const pendingDetailFetches = new Map<number, Promise<void>>()

const CARD_WIDTH = 210
const CARD_HEIGHT = 294
const TOOLTIP_GAP = 8
const TOOLTIP_OFFSET = 16

export function CardTooltipProvider({ children }: { children: ReactNode }) {
  const [activeCard, setActiveCard] = useState<CardTooltipData | null>(null)
  const [position, setPosition] = useState<CardTooltipPosition>({ x: 0, y: 0 })
  const [fetchedOtherFaceId, setFetchedOtherFaceId] = useState<number | null>(null)

  const showTooltip = useCallback((data: CardTooltipData, pos: CardTooltipPosition) => {
    if (data.catalogId <= 0) return
    setActiveCard(data)
    setPosition(pos)
    setFetchedOtherFaceId(data.otherFaceCatalogId ?? null)
  }, [])

  const updatePosition = useCallback((pos: CardTooltipPosition) => {
    setPosition(pos)
  }, [])

  const hideTooltip = useCallback(() => {
    setActiveCard(null)
    setFetchedOtherFaceId(null)
  }, [])

  // On-demand detail lookup for double-sided back-face catalog IDs
  useEffect(() => {
    if (!activeCard || activeCard.catalogId <= 0) return

    const catalogId = activeCard.catalogId

    // If caller provided explicit otherFaceCatalogId, use it
    if (activeCard.otherFaceCatalogId !== undefined) {
      setFetchedOtherFaceId(activeCard.otherFaceCatalogId)
      return
    }

    // Check cache
    if (cardDetailsCache.has(catalogId)) {
      const cached = cardDetailsCache.get(catalogId)
      setFetchedOtherFaceId(cached?.otherFaceCatalogId ?? null)
      return
    }

    // Fetch details
    if (pendingDetailFetches.has(catalogId)) return

    const fetchPromise = (async () => {
      try {
        const res = await fetch(getApiUrl(`/api/collection/cards/${catalogId}/details`))
        if (!res.ok) return
        const data = await res.json()
        const otherId = typeof data.otherFaceCatalogId === "number" ? data.otherFaceCatalogId : null
        cardDetailsCache.set(catalogId, { otherFaceCatalogId: otherId })
        setFetchedOtherFaceId(otherId)
      } catch {
        cardDetailsCache.set(catalogId, { otherFaceCatalogId: null })
      } finally {
        pendingDetailFetches.delete(catalogId)
      }
    })()

    pendingDetailFetches.set(catalogId, fetchPromise)
  }, [activeCard])

  const otherFaceCatalogId = activeCard?.otherFaceCatalogId ?? fetchedOtherFaceId
  const isDoubleSided = Boolean(otherFaceCatalogId && otherFaceCatalogId > 0 && otherFaceCatalogId !== activeCard?.catalogId)

  // Viewport bounding math & intelligent left/right placement
  const containerWidth = isDoubleSided ? CARD_WIDTH * 2 + TOOLTIP_GAP + 12 : CARD_WIDTH + 12
  const containerHeight = CARD_HEIGHT + 12

  let left = position.x + TOOLTIP_OFFSET
  let top = position.y - CARD_HEIGHT / 2

  if (typeof window !== "undefined") {
    const screenWidth = window.innerWidth
    const screenHeight = window.innerHeight

    // Position tooltip to the left of cursor if on the right half of the screen
    if (position.x > screenWidth / 2) {
      left = position.x - containerWidth - TOOLTIP_OFFSET
      if (left < 12) {
        left = position.x + TOOLTIP_OFFSET
      }
    } else {
      if (left + containerWidth > screenWidth - 12) {
        left = position.x - containerWidth - TOOLTIP_OFFSET
      }
    }

    // Clamp inside viewport bounds
    left = Math.max(12, Math.min(left, screenWidth - containerWidth - 12))
    top = Math.max(12, Math.min(top, screenHeight - containerHeight - 12))
  }

  return (
    <CardTooltipContext.Provider value={{ showTooltip, updatePosition, hideTooltip }}>
      {children}
      {activeCard && typeof document !== "undefined"
        ? createPortal(
            <div
              className="fixed z-[9999] pointer-events-none rounded-xl border border-white/15 bg-black/85 p-1.5 shadow-2xl backdrop-blur-md transition-opacity duration-150 animate-in fade-in-0 zoom-in-95"
              style={{
                left: `${left}px`,
                top: `${top}px`,
              }}
            >
              <div className="flex flex-row items-center gap-2">
                <div
                  className="relative overflow-hidden rounded-lg border border-white/10 shadow-md bg-muted/30"
                  style={{ width: CARD_WIDTH, height: CARD_HEIGHT }}
                >
                  <CardImage
                    catalogId={activeCard.catalogId}
                    alt={activeCard.name ?? ""}
                    width={CARD_WIDTH}
                    height={CARD_HEIGHT}
                    style={{ width: CARD_WIDTH, height: CARD_HEIGHT, objectFit: "cover" }}
                  />
                </div>
                {isDoubleSided && otherFaceCatalogId ? (
                  <div
                    className="relative overflow-hidden rounded-lg border border-white/10 shadow-md bg-muted/30"
                    style={{ width: CARD_WIDTH, height: CARD_HEIGHT }}
                  >
                    <CardImage
                      catalogId={otherFaceCatalogId}
                      alt={`${activeCard.name ?? ""} (Back Face)`}
                      width={CARD_WIDTH}
                      height={CARD_HEIGHT}
                      style={{ width: CARD_WIDTH, height: CARD_HEIGHT, objectFit: "cover" }}
                    />
                  </div>
                ) : null}
              </div>
            </div>,
            document.body
          )
        : null}
    </CardTooltipContext.Provider>
  )
}

export function useCardTooltip() {
  const context = useContext(CardTooltipContext)
  if (!context) {
    throw new Error("useCardTooltip must be used within a CardTooltipProvider")
  }
  return context
}

export function useCardTooltipHover({
  catalogId,
  name,
  otherFaceCatalogId,
  enabled = true,
}: {
  catalogId?: number | null
  name?: string
  otherFaceCatalogId?: number | null
  enabled?: boolean
}) {
  const context = useContext(CardTooltipContext)

  const onMouseEnter = useCallback(
    (e: ReactMouseEvent) => {
      if (!context || !enabled || !catalogId || catalogId <= 0) return
      context.showTooltip({ catalogId, name, otherFaceCatalogId }, { x: e.clientX, y: e.clientY })
    },
    [context, enabled, catalogId, name, otherFaceCatalogId]
  )

  const onMouseMove = useCallback(
    (e: ReactMouseEvent) => {
      if (!context || !enabled || !catalogId || catalogId <= 0) return
      context.updatePosition({ x: e.clientX, y: e.clientY })
    },
    [context, enabled, catalogId]
  )

  const onMouseLeave = useCallback(() => {
    if (!context) return
    context.hideTooltip()
  }, [context])

  return {
    onMouseEnter: context && enabled && catalogId ? onMouseEnter : undefined,
    onMouseMove: context && enabled && catalogId ? onMouseMove : undefined,
    onMouseLeave: context && enabled && catalogId ? onMouseLeave : undefined,
  }
}
