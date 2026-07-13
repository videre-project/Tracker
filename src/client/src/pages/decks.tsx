"use client"

import { type CSSProperties, useEffect, useMemo, useState } from "react"
import { createPortal } from "react-dom"
import { Link, useSearchParams } from "react-router-dom"
import {
  Layers3,
  Loader2,
  Search,
  SquarePen,
} from "lucide-react"

import {
  CardEntry,
  DeckDetail,
  DeckSummary,
  useDecks,
} from "@/hooks/use-decks"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { useCardArtContext } from "@/components/card-art"
import {
  Card,
  CardContent,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Skeleton } from "@/components/ui/skeleton"
import { cn } from "@/lib/utils"
import { getApiUrl } from "@/utils/api-config"
import { compareFormats, getFormatDotColor } from "@/utils/formats"
import { getManaSymbolSvgPath } from "@/utils/mana-symbols"
import { getDisplayCardColors } from "@/utils/card-colors"

const ALL_FORMATS = "All"

const DECK_TILE_GRID_CLASS = "grid grid-cols-1 gap-4 pt-3 pr-1 lg:grid-cols-2 2xl:grid-cols-3"

type MockDeckStats = {
  winrate: number
  wins: number
  losses: number
}

type FormatSummary = {
  format: string
  deckCount: number
}

function formatFormatName(format?: string) {
  const trimmed = format?.trim()
  if (!trimmed) return "Unspecified"
  if (trimmed === ALL_FORMATS) return ALL_FORMATS
  if (trimmed === trimmed.toLowerCase()) {
    return trimmed.charAt(0).toUpperCase() + trimmed.slice(1)
  }
  return trimmed
}

function hashSeed(value: string) {
  let seed = 0
  for (let i = 0; i < value.length; i++) {
    seed = (seed * 31 + value.charCodeAt(i)) >>> 0
  }
  return seed
}

function buildMockDeckStats(deck: DeckSummary): MockDeckStats {
  const seed = hashSeed(deck.hash || deck.name)
  const winrate = 44 + (seed % 23)
  const matches = 22 + (seed % 15)
  const wins = Math.round(matches * (winrate / 100))

  return {
    winrate,
    wins,
    losses: matches - wins,
  }
}

function normalizeSearchFormat(value?: string | null) {
  const trimmed = (value ?? "").trim().toLowerCase().replace(/\s+/g, " ")
  return trimmed
}

function resolveFilteredFormat(
  queryValue: string | null,
  summaries: FormatSummary[]
) {
  const normalizedQuery = normalizeSearchFormat(queryValue)
  if (!normalizedQuery || normalizedQuery === ALL_FORMATS.toLowerCase()) return ALL_FORMATS

  const exact = summaries.find((summary) =>
    normalizeSearchFormat(summary.format) === normalizedQuery
  )
  if (exact) return exact.format

  const compact = summaries.find((summary) =>
    normalizeSearchFormat(summary.format).replace(/\s/g, "")
      === normalizedQuery.replace(/\s/g, "")
  )
  return compact?.format ?? ALL_FORMATS
}

function FormatLabel({
  format,
  className,
}: {
  format: string
  className?: string
}) {
  return (
    <span className={cn("inline-flex min-w-0 items-center gap-1.5", className)}>
      <span className={cn("h-2 w-2 shrink-0 rounded-full", getFormatDotColor(format))} />
      <span className="truncate">{formatFormatName(format)}</span>
    </span>
  )
}

function getImageUrl(card: CardEntry) {
  return `https://r2.videreproject.com/cards/${card.catalogId}-300px.png`
}

function getFeaturedCards(detail: DeckDetail | null) {
  if (!detail) return []
  return [...detail.mainboard].sort((a, b) => b.quantity - a.quantity).slice(0, 5)
}

function getDeckColors(deck: DeckSummary) {
  const colors = getDisplayCardColors(deck.colors)
  return colors.filter((color, index) => colors.indexOf(color) === index)
}

function ManaSymbols({
  colors,
  className,
}: {
  colors: string[]
  className?: string
}) {
  const visibleColors = getDisplayCardColors(colors)

  return (
    <div className={cn("flex items-center gap-1", className)}>
      {visibleColors.map((color, index) => (
        <img
          key={`${color}-${index}`}
          src={getManaSymbolSvgPath(color) ?? undefined}
          alt={color}
          className="h-5 w-5 rounded-full bg-background shadow-sm ring-1 ring-background"
        />
      ))}
    </div>
  )
}

