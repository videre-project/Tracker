/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useEffect, useId, useMemo, useRef, useState, type ReactNode } from "react"
import { useSearchParams } from "react-router-dom"
import { ArrowDown, ArrowUpRight, ChevronDown, Layers, Search, SlidersHorizontal, X } from "lucide-react"
import { ScrollArea } from "radix-ui"
import { Button, CardImage, Input, Popover, PopoverContent, PopoverTrigger, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Skeleton, useCardTooltipHover, type DeckZone } from "@videreproject/ui"
import type { DeckCardStatisticsData, DeckStatisticsData } from "@/types/api"
import { useSessionQuery } from "@/hooks/use-session-query"
import { cardInZone, readCardZone, cardMetrics, cardMetricDescriptions, compareCardObservations, readCardMetric, readSampleThreshold, sampleThresholds, type CardMetric, type CardSort } from "@/utils/card-statistics"
import { OverallWinrate, PlayDrawComparison } from "./matchup-stats"

type StatisticsScope = "deck" | "revision"

function StatisticsScopeSelect({ value, onChange }: { value: StatisticsScope; onChange: (value: StatisticsScope) => void }) {
  return <Select value={value} onValueChange={value => onChange(value as StatisticsScope)}>
    <SelectTrigger aria-label="Statistics scope" className="h-8 w-36 text-xs"><SelectValue /></SelectTrigger>
    <SelectContent><SelectItem value="deck">All revisions</SelectItem><SelectItem value="revision">This revision</SelectItem></SelectContent>
  </Select>
}

function StatisticsCardLabel({ row, emphasized = false }: { row: DeckCardStatisticsData; emphasized?: boolean }) {
  const card = row.card
  const hover = useCardTooltipHover({ catalogId: card?.catalogId, name: card?.name ?? undefined, otherFaceCatalogId: card?.otherFaceCatalogId, delayMs: 250 })
  return <span className="flex min-w-0 items-center gap-2">
    <span className={`block h-9 w-10 shrink-0 overflow-hidden rounded bg-muted ${emphasized ? "outline outline-2 outline-offset-1 outline-primary/75" : ""}`} {...hover}>
      <CardImage catalogId={card?.catalogId ?? null} name={card?.name ?? undefined} alt="" width={280} height={392}
        className="h-auto w-full" fallback={<Layers className="m-2 h-5 w-5 text-muted-foreground" />} />
    </span>
    <span className="min-w-0 text-xs font-medium leading-4">{card?.name}</span>
  </span>
}

function StatisticsGameSelect({ value, onChange }: { value: string; onChange: (value: string) => void }) {
  return <Select value={value} onValueChange={onChange}>
    <SelectTrigger aria-label="Games included" className="h-8 w-36 text-xs"><SelectValue /></SelectTrigger>
    <SelectContent><SelectItem value="all">All games</SelectItem><SelectItem value="one">Game 1</SelectItem><SelectItem value="postboard">Games 2–3</SelectItem></SelectContent>
  </Select>
}

function StatisticsError({ message, retry }: { message: string; retry: () => void }) {
  return <div role="alert" className="space-y-2 py-4 text-xs"><p className="text-destructive">{message}</p><Button type="button" variant="outline" size="sm" onClick={retry}>Try again</Button></div>
}

function StatisticsSampleSelect({ value, onChange, className = "w-28" }: { value: number; onChange: (value: number) => void; className?: string }) {
  return <Select value={String(value)} onValueChange={value => onChange(Number(value))}>
    <SelectTrigger aria-label="Minimum observed games" className={`h-8 text-xs ${className}`}><SelectValue /></SelectTrigger>
    <SelectContent>{sampleThresholds.map(threshold => <SelectItem key={threshold} value={String(threshold)}>{threshold ? `${threshold}+ games` : "All samples"}</SelectItem>)}</SelectContent>
  </Select>
}

