/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useState } from "react"
import { ScrollArea } from "radix-ui"
import { ArrowDown, ArrowUp, Search } from "lucide-react"
import { Input, Table, TableBody, TableBodySkeleton, TableCell, TableHead, TableHeader, TableRow } from "@videreproject/ui"
import type { DeckReferenceData, WinLossRecord } from "@/types/api"
import { MatchupDeckLabel } from "./matchup-deck-label"
import { OverallWinrate } from "./matchup-stats"
import { matchupDeckKey } from "@/utils/match-results"

export function MatchupDecks({ decks, totals, selectedKey, onSelect, groupBy, onGroupByChange, loading = false, disabled = false }: {
  decks: DeckReferenceData[]
  totals: ReadonlyMap<string, WinLossRecord>
  selectedKey?: string
  onSelect: (key: string) => void
  groupBy: "deck" | "archetype"
  onGroupByChange: (groupBy: "deck" | "archetype") => void
  loading?: boolean
  disabled?: boolean
}) {
  const [search, setSearch] = useState("")
  const [sort, setSort] = useState<{ field: "matches" | "overall"; descending: boolean }>({
    field: "matches", descending: true
  })
  const query = search.trim().toLocaleLowerCase()
  const multipleFormats = new Set(decks.map(deck => deck.format)).size > 1
  const visibleDecks = decks.filter(deck => [deck.name, deck.archetype].some(label => label?.toLocaleLowerCase().includes(query)))
    .sort((left, right) => {
      const a = totals.get(matchupDeckKey(left))
      const b = totals.get(matchupDeckKey(right))
      let comparison: number
      if (sort.field === "matches") {
        comparison = (a?.total ?? 0) - (b?.total ?? 0)
      } else {
        const leftRate = a?.winrate
        const rightRate = b?.winrate
        if (leftRate == null || rightRate == null)
          return leftRate == null ? (rightRate == null ? (left.name ?? "").localeCompare(right.name ?? "") : 1) : -1
        comparison = leftRate - rightRate
      }
      return comparison * (sort.descending ? -1 : 1)
        || (b?.total ?? 0) - (a?.total ?? 0)
        || (left.name ?? "").localeCompare(right.name ?? "")
    })
  const sortHeader = (field: typeof sort.field, label: string) => (
    <button type="button" disabled={!visibleDecks.length}
      className="inline-flex items-center gap-1 hover:text-foreground disabled:pointer-events-none disabled:opacity-50"
      onClick={() => setSort({ field, descending: sort.field === field ? !sort.descending : true })}>
      {label}
      {sort.field === field && (sort.descending
        ? <ArrowDown className="h-3 w-3" /> : <ArrowUp className="h-3 w-3" />)}
    </button>
  )
  const renderRow = (deck: typeof decks[number]) => {
    const key = matchupDeckKey(deck)
    const record = totals.get(key)
    const selected = key === selectedKey
    const select = () => onSelect(key)
    return (
      <TableRow key={key} data-state={selected ? "selected" : undefined}
        className="matchup-deck-row cursor-pointer" onClick={select}>
        <TableCell className="py-2 pl-3">
          <button type="button" aria-label={`Show matchups for ${deck.name}${multipleFormats ? ` (${deck.format})` : ""}`}
            aria-pressed={selected} className="block w-full rounded text-left text-xs outline-none focus-visible:ring-1 focus-visible:ring-ring"
            onClick={event => { event.stopPropagation(); select() }}>
            <MatchupDeckLabel deck={deck} showArchetype={groupBy === "deck" || deck.id != null} selected={selected} />
          </button>
        </TableCell>
        <TableCell className="text-right text-xs tabular-nums text-muted-foreground">{record?.total ?? 0}</TableCell>
        <TableCell className="pr-3 text-right"><OverallWinrate record={record} /></TableCell>
      </TableRow>
    )
  }
  return (
    <aside aria-label="Filter by your deck" className="matchup-decks flex min-h-0 min-w-0 flex-col border-y border-transparent">
      <div className="flex h-12 shrink-0 items-center gap-2 border-b border-transparent py-2">
        <div className="relative min-w-0 flex-1">
          <Search className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground" />
          <Input aria-label={groupBy === "deck" ? "Find your deck" : "Find an archetype"} placeholder={groupBy === "deck" ? "Find your deck…" : "Find an archetype…"} value={search}
            onChange={event => setSearch(event.target.value)} disabled={disabled || loading} className="h-8 pl-8 text-xs" />
        </div>
        <div role="group" aria-label="Group matchups by" className="inline-flex h-8 shrink-0 items-center rounded-md border border-sidebar-border/70 bg-background/70 p-0.5">
          {([['deck', 'Decks'], ['archetype', 'Archetypes']] as const).map(([value, label]) =>
            <button key={value} type="button" aria-pressed={groupBy === value}
              disabled={disabled || loading} onClick={() => onGroupByChange(value)}
              className={`h-6 rounded-sm px-2 text-xs font-medium leading-none outline-none transition-colors focus-visible:ring-1 focus-visible:ring-ring disabled:pointer-events-none disabled:opacity-50 ${groupBy === value ? "bg-secondary text-secondary-foreground" : "text-muted-foreground hover:text-foreground"}`}>
              {label}
            </button>)}
        </div>
      </div>
      <ScrollArea.Root type="hover" className="relative min-h-0 flex-1">
        <ScrollArea.Viewport role="region" aria-label="Your decks" tabIndex={0}
          className="h-full w-full outline-none focus-visible:ring-1 focus-visible:ring-inset focus-visible:ring-ring">
          <Table aria-label="Your decks" className="table-fixed [&_tbody_tr]:h-14" wrapperClassName="overflow-visible">
            <colgroup><col /><col style={{ width: 80 }} /><col style={{ width: 140 }} /></colgroup>
            <TableHeader className="sticky top-0 z-10 bg-background">
              <TableRow className="hover:bg-transparent">
                <TableHead className="pl-3 text-xs">{groupBy === "deck" ? "Deck" : "Archetype"}</TableHead>
                <TableHead className="text-right text-xs" aria-sort={sort.field === "matches" ? (sort.descending ? "descending" : "ascending") : "none"}>
                  {sortHeader("matches", "Matches")}
                </TableHead>
                <TableHead className="pr-3 text-xs" aria-sort={sort.field === "overall" ? (sort.descending ? "descending" : "ascending") : "none"}>
                  {sortHeader("overall", "Overall Winrate")}
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {loading ? <TableBodySkeleton rows={5} columns={3} /> : visibleDecks.length
                ? visibleDecks.map(deck => renderRow(deck))
                : <TableRow><TableCell colSpan={3} className="p-3 text-xs text-muted-foreground">
                  {query ? groupBy === "deck" ? "No decks found." : "No archetypes found."
                    : disabled ? "No decks loaded." : "No saved decks for this format."}
                </TableCell></TableRow>}
            </TableBody>
          </Table>
        </ScrollArea.Viewport>
        {/* Keep the overlay thumb in the panel gap, clear of the winrate values. */}
        <ScrollArea.Scrollbar orientation="vertical"
          className="pointer-events-none flex w-2.5 translate-x-full touch-none select-none p-0.5">
          <ScrollArea.Thumb className="pointer-events-auto relative flex-1 cursor-default rounded-full bg-muted-foreground/40 hover:bg-muted-foreground/70" />
        </ScrollArea.Scrollbar>
      </ScrollArea.Root>
    </aside>
  )
}