function FormatStrip({
  summaries,
  selectedFormat,
  onSelect,
  className,
}: {
  summaries: FormatSummary[]
  selectedFormat: string
  onSelect: (format: string) => void
  className?: string
}) {
  return (
    <div className={cn("flex min-w-0 items-center gap-2 overflow-x-auto pb-1", className)}>
      <div className="flex shrink-0 items-center gap-2 pr-1 text-xs font-medium text-muted-foreground">
        <Layers3 className="h-3.5 w-3.5" />
        <span>Formats</span>
      </div>

      <div className="flex shrink-0 items-center rounded-lg border border-sidebar-border/60 bg-card p-1">
        {summaries.map((summary) => {
          const selected = selectedFormat === summary.format

          return (
            <Button
              key={summary.format}
              type="button"
              variant={selected ? "secondary" : "ghost"}
              size="sm"
              onClick={() => onSelect(summary.format)}
              className={cn(
                "h-7 justify-start gap-2 rounded-md px-2.5",
                selected ? "shadow-sm" : "text-muted-foreground hover:text-foreground"
              )}
              aria-pressed={selected}
            >
              <FormatLabel format={summary.format} className="max-w-36" />
              <span
                className={cn(
                  "inline-flex h-4 min-w-4 items-center justify-center rounded-full px-1 text-[10px] font-semibold leading-none tabular-nums",
                  selected ? "bg-background text-foreground" : "bg-muted text-muted-foreground"
                )}
              >
                {summary.deckCount}
              </span>
            </Button>
          )
        })}
      </div>
    </div>
  )
}

function DeckHeroPreview({
  deck,
  cards,
}: {
  deck: DeckSummary
  cards: CardEntry[]
}) {
  const colors = getDeckColors(deck)
  const visibleCards = cards.slice(0, 5)
  const backgroundCard = visibleCards[Math.floor(visibleCards.length / 2)]
  const { getArtUrl, prefetchCards, isReady: clientReady } = useCardArtContext()
  const backgroundArtUrl = backgroundCard ? getArtUrl(backgroundCard.name) : null

  useEffect(() => {
    if (!clientReady || !backgroundCard?.name || backgroundArtUrl) return
    void prefetchCards([backgroundCard.name])
  }, [backgroundArtUrl, backgroundCard?.name, clientReady, prefetchCards])

  return (
    <div className="relative z-0 h-36 overflow-visible rounded-t-lg border-b border-sidebar-border/60">
      <div className="absolute inset-0 overflow-hidden rounded-t-lg bg-muted/25 transition-colors duration-300 group-hover/editor:bg-muted/40">
        {backgroundArtUrl ? (
          <img
            src={backgroundArtUrl}
            alt=""
            aria-hidden="true"
            className="absolute inset-0 h-full w-full scale-110 object-cover object-top opacity-55 blur-sm saturate-125 transition-opacity duration-300 group-hover/editor:opacity-65"
          />
        ) : null}
        <div className="absolute inset-0 bg-background/45" />
        <div className="absolute inset-0 bg-gradient-to-b from-background/25 via-background/5 to-background/45" />
      </div>

      <div className="absolute inset-x-0 bottom-0 top-0 z-10 [clip-path:inset(-4rem_0_1px_0)]">
        {visibleCards.length > 0 ? (
          visibleCards.map((card, index) => {
            const offset = index - (visibleCards.length - 1) / 2
            const distance = Math.abs(offset)
            const restingTransform = `translateX(calc(-50% + ${offset * 20}px)) translateY(${-distance * 3}px) rotate(${offset * 8}deg)`
            const activeTransform = `translateX(calc(-50% + ${offset * 34}px)) translateY(${-16 - distance * 4}px) rotate(${offset * 12}deg)`

            return (
              <img
                key={`${card.catalogId}-${card.name}`}
                src={getImageUrl(card)}
                alt={card.name}
                title={card.name}
                className="absolute bottom-[-10px] left-1/2 h-32 w-[5.7rem] origin-[50%_92%] rounded-sm object-cover shadow-lg ring-1 ring-border/70 transition-[filter,transform] duration-300 ease-out [transform:var(--deck-card-transform)] group-hover/editor:brightness-110 group-hover/editor:[transform:var(--deck-card-active-transform)] group-focus-visible/editor:brightness-110 group-focus-visible/editor:[transform:var(--deck-card-active-transform)]"
                style={{
                  "--deck-card-transform": restingTransform,
                  "--deck-card-active-transform": activeTransform,
                  zIndex: 10 - distance,
                } as CSSProperties}
              />
            )
          })
        ) : (
            <div className="absolute inset-x-6 bottom-3 grid grid-cols-5 gap-2">
            {Array.from({ length: 5 }).map((_, index) => (
              <div
                key={index}
                className="aspect-[5/7] rounded-sm border border-sidebar-border/60 bg-muted/45"
              />
            ))}
          </div>
        )}
      </div>

      <div className="absolute inset-x-0 bottom-0 z-20 h-11 bg-gradient-to-t from-card/90 from-[0%] via-card/45 via-[38%] to-transparent" />
      <div className="absolute inset-x-3 bottom-2 z-30 flex items-center justify-between gap-3">
        <div className="px-2 py-1">
          <ManaSymbols colors={colors} />
        </div>
        <Badge
          variant="secondary"
          className="h-7 max-w-[11rem] justify-start rounded-md border border-sidebar-border/70 bg-background/90 px-2.5 text-[11px] font-medium text-foreground shadow-sm hover:bg-background/90"
        >
          <FormatLabel format={deck.format} />
        </Badge>
      </div>
    </div>
  )
}