export function DeckStatisticsSidebar({
  revisionId, initialScope = "deck", onCardEmphasisChange, cardOrder, hoveredDeckCardName,
}: {
  revisionId: number
  initialScope?: StatisticsScope
  onCardEmphasisChange?: (name: string | null) => void
  cardOrder?: Partial<Record<DeckZone, readonly string[]>>
  hoveredDeckCardName?: string | null
}) {
  const [params, setParams] = useSearchParams()
  const query = params.get("q") ?? ""
  const detailId = useId()
  const scrollPanel = useRef<HTMLDivElement>(null)
  const [showFade, setShowFade] = useState(false)
  const [pointerCardName, setPointerCardName] = useState<string | null>(null)
  const [focusedCardName, setFocusedCardName] = useState<string | null>(null)
  const requestedScope = params.get("scope")
  const scope = requestedScope === "revision" || requestedScope === "deck" ? requestedScope : initialScope
  const zone = readCardZone(params.get("zone"))
  const metric = readCardMetric(params.get("metric"))
  const requestedGameScope = params.get("gameScope")
  const gameScope = requestedGameScope === "one" || requestedGameScope === "postboard" ? requestedGameScope : "all"
  const minimum = params.has("minGames") ? readSampleThreshold(params.get("minGames")) : 0
  const sort: CardSort = params.get("sort") === "games" ? "games" : "winrate"
  const descending = params.get("direction") !== "asc"
  const selectedId = params.get("card")
  const update = (values: Record<string, string | null>) => setParams(previous => {
    const next = new URLSearchParams(previous)
    next.set("panel", "stats")
    for (const [key, value] of Object.entries(values)) {
      if (value == null) next.delete(key); else next.set(key, value)
    }
    return next
  }, { replace: true })
  const request = new URLSearchParams({ scope })
  for (const key of ["opponent", "minDate", "maxDate", "gameScope"]) {
    const value = params.get(key)
    if (value) request.set(key, value)
  }
  const { data, loading, error, refresh, isReady } = useSessionQuery<DeckStatisticsData>(`/api/decks/${revisionId}/statistics?${request}`)
  const roster = useMemo(() => (data?.cards ?? []).filter(row => cardInZone(row, zone)), [data, zone])
  const visualOrder = useMemo(() => {
    if (!cardOrder) return undefined
    const names = zone === "main" ? cardOrder.Mainboard ?? [] : zone === "side" ? cardOrder.Sideboard ?? []
      : [...(cardOrder.Mainboard ?? []), ...(cardOrder.Sideboard ?? [])]
    return new Map([...new Set(names)].map((name, index) => [name, index]))
  }, [cardOrder, zone])
  const cards = useMemo(() => roster.filter(row => (row[metric]?.total ?? 0) >= minimum &&
    (row.card?.name ?? "").toLowerCase().includes(query.trim().toLowerCase()))
    .sort((a, b) => compareCardObservations(a, b, metric, sort, descending, visualOrder)), [roster, metric, minimum, sort, descending, query, visualOrder])
  const emphasizedName = !loading && !error
    ? [pointerCardName, focusedCardName].find(name => name && cards.some(row => row.card?.name === name)) ?? null
    : null
  const highlightedRowName = !loading && !error
    ? [pointerCardName, hoveredDeckCardName, focusedCardName].find(name => name && cards.some(row => row.card?.name === name)) ?? null
    : null
  useEffect(() => {
    const panel = scrollPanel.current
    if (!panel || !hoveredDeckCardName || loading || error || !cards.some(row => row.card?.name === hoveredDeckCardName)) return
    if (panel.clientHeight === 0) return
    const row = Array.from(panel.querySelectorAll<HTMLTableRowElement>("tr[data-statistics-card-name]"))
      .find(element => element.dataset.statisticsCardName === hoveredDeckCardName)
    if (!row) return
    const viewport = panel.getBoundingClientRect(), target = row.getBoundingClientRect()
    const fadeHeight = panel.parentElement?.querySelector<HTMLElement>(".scroll-fade-bottom")?.offsetHeight ?? 0
    const top = viewport.top + panel.clientTop + 4
    const bottom = viewport.top + panel.clientTop + panel.clientHeight - fadeHeight - 4
    const delta = target.top < top ? target.top - top : target.bottom > bottom ? target.bottom - bottom : 0
    if (delta) panel.scrollBy({ top: delta, behavior: window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth" })
  }, [hoveredDeckCardName, cards, loading, error])
  useEffect(() => {
    onCardEmphasisChange?.(emphasizedName)
  }, [emphasizedName, onCardEmphasisChange])
  useEffect(() => () => onCardEmphasisChange?.(null), [onCardEmphasisChange])
  const rowName = (target: EventTarget | null) => target instanceof Element
    ? target.closest<HTMLElement>("[data-statistics-card-name]")?.dataset.statisticsCardName ?? null : null
  useEffect(() => {
    const panel = scrollPanel.current
    if (!panel) return
    const updateFade = () => {
      setShowFade(panel.clientHeight > 0 && panel.scrollTop + panel.clientHeight < panel.scrollHeight - 1)
      if (panel.clientHeight === 0) {
        setPointerCardName(null)
        setFocusedCardName(null)
      }
    }
    updateFade()
    const observer = new ResizeObserver(updateFade)
    observer.observe(panel)
    Array.from(panel.children).forEach(child => observer.observe(child))
    panel.addEventListener("scroll", updateFade, { passive: true })
    return () => { observer.disconnect(); panel.removeEventListener("scroll", updateFade) }
  }, [cards, loading, error])
  const sortBy = (column: CardSort) => update({ sort: column, direction: sort === column && descending ? "asc" : "desc" })
  const activeFilters = Number(metric !== "inHand") + Number(scope === "revision") + Number(gameScope !== "all") + Number(minimum > 0) +
    Number(params.has("opponent")) + Number(params.has("minDate") || params.has("maxDate"))
  const columns = <colgroup><col /><col className="w-10" /><col className="w-[4.5rem]" /></colgroup>

  let comparison: ReactNode = null
  if (loading) {
    comparison = <div className="space-y-2">{Array.from({ length: 6 }, (_, i) => <Skeleton key={i} className="h-14" />)}</div>
  } else if (error) {
    comparison = <StatisticsError message={error} retry={refresh} />
  } else if (!isReady) {
    comparison = <p className="py-4 text-xs text-muted-foreground">Connect to MTGO to view recorded statistics.</p>
  } else if (data && !cards.length) {
    let message: string
    if (!data.games?.total) {
      message = "Play matches with this deck to build its recorded statistics."
    } else if (query) {
      message = "No cards match this search and the current filters."
    } else {
      message = `No cards have ${minimum || "any"} observed games for this observation.`
    }
    comparison = <div className="py-6 text-xs leading-5 text-muted-foreground">
      <p>{message}</p>
      {minimum > 0 && <button type="button" className="mt-2 text-foreground underline underline-offset-2" onClick={() => update({ minGames: "0" })}>Show all samples</button>}
    </div>
  } else if (data) {
    comparison = <table role="presentation" className="w-full table-fixed text-xs">
      {columns}
      <tbody role="rowgroup"
        onPointerOver={event => setPointerCardName(event.buttons === 0 && event.pointerType !== "touch" ? rowName(event.target) : null)}
        onPointerLeave={() => setPointerCardName(null)}
        onFocusCapture={event => setFocusedCardName(event.target.matches(":focus-visible") ? rowName(event.target) : null)}
        onBlurCapture={event => setFocusedCardName(event.relatedTarget instanceof Element && event.relatedTarget.matches(":focus-visible") ? rowName(event.relatedTarget) : null)}>
        {cards.map(row => {
          const id = String(row.card?.catalogId)
          const expanded = selectedId === id
          return <CardComparisonRow key={id} row={row} metric={metric} expanded={expanded}
            emphasized={highlightedRowName === row.card?.name} detailId={`${detailId}-${id}`}
            onSelect={() => update({ card: expanded ? null : id })} />
        })}
      </tbody>
    </table>
  }

  return <section aria-label="Deck statistics" className="flex min-h-0 flex-1 flex-col">
    <div role="tablist" aria-label="Card zone" className="flex shrink-0 gap-3 border-b border-sidebar-border/60"
      onKeyDown={event => {
        if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return
        const buttons = Array.from(event.currentTarget.querySelectorAll<HTMLButtonElement>('[role="tab"]'))
        const index = buttons.indexOf(event.target as HTMLButtonElement)
        if (index < 0) return
        event.preventDefault()
        const next = event.key === "Home" ? 0 : event.key === "End" ? buttons.length - 1
          : (index + (event.key === "ArrowRight" ? 1 : -1) + buttons.length) % buttons.length
        buttons[next].click()
        buttons[next].focus()
      }}>
      {([["main", "Mainboard"], ["side", "Sideboard"], ["all", "All cards"]] as const).map(([value, label]) =>
        <button key={value} type="button" role="tab" id={`${detailId}-tab-${value}`} aria-selected={zone === value}
          aria-controls={`${detailId}-panel`} tabIndex={zone === value ? 0 : -1} onClick={() => update({ zone: value, card: null })}
          className={`-mb-px border-b-2 py-2 text-xs outline-none focus-visible:ring-1 focus-visible:ring-ring ${zone === value ? "border-foreground font-medium text-foreground" : "border-transparent text-muted-foreground hover:text-foreground"}`}>{label}</button>)}
    </div>
    <div className="flex shrink-0 gap-2 py-2">
      <div className="relative min-w-0 flex-1"><Search className="pointer-events-none absolute left-2.5 top-2 h-3.5 w-3.5 text-muted-foreground" />
        <Input aria-label="Find a card in statistics" placeholder="Find a card…" value={query} onChange={event => update({ q: event.target.value || null })} className="h-8 pl-8 pr-7 text-xs" />
        {query && <button type="button" aria-label="Clear card search" onClick={() => update({ q: null })} className="absolute right-1 top-1 rounded p-1 text-muted-foreground hover:text-foreground focus-visible:ring-1 focus-visible:ring-ring"><X className="h-3.5 w-3.5" /></button>}
      </div>
      <Popover>
        <PopoverTrigger asChild><Button type="button" variant="outline" size="sm" aria-label="Card comparison filters" className="h-8 shrink-0 gap-1.5 px-2 text-xs">
          <SlidersHorizontal className="h-3.5 w-3.5" />{cardMetrics.find(([key]) => key === metric)?.[1]}
          {activeFilters > 0 && <span className="rounded bg-muted px-1 text-[10px] tabular-nums" aria-label={`${activeFilters} active filters`}>{activeFilters}</span>}
        </Button></PopoverTrigger>
        <PopoverContent align="end" aria-label="Card comparison filters" className="w-72 max-w-[calc(100vw-2rem)] space-y-3 p-3">
          <div className="flex items-center justify-between"><h3 className="text-sm font-medium">Comparison filters</h3>
            {activeFilters > 0 && <button type="button" className="rounded text-xs text-muted-foreground hover:text-foreground focus-visible:ring-1 focus-visible:ring-ring" onClick={() => update({ metric: "inHand", scope: "deck", gameScope: null, minGames: "0", opponent: null, minDate: null, maxDate: null })}>Reset</button>}
          </div>
          <div className="flex items-center justify-between gap-3"><span className="text-xs text-muted-foreground">Observation</span>
            <Select value={metric} onValueChange={value => update({ metric: value })}>
              <SelectTrigger aria-label="Card observation" className="h-8 w-36 text-xs"><SelectValue /></SelectTrigger>
              <SelectContent>{cardMetrics.map(([key, label]) => <SelectItem key={key} value={key}>{label}</SelectItem>)}</SelectContent>
            </Select>
          </div>
          <div className="flex items-center justify-between gap-3"><span className="text-xs text-muted-foreground">Revisions</span><StatisticsScopeSelect value={scope} onChange={value => update({ scope: value })} /></div>
          <div className="flex items-center justify-between gap-3"><span className="text-xs text-muted-foreground">Games</span><StatisticsGameSelect value={gameScope} onChange={value => update({ gameScope: value })} /></div>
          <div className="flex items-center justify-between gap-3"><span className="text-xs text-muted-foreground">Minimum games</span><StatisticsSampleSelect value={minimum} onChange={value => update({ minGames: String(value) })} className="w-36" /></div>
          {params.has("opponent") && <div className="flex items-center justify-between gap-2 text-xs"><span className="min-w-0 truncate">{params.get("opponent")}</span><button type="button" aria-label="Clear opponent filter" className="rounded p-1 hover:bg-muted focus-visible:ring-1 focus-visible:ring-ring" onClick={() => update({ opponent: null })}><X className="h-3.5 w-3.5" /></button></div>}
          {(params.has("minDate") || params.has("maxDate")) && <div className="flex items-center justify-between gap-2 text-xs"><span>Date range</span><button type="button" aria-label="Clear date filter" className="rounded p-1 hover:bg-muted focus-visible:ring-1 focus-visible:ring-ring" onClick={() => update({ minDate: null, maxDate: null })}><X className="h-3.5 w-3.5" /></button></div>}
          <p className="border-t border-sidebar-border/60 pt-3 text-[11px] leading-4 text-muted-foreground">{cardMetricDescriptions[metric]} Each card counts once per game. Small samples can vary substantially.</p>
        </PopoverContent>
      </Popover>
    </div>
    <div id={`${detailId}-panel`} role="tabpanel" aria-labelledby={`${detailId}-tab-${zone}`} className="flex min-h-0 flex-1 flex-col">
    <div role="table" aria-label="Card performance comparison" className="flex min-h-0 flex-1 flex-col">
      <table role="presentation" className="w-full shrink-0 table-fixed text-xs">
        {columns}
        <thead role="rowgroup" className="bg-background"><tr role="row" className="border-b border-sidebar-border/60 text-[11px] text-muted-foreground">
          <th role="columnheader" className="py-2 pl-1 text-left font-normal">Card</th>
          {([['games', 'Games'], ['winrate', 'Winrate']] as const).map(([column, label]) => <th role="columnheader" key={column} aria-sort={sort === column ? descending ? "descending" : "ascending" : "none"} className="py-2 text-right font-normal">
            <button type="button" onClick={() => sortBy(column)} className="inline-flex items-center justify-end gap-0.5 rounded outline-none focus-visible:ring-1 focus-visible:ring-ring">{label}{sort === column && <ArrowDown className={`h-3 w-3 ${descending ? "" : "rotate-180"}`} />}</button>
          </th>)}
        </tr></thead>
      </table>
    <ScrollArea.Root type="hover" className="relative -mr-3 flex min-h-0 flex-1 flex-col overflow-hidden">
    <ScrollArea.Viewport ref={scrollPanel} onScroll={() => setPointerCardName(null)} role="region" aria-label="Scroll card comparison" tabIndex={0} className="min-h-0 w-full flex-1 pr-3 outline-none focus-visible:ring-1 focus-visible:ring-inset focus-visible:ring-ring">
      {comparison}
    </ScrollArea.Viewport>
    <ScrollArea.Scrollbar orientation="vertical" className="pointer-events-none flex w-2.5 touch-none select-none p-0.5">
      <ScrollArea.Thumb className="pointer-events-auto relative flex-1 cursor-default rounded-full bg-muted-foreground/40 hover:bg-muted-foreground/70" />
    </ScrollArea.Scrollbar>
    {showFade && <div aria-hidden="true" className="scroll-fade-bottom" />}
    </ScrollArea.Root>
    </div>
    </div>
    {data && <div className="flex justify-between gap-3 pt-2 text-[11px] text-muted-foreground"><span>{cards.length} of {roster.length} cards</span><span>{data.recordedGames ?? 0} games recorded</span></div>}
    <Button variant="outline" size="sm" disabled className="mt-2 h-8 shrink-0 justify-between text-xs">View card analysis<ArrowUpRight className="h-3.5 w-3.5" /></Button>
  </section>
}

function CardComparisonRow({ row, metric, expanded, emphasized, detailId, onSelect }: {
  row: DeckCardStatisticsData; metric: CardMetric; expanded: boolean; emphasized: boolean; detailId: string; onSelect: () => void
}) {
  return <>
    <tr role="row" data-statistics-card-name={row.card?.name} data-highlighted={emphasized || undefined} data-state={expanded ? "selected" : undefined} className="cursor-pointer border-b border-sidebar-border/60 hover:bg-muted/30 data-[highlighted=true]:bg-muted/30 data-[state=selected]:bg-muted/50" onClick={onSelect}>
      <td role="cell" className="py-2 pl-1 pr-1"><button type="button" aria-label={`Inspect ${row.card?.name}`} aria-expanded={expanded} aria-controls={expanded ? detailId : undefined}
        onClick={event => { event.stopPropagation(); onSelect() }} className="block w-full rounded text-left outline-none focus-visible:ring-1 focus-visible:ring-ring"><StatisticsCardLabel row={row} emphasized={emphasized} /></button></td>
      <td role="cell" className="py-2 text-right tabular-nums text-muted-foreground">{row[metric]?.total ?? 0}</td>
      <td role="cell" className="py-2 pl-2"><OverallWinrate record={row[metric]} unit="games" /></td>
    </tr>
    {expanded && <tr role="row" data-statistics-card-name={row.card?.name}><td role="cell" colSpan={3} aria-colspan={3} className="border-b border-sidebar-border/60 bg-muted/20 p-2.5"><div id={detailId} className="space-y-3">
      <div className="flex items-center justify-between text-[11px] text-muted-foreground"><span>{row.mainboardCopies ?? 0} main / {row.sideboardCopies ?? 0} side in this revision</span><ChevronDown className="h-3 w-3" /></div>
      {metric === "inHand" && <div><p className="mb-1 text-[11px] text-muted-foreground">Play / Draw</p><PlayDrawComparison play={row.inHandOnPlay} draw={row.inHandOnDraw} compact /></div>}
    </div></td></tr>}
  </>
}
