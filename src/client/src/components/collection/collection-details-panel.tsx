/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import {
  type ReactNode,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react"
import { ArrowDownRight, ArrowUpRight, Loader2, X } from "lucide-react"
import { Area, AreaChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from "recharts"

import { Button } from "@/components/ui/button"
import type { CollectionProductEntry } from "@/hooks/use-collection"
import { cn } from "@/lib/utils"
import { getApiUrl } from "@/utils/api-config"
import { GameLogText } from "@/utils/parse-game-log"
import { CollectionCardImage, CollectionProductImage } from "./collection-grid"
import type {
  CollectionCardDetail,
  CollectionPriceChartPoint,
  CollectionPriceHistorySnapshot,
  SelectedCollectionItem,
} from "./collection-types"
import {
  formatCollectionHistoryPrice,
  formatCollectionPrice,
  formatPriceDelta,
  formatPriceDeltaPercent,
  getCollectionHistoryPrecision,
  isPriceHistoryCacheFresh,
} from "./collection-utils"

const COLLECTION_RECENT_CLOSES_LIMIT = 7
const collectionPriceHistoryCache = new Map<number, CollectionPriceHistorySnapshot>()
const collectionCardDetailCache = new Map<number, CollectionCardDetail>()
function CollectionPriceTooltip({
  active,
  payload,
}: {
  active?: boolean
  payload?: Array<{ payload?: CollectionPriceChartPoint }>
}) {
  const point = payload?.[0]?.payload

  if (!active || !point) return null

  return (
    <div className="min-w-[8.75rem] rounded-md border border-sidebar-border/70 bg-popover/95 px-2.5 py-2 text-xs text-popover-foreground shadow-xl backdrop-blur">
      <div className="text-[10px] font-medium uppercase leading-none text-muted-foreground">
        {point.date}
      </div>
      <div className="mt-2 flex items-baseline justify-between gap-4">
        <span className="text-muted-foreground">Average</span>
        <span className="flex items-baseline gap-1.5 font-semibold tabular-nums text-foreground">
          <span>
            {point.label}
            <span className="ml-1 text-[10px] font-medium text-muted-foreground">tix</span>
          </span>
          {point.deltaLabel ? (
            <span className={cn(
              "text-[10px] font-medium",
              point.deltaPositive === null
                ? "text-muted-foreground"
                : point.deltaPositive
                  ? "text-emerald-400"
                  : "text-red-400"
            )}>
              {point.deltaLabel}
              {point.deltaPercentLabel ? (
                <span className="ml-1 text-current/70">({point.deltaPercentLabel})</span>
              ) : null}
            </span>
          ) : null}
        </span>
      </div>
    </div>
  )
}

function ScrollFadeText({
  children,
  className,
  contentClassName,
  watchKey,
}: {
  children: ReactNode
  className?: string
  contentClassName?: string
  watchKey: string
}) {
  const scrollRef = useRef<HTMLDivElement | null>(null)
  const [showFade, setShowFade] = useState(false)

  const updateFade = useCallback(() => {
    const element = scrollRef.current
    if (!element) return

    const hasOverflow = element.scrollHeight > element.clientHeight + 1
    const atBottom = element.scrollTop + element.clientHeight >= element.scrollHeight - 1
    setShowFade(hasOverflow && !atBottom)
  }, [])

  useEffect(() => {
    const element = scrollRef.current
    if (!element) return

    const frame = window.requestAnimationFrame(updateFade)
    const resizeObserver = typeof ResizeObserver === "undefined"
      ? null
      : new ResizeObserver(updateFade)

    resizeObserver?.observe(element)
    Array.from(element.children).forEach(child => resizeObserver?.observe(child))

    return () => {
      window.cancelAnimationFrame(frame)
      resizeObserver?.disconnect()
    }
  }, [updateFade, watchKey])

  return (
    <div className={cn("relative mt-2 overflow-hidden", className)}>
      <div
        ref={scrollRef}
        onScroll={updateFade}
        className={cn(
          "overflow-y-auto overflow-x-hidden whitespace-pre-wrap break-words pr-1",
          contentClassName
        )}
      >
        {children}
      </div>
      {showFade ? (
        <div
          className="pointer-events-none absolute bottom-0 left-0 right-2 h-5"
          style={{
            background: "linear-gradient(to top, hsl(var(--card)), transparent)",
          }}
        />
      ) : null}
    </div>
  )
}

function normalizeCollectionCardText(text?: string | null) {
  return (text ?? "")
    .replace(/\r\n/g, "\n")
    .replace(/\\n/g, "\n")
    .replace(/\r/g, "\n")
    .replace(/(^|\n)\*\*\s*/g, "$1• ")
    .trim()
}

function getCollectionCardStatText(card: CollectionCardDetail | null) {
  if (!card) return null
  const normalizedType = card.typeLine.toLowerCase()

  if (normalizedType.includes("creature")) {
    const power = card.power?.trim()
    const toughness = card.toughness?.trim()
    return power && toughness ? `${power}/${toughness}` : null
  }

  if (normalizedType.includes("planeswalker")) {
    const loyalty = card.loyalty?.trim()
    return loyalty ? `Loyalty ${loyalty}` : null
  }

  if (normalizedType.includes("battle")) {
    const defense = card.defense?.trim()
    return defense ? `Defense ${defense}` : null
  }

  return null
}

function getCollectionRarityClass(rarity?: string | null) {
  switch (rarity?.toLowerCase()) {
    case "mythic":
    case "mythic rare":
      return "border-orange-400/45 bg-orange-500/10 text-orange-300"
    case "rare":
      return "border-amber-300/45 bg-amber-500/10 text-amber-200"
    case "uncommon":
      return "border-slate-300/45 bg-slate-300/10 text-slate-200"
    case "common":
      return "border-neutral-400/35 bg-neutral-400/10 text-neutral-200"
    default:
      return "border-sidebar-border/70 bg-background/70 text-muted-foreground"
  }
}

export function CollectionPriceHistoryPanel({
  selection,
  onClose,
}: {
  selection: SelectedCollectionItem
  onClose: () => void
}) {
  const { item, viewMode } = selection
  const [history, setHistory] = useState<CollectionPriceHistorySnapshot | null>(() => {
    const cached = collectionPriceHistoryCache.get(item.catalogId)
    return isPriceHistoryCacheFresh(cached) ? cached : null
  })
  const [loading, setLoading] = useState(!history)
  const [error, setError] = useState<string | null>(null)
  const [cardDetail, setCardDetail] = useState<CollectionCardDetail | null>(() => {
    if (viewMode !== "cards") return null
    return collectionCardDetailCache.get(item.catalogId) ?? null
  })
  const [cardDetailLoading, setCardDetailLoading] = useState(
    viewMode === "cards" && !cardDetail
  )
  const [cardDetailError, setCardDetailError] = useState<string | null>(null)
  const eachPrice = formatCollectionPrice(item.price)
  const chartData = useMemo<CollectionPriceChartPoint[]>(() => {
    const points = (history?.prices ?? [])
      .filter(point => typeof point.price === "number" && Number.isFinite(point.price))

    return points.map((point, index) => {
      const previous = index > 0 ? points[index - 1] : null
      const delta = previous ? point.price - previous.price : null
      const deltaPercent = previous && previous.price !== 0
        ? delta! / previous.price * 100
        : null

      return {
        date: point.date,
        price: point.price,
        label: formatCollectionHistoryPrice(point.price) ?? String(point.price),
        delta,
        deltaLabel: formatPriceDelta(delta),
        deltaPercent,
        deltaPercentLabel: formatPriceDeltaPercent(deltaPercent),
        deltaPositive: delta !== null && Math.abs(delta) >= 0.0005 ? delta > 0 : null,
      }
    })
  }, [history])
  const priceStats = useMemo(() => {
    if (chartData.length === 0) return null

    const prices = chartData.map(point => point.price)
    const low = Math.min(...prices)
    const high = Math.max(...prices)
    const average = prices.reduce((sum, price) => sum + price, 0) / prices.length
    const trendPointsByDate = new Map<string, typeof chartData[number]>()
    chartData.forEach(point => {
      trendPointsByDate.set(point.date, point)
    })
    const trendPoints = [...trendPointsByDate.values()]
      .sort((a, b) => a.date.localeCompare(b.date))
    const latest = trendPoints.at(-1) ?? chartData.at(-1)!
    const getTrend = (baseline: typeof latest | null) => {
      if (!baseline) return null
      const delta = latest.price - baseline.price
      return {
        delta,
        percent: baseline.price !== 0 ? delta / baseline.price * 100 : null,
      }
    }
    const getOffsetTrend = (offset: number) => {
      if (trendPoints.length <= 1) return null
      const baselineIndex = Math.max(0, trendPoints.length - 1 - offset)
      return getTrend(trendPoints[baselineIndex] ?? null)
    }

    return {
      low,
      high,
      average,
      latest,
      dailyTrend: getOffsetTrend(1),
      weeklyTrend: getOffsetTrend(7),
    }
  }, [chartData])
  const trendRows = [
    { label: "1D", trend: priceStats?.dailyTrend ?? null },
    { label: "7D", trend: priceStats?.weeklyTrend ?? null },
  ]
  const trendDeltaDecimals = getCollectionHistoryPrecision(trendRows.map(row => row.trend?.delta))
  const trendDisplayRows = trendRows.map(row => {
    const direction = row.trend && Math.abs(row.trend.delta) >= 0.0005
      ? row.trend.delta > 0 ? "up" as const : "down" as const
      : "flat" as const

    return {
      ...row,
      direction,
      deltaLabel: direction === "flat" || !row.trend
        ? null
        : formatPriceDelta(row.trend.delta, trendDeltaDecimals),
      percentLabel: direction === "flat" || !row.trend
        ? null
        : formatPriceDeltaPercent(row.trend.percent),
    }
  })
  const marketPrice = priceStats
    ? formatCollectionPrice(priceStats.latest.price)
    : eachPrice
  const lowPrice = priceStats ? formatCollectionPrice(priceStats.low) : null
  const highPrice = priceStats ? formatCollectionPrice(priceStats.high) : null
  const averagePrice = priceStats ? formatCollectionPrice(priceStats.average) : null
  const rangePrice = priceStats && lowPrice && highPrice
    ? priceStats.low === priceStats.high
      ? lowPrice
      : `${lowPrice} - ${highPrice}`
    : lowPrice ?? highPrice
  const chartDomain = useMemo<[number, number] | undefined>(() => {
    if (!priceStats) return undefined

    const range = priceStats.high - priceStats.low
    const lowerPadding = range === 0
      ? Math.max(Math.abs(priceStats.high) * 0.08, 0.001)
      : Math.max(range * 0.18, 0.001)
    const upperPadding = range === 0
      ? Math.max(Math.abs(priceStats.high) * 0.16, 0.002)
      : Math.max(range * 0.35, 0.002)

    return [
      Math.max(0, priceStats.low - lowerPadding),
      priceStats.high + upperPadding,
    ]
  }, [priceStats])
  const cardOracleText = normalizeCollectionCardText(cardDetail?.oracleText)
  const cardFlavorText = normalizeCollectionCardText(cardDetail?.flavorText)
  const productItem = viewMode === "products" ? item as CollectionProductEntry : null
  const productDescription = productItem
    ? normalizeCollectionCardText(productItem.description)
    : ""
  const productSetName = productItem?.setName?.trim() || productItem?.setCode?.trim() || ""
  const cardStatText = getCollectionCardStatText(cardDetail)
  const cardSetLabel = cardDetail?.setCode
    ? [cardDetail.setCode, cardDetail.collectorNumber ? `#${cardDetail.collectorNumber}` : null]
        .filter(Boolean)
        .join(" ")
    : null
  const recentRows = useMemo(() => {
    const rows = chartData.slice(-COLLECTION_RECENT_CLOSES_LIMIT).reverse()
    const decimals = getCollectionHistoryPrecision(rows.map(point => point.price))
    const maxDeltaPercent = Math.max(
      0,
      ...rows.map(point => (
        typeof point.deltaPercent === "number" && Number.isFinite(point.deltaPercent)
          ? Math.abs(point.deltaPercent)
          : 0
      ))
    )

    return rows.map(point => ({
      ...point,
      displayLabel: formatCollectionHistoryPrice(point.price, decimals) ?? point.label,
      displayDeltaLabel: formatPriceDelta(point.delta, decimals),
      displayDeltaPercentLabel: point.deltaPercentLabel,
      displayDeltaPercentBar: typeof point.deltaPercent === "number" && Number.isFinite(point.deltaPercent) && maxDeltaPercent > 0
        ? Math.min(50, Math.abs(point.deltaPercent) / maxDeltaPercent * 50)
        : 0,
    }))
  }, [chartData])

  useEffect(() => {
    if (viewMode !== "cards") {
      setCardDetail(null)
      setCardDetailLoading(false)
      setCardDetailError(null)
      return
    }

    const cached = collectionCardDetailCache.get(item.catalogId)
    if (cached) {
      setCardDetail(cached)
      setCardDetailLoading(false)
      setCardDetailError(null)
      return
    }

    const abortController = new AbortController()
    setCardDetail(null)
    setCardDetailLoading(true)
    setCardDetailError(null)

    fetch(getApiUrl(`/api/collection/cards/${item.catalogId}/details`), {
      signal: abortController.signal,
    })
      .then(async response => {
        if (!response.ok) {
          let message = `HTTP ${response.status}`
          try {
            const body = await response.json()
            message = body.message ?? body.error ?? message
          } catch {
          }

          throw new Error(message)
        }

        return response.json() as Promise<CollectionCardDetail>
      })
      .then(data => {
        collectionCardDetailCache.set(item.catalogId, data)
        setCardDetail(data)
      })
      .catch(err => {
        if (err instanceof Error && err.name === "AbortError") return
        setCardDetailError(err instanceof Error ? err.message : "Card details unavailable")
      })
      .finally(() => {
        if (!abortController.signal.aborted) {
          setCardDetailLoading(false)
        }
      })

    return () => abortController.abort()
  }, [item.catalogId, viewMode])

  useEffect(() => {
    const cached = collectionPriceHistoryCache.get(item.catalogId)
    if (isPriceHistoryCacheFresh(cached)) {
      setHistory(cached)
      setLoading(false)
      setError(null)
      return
    }

    const abortController = new AbortController()
    setLoading(true)
    setError(null)
    setHistory(null)

    const to = new Date()
    const from = new Date(to)
    from.setUTCDate(from.getUTCDate() - 365)
    const params = new URLSearchParams({
      from: from.toISOString().slice(0, 10),
      to: to.toISOString().slice(0, 10),
      limit: "365",
    })

    fetch(getApiUrl(`/api/collection/prices/${item.catalogId}/history?${params.toString()}`), {
      signal: abortController.signal,
    })
      .then(async response => {
        if (!response.ok) {
          let message = `HTTP ${response.status}`
          try {
            const body = await response.json()
            message = body.message ?? body.error ?? message
          } catch {
          }

          throw new Error(message)
        }

        return response.json() as Promise<CollectionPriceHistorySnapshot>
      })
      .then(data => {
        collectionPriceHistoryCache.set(item.catalogId, data)
        setHistory(data)
      })
      .catch(err => {
        if (err instanceof Error && err.name === "AbortError") return
        setError(err instanceof Error ? err.message : "Price history unavailable")
        setHistory(null)
      })
      .finally(() => {
        if (!abortController.signal.aborted) {
          setLoading(false)
        }
      })

    return () => abortController.abort()
  }, [item.catalogId])

  return (
    <aside className="flex h-full min-h-0 w-96 shrink-0 flex-col overflow-hidden rounded-lg border border-sidebar-border/60 bg-card">
      <div className="relative h-[13rem] shrink-0 overflow-hidden border-b border-sidebar-border/60 p-3">
        {viewMode === "cards" ? (
          <div className="grid h-full grid-cols-[88px_minmax(0,1fr)] gap-x-3">
            <div className="min-w-0">
              <div className="h-[122px] w-[88px] overflow-hidden rounded border border-sidebar-border/60 bg-muted/30">
                <CollectionCardImage catalogId={item.catalogId} name={cardDetail?.name ?? item.name} />
              </div>
              <div className="mt-1.5 text-left text-[11px] font-medium leading-none text-muted-foreground">
                {item.quantity.toLocaleString()} owned
              </div>
            </div>
            <div className="flex h-full min-h-0 min-w-0 flex-col overflow-hidden pr-1">
              <div className="min-w-0 shrink-0 pr-8 text-base font-semibold leading-5 text-foreground">
                {cardDetail?.name ?? item.name}
              </div>

              <div className="mt-2 flex shrink-0 flex-wrap items-center gap-1.5 text-[10px]">
                {cardDetail?.manaCost ? (
                  <span className="inline-flex h-5 items-center gap-0.5 rounded-sm border border-sidebar-border/60 bg-background/60 px-1.5 leading-none text-foreground/85">
                    <GameLogText
                      text={cardDetail.manaCost}
                      manaSymbolClassName="inline h-3.5 w-3.5 align-text-bottom mx-px"
                    />
                  </span>
                ) : null}
                {cardSetLabel ? (
                  <span
                    className={cn(
                      "inline-flex h-5 items-center gap-1 rounded-sm border px-1.5 font-medium uppercase leading-none",
                      getCollectionRarityClass(cardDetail?.rarity)
                    )}
                  >
                    <span className="h-2 w-2 rotate-45 rounded-[1px] bg-current opacity-80" />
                    {cardSetLabel}
                  </span>
                ) : cardDetailLoading ? (
                  <span className="h-5 w-16 animate-pulse rounded-sm bg-muted/60" />
                ) : null}
              </div>

              {cardDetail?.typeLine ? (
                <div className="mt-2 flex min-w-0 shrink-0 items-start gap-2">
                  <div className="min-w-0 flex-1 text-xs font-medium leading-4 text-foreground/85">
                    {cardDetail.typeLine}
                  </div>
                  {cardStatText ? (
                    <span className="inline-flex shrink-0 items-center rounded-sm bg-background/70 px-1.5 py-0.5 text-[11px] font-medium leading-none text-foreground/85 ring-1 ring-sidebar-border/65">
                      {cardStatText}
                    </span>
                  ) : null}
                </div>
              ) : cardDetailLoading ? (
                <div className="mt-2 h-3 w-4/5 animate-pulse rounded bg-muted/60" />
              ) : cardStatText ? (
                <div className="mt-2 flex justify-end">
                  <span className="inline-flex shrink-0 items-center rounded-sm bg-background/70 px-1.5 py-0.5 text-[11px] font-medium leading-none text-foreground/85 ring-1 ring-sidebar-border/65">
                    {cardStatText}
                  </span>
                </div>
              ) : null}

              {cardOracleText ? (
                <ScrollFadeText
                  className="min-h-0 flex-1"
                  contentClassName="h-full pb-2 text-xs leading-5 text-muted-foreground [&_.gl-italic]:text-muted-foreground/75"
                  watchKey={cardOracleText}
                >
                  <GameLogText
                    text={cardOracleText}
                    manaSymbolClassName="inline h-3.5 w-3.5 align-text-bottom mx-[1px]"
                  />
                </ScrollFadeText>
              ) : cardDetailLoading ? (
                <div className="mt-2 space-y-1.5">
                  <div className="h-2.5 w-full animate-pulse rounded bg-muted/60" />
                  <div className="h-2.5 w-5/6 animate-pulse rounded bg-muted/60" />
                </div>
              ) : cardDetailError ? (
                <div className="mt-2 text-[11px] leading-4 text-destructive">
                  Card details unavailable.
                </div>
              ) : null}

              {cardFlavorText && !cardOracleText ? (
                <ScrollFadeText
                  className="min-h-0 flex-1"
                  contentClassName="h-full pb-2 text-xs italic leading-5 text-muted-foreground/80"
                  watchKey={cardFlavorText}
                >
                  <GameLogText
                    text={cardFlavorText}
                    manaSymbolClassName="inline h-3.5 w-3.5 align-text-bottom mx-[1px]"
                  />
                </ScrollFadeText>
              ) : null}
            </div>
          </div>
        ) : (
          <div className="flex h-full items-start gap-3">
            <div className="min-w-0 shrink-0">
              <div className="h-[122px] w-[88px] overflow-hidden rounded border border-sidebar-border/60 bg-muted/30">
                <CollectionProductImage product={item as CollectionProductEntry} />
              </div>
              <div className="mt-1.5 text-left text-[11px] font-medium leading-none text-muted-foreground">
                {item.quantity.toLocaleString()} owned
              </div>
            </div>
            <div className="flex h-full min-h-0 min-w-0 flex-1 flex-col">
              <div className="shrink-0 truncate pr-8 text-sm font-semibold text-foreground">{item.name}</div>
              {productSetName ? (
                <div className="mt-1 shrink-0 truncate text-xs text-muted-foreground">
                  {productSetName}
                </div>
              ) : null}
              {productDescription ? (
                <ScrollFadeText
                  className="min-h-0 flex-1"
                  contentClassName="h-full pb-2 text-xs leading-5 text-muted-foreground"
                  watchKey={productDescription}
                >
                  <GameLogText
                    text={productDescription}
                    manaSymbolClassName="inline h-3.5 w-3.5 align-text-bottom mx-[1px]"
                  />
                </ScrollFadeText>
              ) : null}
            </div>
          </div>
        )}
        <Button
          type="button"
          variant="ghost"
          size="icon"
          className="absolute right-2 top-2 h-7 w-7 shrink-0 text-muted-foreground hover:text-foreground"
          onClick={onClose}
          aria-label="Close price history"
        >
          <X className="h-4 w-4" />
        </Button>
      </div>

      <div className="min-h-[150px] flex-1 border-b border-sidebar-border/60 px-3 py-3">
        <div className="relative h-full min-h-0 overflow-hidden rounded-md bg-background/20">
          {loading ? (
            <div className="flex h-full items-center justify-center">
              <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />
            </div>
          ) : error ? (
            <div className="flex h-full items-center justify-center px-3 text-center text-xs text-destructive">
              {error}
            </div>
          ) : chartData.length > 0 ? (
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={chartData} margin={{ top: 82, right: 12, left: 12, bottom: 6 }}>
                <defs>
                  <linearGradient id="collection-price-history-fill" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="hsl(var(--primary))" stopOpacity={0.28} />
                    <stop offset="95%" stopColor="hsl(var(--primary))" stopOpacity={0.02} />
                  </linearGradient>
                </defs>
                <XAxis hide dataKey="date" />
                <YAxis hide domain={chartDomain ?? ["dataMin", "dataMax"]} />
                <Tooltip
                  cursor={{ stroke: "hsl(var(--sidebar-foreground) / 0.25)", strokeWidth: 1 }}
                  content={<CollectionPriceTooltip />}
                  wrapperStyle={{ outline: "none" }}
                />
                <Area
                  type="monotone"
                  dataKey="price"
                  stroke="hsl(var(--primary))"
                  strokeWidth={2}
                  fill="url(#collection-price-history-fill)"
                  isAnimationActive={false}
                  dot={false}
                />
              </AreaChart>
            </ResponsiveContainer>
          ) : (
            <div className="flex h-full items-center justify-center text-xs text-muted-foreground">
              No price history.
            </div>
          )}
          <div className="pointer-events-none absolute inset-x-3 top-3 grid grid-cols-[5rem_minmax(6.25rem,1fr)_9rem] grid-rows-[0.625rem_auto] items-start gap-x-4 gap-y-2">
            <div className="col-start-1 row-start-1 text-[10px] font-medium uppercase leading-none text-muted-foreground">Market Price</div>
            <div className="col-start-1 row-start-2 flex min-w-0 items-baseline gap-1.5">
              <span className="text-[1.625rem] font-semibold leading-[1.75rem] tabular-nums text-foreground">
                {marketPrice ?? "-"}
              </span>
              <span className="text-xs font-medium text-muted-foreground">tix</span>
            </div>
            <div className="col-start-2 row-start-2 flex min-w-0 flex-col gap-1 text-[11px] leading-none">
              <div className="grid grid-cols-[2.25rem_minmax(0,1fr)] items-baseline gap-x-1">
                <span className="text-muted-foreground">Avg</span>
                <span className="truncate font-semibold tabular-nums text-foreground">{averagePrice ?? "-"}</span>
              </div>
              <div className="grid grid-cols-[2.25rem_minmax(0,1fr)] items-baseline gap-x-1">
                <span className="text-muted-foreground">Range</span>
                <span className="truncate font-semibold tabular-nums text-foreground">{rangePrice ?? "-"}</span>
              </div>
            </div>
            {priceStats ? (
              <div className="col-start-3 row-start-2 flex min-w-0 flex-col gap-1 text-[11px] leading-none">
                {trendDisplayRows.map(row => (
                  <div key={row.label} className="grid grid-cols-[1.25rem_minmax(0,1fr)] items-center gap-x-1">
                    <span className="text-right text-muted-foreground">{row.label}</span>
                    <span className={cn(
                      "grid min-w-0 grid-cols-[0.75rem_2.35rem_3.25rem] items-center gap-0.5 rounded-sm px-1.5 py-px text-[10px] font-medium leading-none tabular-nums ring-1 ring-white/5",
                      row.direction === "up"
                        ? "bg-emerald-500/10 text-emerald-400"
                        : row.direction === "down"
                          ? "bg-red-500/10 text-red-400"
                          : "bg-muted/55 text-muted-foreground"
                    )}>
                      <span className="flex h-2.5 w-2.5 items-center justify-center">
                        {row.direction === "up" ? (
                          <ArrowUpRight className="h-2.5 w-2.5 shrink-0" />
                        ) : row.direction === "down" ? (
                          <ArrowDownRight className="h-2.5 w-2.5 shrink-0" />
                        ) : null}
                      </span>
                      {row.deltaLabel ? (
                        <>
                          <span className="text-right">{row.deltaLabel}</span>
                          <span className="text-right text-current/70">
                            {row.percentLabel ? `(${row.percentLabel})` : ""}
                          </span>
                        </>
                      ) : (
                        <span className="col-span-2 text-center">-</span>
                      )}
                    </span>
                  </div>
                ))}
              </div>
            ) : null}
          </div>
        </div>
      </div>

      <div className="shrink-0 px-3 py-2.5">
        <div className="mb-1.5 text-[10px] font-medium uppercase text-muted-foreground">
          <span>Recent Prices</span>
        </div>
        <div className="grid grid-cols-[minmax(0,1fr)_auto_auto] items-center gap-2 border-b border-sidebar-border/45 pb-1.5 text-[10px] font-medium uppercase leading-none text-muted-foreground/70">
          <span>Date</span>
          <span className="min-w-14 text-right">Avg sell</span>
          <span className="min-w-[5.875rem] text-right">1D Δ</span>
        </div>
        {recentRows.map(point => (
          <div
            key={`${item.catalogId}-${point.date}`}
            className="grid grid-cols-[minmax(0,1fr)_auto_auto] items-stretch gap-2 border-b border-sidebar-border/35 text-xs last:border-b-0"
          >
            <span className="flex items-center py-1.5 text-muted-foreground">{point.date}</span>
            <span className="flex items-center justify-end py-1.5 font-medium tabular-nums text-foreground">{point.displayLabel}</span>
            <span className={cn(
              "relative grid min-w-[5.875rem] grid-cols-[2.25rem_3.125rem] items-stretch gap-0.5 self-stretch overflow-hidden rounded-sm whitespace-nowrap text-[11px] tabular-nums",
              point.deltaPositive === null
                ? "text-muted-foreground/45"
                : point.deltaPositive
                  ? "text-emerald-400"
                  : "text-red-400"
            )}>
              {point.displayDeltaLabel ? (
                <>
                  {point.displayDeltaPercentBar > 0 ? (
                    <span
                      className={cn(
                        "absolute inset-y-0 opacity-20",
                        point.deltaPositive ? "left-1/2 bg-emerald-400" : "right-1/2 bg-red-400"
                      )}
                      style={{ width: `${point.displayDeltaPercentBar}%` }}
                    />
                  ) : null}
                  <span className="relative z-10 flex items-center justify-end text-right">{point.displayDeltaLabel}</span>
                  {point.displayDeltaPercentLabel ? (
                    <span className="relative z-10 flex items-center justify-end px-1 text-right text-current/80">
                      <span className="relative z-10">({point.displayDeltaPercentLabel})</span>
                    </span>
                  ) : null}
                </>
              ) : (
                <span className="col-span-2 text-right">-</span>
              )}
            </span>
          </div>
        ))}
      </div>
    </aside>
  )
}