function DeckWinrateMetric({ stats }: { stats: MockDeckStats }) {
  return (
    <div
      className="shrink-0 text-right tabular-nums"
      aria-label={`${stats.winrate}% win rate, ${stats.wins}-${stats.losses} record`}
      title="Win rate and record"
    >
      <div className="text-2xl font-bold leading-6 text-foreground">
        {stats.winrate}%
      </div>
      <div className="mt-0.5 text-xs font-medium leading-4 text-muted-foreground">
        {stats.wins}-{stats.losses}
      </div>
    </div>
  )
}

function DeckWinrateBar({ stats }: { stats: MockDeckStats }) {
  return (
    <div
      className="relative flex h-2 w-full overflow-hidden rounded-full bg-muted"
      aria-label={`${stats.winrate}% win rate`}
      title="Win rate"
    >
      <div
        className="h-full bg-emerald-500"
        style={{ width: `${stats.winrate}%` }}
      />
      <div className="h-full flex-1 bg-rose-500/85" />
      <div className="absolute left-1/2 top-0 h-full w-[2px] -translate-x-1/2 bg-background" />
    </div>
  )
}

function DeckGallery({
  decks,
}: {
  decks: DeckSummary[]
}) {
  const [previewCardsByHash, setPreviewCardsByHash] = useState<Record<string, CardEntry[]>>({})

  useEffect(() => {
    const missingDecks = decks
      .filter((deck) => {
        if (deck.featuredCards?.length) return false
        if (Object.prototype.hasOwnProperty.call(previewCardsByHash, deck.hash)) return false
        return true
      })
      .slice(0, 24)

    if (missingDecks.length === 0) return

    const abortController = new AbortController()

    Promise.all(
      missingDecks.map(async (deck) => {
        try {
          const response = await fetch(getApiUrl(`/api/decks/${deck.hash}`), {
            signal: abortController.signal,
          })
          if (!response.ok) return null
          const detail = await response.json() as DeckDetail
          return [deck.hash, getFeaturedCards(detail)] as const
        } catch (error) {
          if (error instanceof Error && error.name === "AbortError") return null
          return null
        }
      })
    ).then((entries) => {
      if (abortController.signal.aborted) return

      setPreviewCardsByHash((current) => {
        const next = { ...current }
        for (const entry of entries) {
          if (!entry) continue
          const [hash, cards] = entry
          next[hash] = cards
        }
        return next
      })
    })

    return () => abortController.abort()
  }, [decks, previewCardsByHash])

  return (
    <div className={DECK_TILE_GRID_CLASS}>
      {decks.map((deck) => {
        const previewCards = deck.featuredCards?.length
          ? deck.featuredCards
          : previewCardsByHash[deck.hash] ?? []

        return (
          <DeckTile
            key={deck.hash}
            deck={deck}
            previewCards={previewCards}
          />
        )
      })}
    </div>
  )
}

