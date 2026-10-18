/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useEffect, useLayoutEffect, useRef, useState } from "react"
import { BookOpen, Search, Swords } from "lucide-react"
import type { DateRange } from "react-day-picker"
import {
  Button,
  Input,
  type DashboardGameType,
  type DeckGalleryItem
} from "@videreproject/ui"
import type { MatchupOverviewData, WinLossRecord } from "@/types/api"
import { useSessionQuery } from "@/hooks/use-session-query"
import { useClientState } from "@/hooks/use-client-state"
import { dateRangeParams } from "@/utils/api-config"
import { MatchupDetails } from "./matchup-details"
import { MatchupDecks } from "./matchup-decks"
import { combineRecords, matchupDeckKey, type DashboardDeckSelection } from "@/utils/match-results"
import { MatchupResults } from "./matchup-results"
import "./matchup-analysis.css"

export function MatchupAnalysis({
  dateRange,
  format,
  gameType,
  selection,
  onSelectionChange,
  archetypeDecks = [],
  refreshKey,
}: {
  dateRange?: DateRange
  format: string
  gameType: DashboardGameType
  selection: DashboardDeckSelection
  onSelectionChange: (selection: DashboardDeckSelection) => void
  archetypeDecks?: DeckGalleryItem[]
  refreshKey?: number
}) {
  const sectionRef = useRef<HTMLElement>(null)
  useLayoutEffect(() => {
    const section = sectionRef.current
    if (!section) return
    const dashboard = section.closest<HTMLElement>('[data-ui-layout="dashboard"]')
    const filters = dashboard?.querySelector<HTMLElement>('[aria-label="Dashboard filters"]')
    const metagame = dashboard?.querySelector<HTMLElement>('[aria-labelledby="dashboard-metagame-title"]')
    const measure = () => {
      // Fit the filters, matchups, and metagame carousel together at the page bottom.
      // This stays constant while scrolling, keeping rows beneath the pointer stable.
      const layoutStyle = getComputedStyle(dashboard ?? section)
      const filterTop = filters ? Number.parseFloat(getComputedStyle(filters).top) || 0 : 0
      const filterHeight = filters?.getBoundingClientRect().height ?? 0
      const gap = Number.parseFloat(layoutStyle.rowGap) || 0
      const padding = Number.parseFloat(layoutStyle.paddingBottom) || 0
      const metagameSpace = metagame ? metagame.getBoundingClientRect().height + gap : 0
      const workspace = section.querySelector<HTMLElement>(".matchup-workspace")
      const comparison = section.querySelector<HTMLElement>(".matchup-comparison")
      // On narrower layouts, also reserve the deck list or details stacked below/above the table.
      const stackedSpace = workspace && comparison
        ? Math.max(0, workspace.getBoundingClientRect().height - comparison.getBoundingClientRect().height)
        : 0
      const available = window.innerHeight - filterTop - filterHeight - gap - metagameSpace - stackedSpace - padding
      section.style.setProperty("--matchup-available-height", `${Math.max(0, available)}px`)
    }
    const observer = new ResizeObserver(measure)
    if (dashboard) observer.observe(dashboard)
    if (filters) observer.observe(filters)
    if (metagame) observer.observe(metagame)
    window.addEventListener("resize", measure)
    measure()
    return () => {
      observer.disconnect()
      window.removeEventListener("resize", measure)
    }
  }, [])
  const [opponentKey, setOpponentKey] = useState<string | null>(null)
  const [search, setSearch] = useState("")
  const [requestedGroupBy, setGroupBy] = useState<"deck" | "archetype">("deck")
  const { state: clientState } = useClientState()
  const range = dateRangeParams(dateRange)
  if (format) range.set("format", format)
  range.set("gameType", gameType)
  const retainDataKey = `${gameType}:${range}`
  range.set("groupBy", requestedGroupBy)
  const { data, dataPath, loading, error, isReady, refresh } = useSessionQuery<MatchupOverviewData>(
    `/api/games/matchups?${range}`, { retainDataKey, refreshKey }
  )
  // Commit the grouping label with its results, keeping the workspace mounted while loading.
  const displayedGrouping = dataPath
    ? new URLSearchParams(dataPath.split("?")[1]).get("groupBy") : requestedGroupBy
  const groupBy = displayedGrouping === "archetype" ? "archetype" : "deck"
  const decks = data?.decks ?? []
  const matchups = data?.matchups ?? []
  const totals = new Map<string, WinLossRecord>()
  for (const row of matchups) {
    if (row.deck) {
      const key = matchupDeckKey(row.deck)
      totals.set(key, combineRecords([totals.get(key), row.matches]))
    }
  }
  // Resolve chart selections within the current grouping and format.
  const rankedDecks = [...decks].sort((left, right) =>
    (totals.get(matchupDeckKey(right))?.total ?? 0) - (totals.get(matchupDeckKey(left))?.total ?? 0) ||
    (left.name ?? "").localeCompare(right.name ?? "") || matchupDeckKey(left).localeCompare(matchupDeckKey(right))
  )
  let selectedDeck = selection?.id != null ? rankedDecks.find(deck => deck.id === selection.id) : undefined
  if (!selectedDeck && selection) {
    const archetype = selection.archetype.trim().toLocaleLowerCase()
    selectedDeck = rankedDecks.find(deck => deck.archetype?.trim().toLocaleLowerCase() === archetype &&
      (!selection.format || deck.format === selection.format))
  }
  selectedDeck ??= rankedDecks[0]
  const selectedId = selectedDeck?.id ?? (groupBy === "archetype" ? selection?.id : undefined)
  const selectedKey = selectedDeck ? matchupDeckKey(selectedDeck) : undefined
  const selectedFormat = selectedDeck?.format ?? undefined
  const selectedArchetype = selectedDeck?.archetype || ""
  useEffect(() => {
    if (!data || loading) return
    if (selectedKey != null) {
      if (selection?.id !== selectedId || selection?.archetype !== selectedArchetype || selection?.format !== selectedFormat)
        onSelectionChange({ id: selectedId, archetype: selectedArchetype, format: selectedFormat })
    } else if (selection != null) {
      onSelectionChange(null)
    }
  }, [data, loading, selectedKey, selectedId, selectedArchetype, selectedFormat, selection, onSelectionChange])
  const selectDeck = (key: string) => {
    const deck = decks.find(deck => matchupDeckKey(deck) === key)
    if (deck) onSelectionChange({ id: deck.id ?? undefined, archetype: deck.archetype || "", format: deck.format ?? undefined })
  }
  const query = search.trim().toLocaleLowerCase()
  const rows = matchups.filter(row => row.deck && matchupDeckKey(row.deck) === selectedKey &&
    (row.opponentArchetype || "Unclassified").toLocaleLowerCase().includes(query))
  const opponents = new Map(rows.map(row => {
    const reference = archetypeDecks.find(deck => deck.format === row.deck?.format && deck.name === row.opponentArchetype) ??
      decks.find(deck => deck.format === row.deck?.format && deck.archetype === row.opponentArchetype)
    return [row.opponentArchetype ?? "", {
      name: row.opponentArchetype || "Unclassified", format: row.deck?.format,
      colors: row.opponentColors ?? reference?.colors, featuredCards: reference?.featuredCards
    }]
  }))
  const selectedRow = rows.find(row => (row.opponentArchetype ?? "").toLowerCase() === opponentKey?.toLowerCase()) ?? rows[0]
  const pending = loading && !data
  let emptyMessage = "No matchups in this range. Try another deck or date range."
  if (!isReady) {
    const connecting = clientState.status === "connecting" || (clientState.processId ?? 0) > 0
    emptyMessage = connecting ? "Waiting for MTGO to finish connecting."
      : "Open MTGO to load your decks and matchup history."
  } else if (query) {
    emptyMessage = "No matchups match your search."
  }

  return (
    <section ref={sectionRef} aria-labelledby="matchup-heading" aria-busy={loading} className="matchup-analysis min-w-0" data-testid="matchup-analysis">
      <h2 id="matchup-heading" className="sr-only">Matchup analysis</h2>
      {error ? (
        <div
          role="alert"
          className="rounded-md border border-sidebar-border/60 p-6 text-sm text-destructive"
        >
          {error}
          <Button size="sm" variant="ghost" onClick={refresh} className="ml-2 h-6 text-xs">
            Try again
          </Button>
        </div>
      ) : (
        <div className={`matchup-workspace ${selectedRow ? "" : "matchup-workspace-empty"}`}
          data-empty={!pending && !decks.length || undefined}>
          <MatchupDecks decks={decks} totals={totals}
            selectedKey={selectedKey} onSelect={selectDeck} groupBy={groupBy} onGroupByChange={setGroupBy} loading={pending} disabled={!isReady} />
          <div className="matchup-comparison flex min-h-0 min-w-0 flex-col overflow-hidden rounded-lg border border-sidebar-border/60 bg-card">
            <div className="matchup-toolbar flex h-12 shrink-0 items-center justify-between gap-3 border-b border-sidebar-border/60 px-3">
              <div className="matchup-report-tabs inline-flex h-12 shrink-0 items-stretch gap-1"
                role="tablist" aria-label="Matchup reports">
                <button type="button" id="matchup-tab-matchups" role="tab"
                  aria-selected="true" aria-controls="matchup-panel"
                  className="-mb-px inline-flex h-12 items-center gap-1.5 border-b-2 border-foreground px-2 text-sm font-medium text-foreground outline-none focus-visible:ring-1 focus-visible:ring-inset focus-visible:ring-ring">
                  <Swords className="h-4 w-4" />Matchups
                </button>
                <button type="button" role="tab" disabled aria-selected="false" aria-label="Sideboard Guides (planned)"
                  className="-mb-px inline-flex h-12 cursor-not-allowed items-center gap-1.5 border-b-2 border-transparent px-2 text-sm font-medium text-muted-foreground/50">
                  <BookOpen className="h-4 w-4" />Sideboard Guides
                </button>
              </div>
              <div className="matchup-search relative w-44 shrink-0">
                <Search className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
                <Input aria-label="Find a matchup" placeholder="Find a matchup…"
                  value={search} onChange={event => setSearch(event.target.value)} disabled={!isReady || !selectedDeck}
                  className="h-8 pl-8 text-xs" />
              </div>
            </div>
            <div id="matchup-panel" role="tabpanel" aria-labelledby="matchup-tab-matchups" tabIndex={0}
              className="flex min-h-0 flex-1 flex-col outline-none focus-visible:ring-1 focus-visible:ring-inset focus-visible:ring-ring">
              <MatchupResults rows={rows} opponents={opponents} selectedKey={selectedRow?.opponentArchetype ?? ""}
                onSelect={setOpponentKey} loading={pending} emptyMessage={emptyMessage} />
            </div>
          </div>
          {selectedRow && <MatchupDetails matchup={selectedRow} opponent={opponents.get(selectedRow.opponentArchetype ?? "")}
            range={dateRangeParams(dateRange)} retainDataKey={JSON.stringify([retainDataKey, selectedId ?? selectedKey, selectedRow.opponentArchetype])}
            refreshKey={refreshKey} />}
        </div>
      )}
    </section>
  )
}