function DeckTile({
  deck,
  previewCards,
}: {
  deck: DeckSummary
  previewCards: CardEntry[]
}) {
  const stats = useMemo(() => buildMockDeckStats(deck), [deck])
  const editorPath = `/decks/${encodeURIComponent(deck.hash)}`

  return (
    <Link
      to={editorPath}
      state={{
        deckName: deck.name,
        deckFormat: deck.format,
        deckColors: deck.colors,
      }}
      className="group/editor block min-w-0 outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-ring"
      aria-label={`Open ${deck.name} in deck editor`}
    >
      <Card
        className="group relative min-w-0 overflow-visible border-sidebar-border/60 bg-card transition-colors hover:z-20 hover:border-primary/35 hover:bg-muted/15 focus-within:z-20"
      >
        <DeckHeroPreview
          deck={deck}
          cards={previewCards}
        />

        <span
          className="absolute right-3 top-3 z-30 inline-flex h-8 w-8 items-center justify-center rounded-full border border-sidebar-border/70 bg-background/85 text-foreground opacity-0 shadow-sm backdrop-blur-sm transition-opacity group-hover/editor:opacity-100 group-focus-visible/editor:opacity-100"
          aria-hidden="true"
        >
          <SquarePen className="h-4 w-4" />
        </span>

        <CardContent className="relative z-20 bg-card p-3 pb-2">
          <div className="flex min-w-0 items-center justify-between gap-4">
            <div className="min-w-0">
              <h2 className="truncate text-[15px] font-semibold leading-5">{deck.name}</h2>
              <p className="mt-0.5 truncate text-xs leading-4 text-muted-foreground">
                {deck.archetype || "Unclassified deck"}
              </p>
            </div>
            <DeckWinrateMetric stats={stats} />
          </div>
        </CardContent>
        <div className="relative z-20 rounded-b-lg bg-card px-3 pb-3">
          <DeckWinrateBar stats={stats} />
        </div>
      </Card>
    </Link>
  )
}

function LoadingFormatControls() {
  return (
    <div className="flex min-w-0 items-center gap-2 overflow-hidden pb-1">
      <Skeleton className="h-4 w-20 shrink-0" />
      {Array.from({ length: 5 }).map((_, index) => (
        <Skeleton key={index} className="h-10 w-32 shrink-0 rounded-md" />
      ))}
    </div>
  )
}

function LoadingDeckRows() {
  return (
    <div className="min-h-0 overflow-hidden">
      <div className="border-b border-sidebar-border/60 px-4 py-3">
        <Skeleton className="mb-2 h-4 w-28" />
        <Skeleton className="h-3 w-24" />
      </div>
      <div className={DECK_TILE_GRID_CLASS}>
        {Array.from({ length: 6 }).map((_, index) => (
          <Card key={index} className="overflow-hidden border-sidebar-border/60">
            <Skeleton className="h-36 rounded-none" />
            <CardContent className="p-3 pb-2">
              <div className="flex items-center justify-between gap-3">
                <div className="min-w-0 flex-1">
                  <Skeleton className="mb-2 h-5 w-36 max-w-full" />
                  <Skeleton className="h-3 w-28 max-w-full" />
                </div>
                <Skeleton className="h-7 w-14 shrink-0" />
              </div>
            </CardContent>
            <div className="px-3 pb-3">
              <Skeleton className="h-2 w-full rounded-full" />
            </div>
          </Card>
        ))}
      </div>
    </div>
  )
}

function LoadingDeckLayout() {
  return (
    <div className="flex min-h-0 flex-1 flex-col overflow-hidden">
      <div className="border-b border-sidebar-border/60 pb-3">
        <LoadingFormatControls />
      </div>
      <LoadingDeckRows />
    </div>
  )
}

export default function Decks() {
  const [searchParams, setSearchParams] = useSearchParams()
  const { decks, loading, error } = useDecks()
  const [selectedFormat, setSelectedFormat] = useState(ALL_FORMATS)
  const [query, setQuery] = useState("")

  const formats = useMemo(
    () => Object.keys(decks).filter(Boolean).sort(compareFormats),
    [decks]
  )

  const allDecks = useMemo(
    () => formats.flatMap((format) => decks[format] ?? []),
    [decks, formats]
  )

  const summaries = useMemo<FormatSummary[]>(() => {
    const buildSummary = (format: string, formatDecks: DeckSummary[]) => {
      return {
        format,
        deckCount: formatDecks.length,
      }
    }

    return [
      buildSummary(ALL_FORMATS, allDecks),
      ...formats.map((format) => buildSummary(format, decks[format] ?? [])),
    ]
  }, [allDecks, decks, formats])

  useEffect(() => {
    if (summaries.length === 0) return

    const nextFormat = resolveFilteredFormat(searchParams.get("format"), summaries)
    if (nextFormat !== selectedFormat) {
      setSelectedFormat(nextFormat)
    }
  }, [searchParams, selectedFormat, summaries])

  const visibleDecks = useMemo(() => {
    const source = selectedFormat === ALL_FORMATS
      ? allDecks
      : decks[selectedFormat] ?? []
    const normalizedQuery = query.trim().toLowerCase()

    if (!normalizedQuery) return source

    return source.filter((deck) => {
      const searchable = [
        deck.name,
        deck.format,
        deck.archetype ?? "",
        ...(deck.colors ?? []),
      ].join(" ").toLowerCase()

      return searchable.includes(normalizedQuery)
    })
  }, [allDecks, decks, query, selectedFormat])

  useEffect(() => {
    if (summaries.length === 0) return
    if (selectedFormat === ALL_FORMATS) return

    const availableFormats = new Set(summaries.map((summary) => summary.format))
    if (!availableFormats.has(selectedFormat)) {
      setSelectedFormat(ALL_FORMATS)
      setSearchParams((previous) => {
        const next = new URLSearchParams(previous)
        next.delete("format")
        return next
      }, { replace: true })
    }
  }, [selectedFormat, summaries])

  const onFormatSelect = (format: string) => {
    setSelectedFormat(format)
    setSearchParams((previous) => {
      const next = new URLSearchParams(previous)
      if (format === ALL_FORMATS) {
        next.delete("format")
      } else {
        next.set("format", format)
      }

      return next
    }, { replace: true })
  }

  const totalDecks = allDecks.length
  const totalFormats = formats.length
  const breadcrumbContextHost = typeof document === "undefined"
    ? null
    : document.getElementById("page-header-context")

  const renderBreadcrumbDecksContext = () => (
    <div className="inline-flex min-w-0 items-center gap-2 text-xs text-muted-foreground">
      {loading ? (
        <Loader2 className="h-3.5 w-3.5 animate-spin" aria-label="Loading decks" />
      ) : (
        <>
          <span>{totalDecks.toLocaleString()} deck{totalDecks === 1 ? "" : "s"}</span>
          <span className="h-1 w-1 rounded-full bg-muted-foreground/45" />
          <span>{totalFormats.toLocaleString()} format{totalFormats === 1 ? "" : "s"}</span>
        </>
      )}
    </div>
  )

  return (
    <div className="flex h-[calc(100vh-2.5rem)] min-h-0 flex-col gap-2 overflow-hidden px-4 pb-4 pt-1">
      {breadcrumbContextHost ? createPortal(
        renderBreadcrumbDecksContext(),
        breadcrumbContextHost
      ) : null}

      {error && (
        <div className="rounded-md border border-destructive/30 bg-destructive/10 px-4 py-3 text-sm text-destructive">
          Error loading decks: {error}
        </div>
      )}

      {loading ? (
        <LoadingDeckLayout />
      ) : allDecks.length === 0 ? (
        <div className="flex flex-1 items-center justify-center rounded-lg border border-sidebar-border/60 bg-muted/25 text-sm text-muted-foreground">
          No decks found in the database.
        </div>
      ) : (
        <div className="flex min-h-0 flex-1 flex-col overflow-hidden">
          <div>
            <div className="flex flex-col gap-3 lg:flex-row lg:items-center">
              <FormatStrip
                summaries={summaries}
                selectedFormat={selectedFormat}
                onSelect={onFormatSelect}
                className="lg:flex-1"
              />

              <div className="flex min-w-0 flex-col gap-2 sm:flex-row sm:items-center lg:shrink-0">
                <div className="relative min-w-0 sm:w-72">
                  <Search className="pointer-events-none absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
                  <Input
                    value={query}
                    onChange={(event) => setQuery(event.target.value)}
                    placeholder="Search decks"
                    className="h-9 pl-8"
                  />
                </div>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => setQuery("")}
                  disabled={!query}
                >
                  Clear
                </Button>
              </div>
            </div>
          </div>

          <main className="min-h-0 overflow-y-auto">
            {visibleDecks.length === 0 ? (
              <div className="flex min-h-72 items-center justify-center p-6 text-sm text-muted-foreground">
                No decks match the current filters.
              </div>
            ) : (
              <DeckGallery
                decks={visibleDecks}
              />
            )}
          </main>
        </div>
      )}
    </div>
  )
}
