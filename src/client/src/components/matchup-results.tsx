/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useState } from "react"
import { ArrowDown, ArrowUp } from "lucide-react"
import {
  Table,
  TableBody,
  TableBodySkeleton,
  TableCell,
  TableHead,
  TableHeader,
  TableRow
} from "@videreproject/ui"
import type { MatchupData } from "@/types/api"
import { recordText } from "@/utils/match-results"

import { MatchupDeckLabel, type DeckIdentity } from "./matchup-deck-label"
import { OverallWinrate, PlayDrawComparison } from "./matchup-stats"

export function MatchupResults({
  rows,
  opponents,
  selectedKey,
  onSelect,
  loading = false,
  emptyMessage = "No matchups in this range. Try another deck or date range."
}: {
  rows: MatchupData[]
  opponents: ReadonlyMap<string, DeckIdentity>
  selectedKey?: string
  onSelect: (key: string) => void
  loading?: boolean
  emptyMessage?: string
}) {
  const [sort, setSort] = useState<{
    field: "matches" | "overall" | "onPlay" | "onDraw"
    descending: boolean
  }>({
    field: "matches",
    descending: true
  })
  const showFormat = new Set(rows.map((row) => row.deck?.format)).size > 1
  const sorted = [...rows].sort((left, right) => {
    let comparison: number
    if (sort.field === "matches") {
      comparison = (left.matches?.total ?? 0) - (right.matches?.total ?? 0)
    } else {
      const a = left[sort.field === "overall" ? "matches" : sort.field]?.winrate
      const b = right[sort.field === "overall" ? "matches" : sort.field]?.winrate
      // Unknown results stay below measured results in either direction.
      if (a == null || b == null) return a == null ? (b == null ? 0 : 1) : -1
      comparison = a - b
    }
    return (
      comparison * (sort.descending ? -1 : 1) ||
      (right.matches?.total ?? 0) - (left.matches?.total ?? 0) ||
      (left.opponentArchetype ?? "").localeCompare(right.opponentArchetype ?? "")
    )
  })
  const sortHeader = (field: typeof sort.field, label: string) => (
    <button
      type="button"
      disabled={!rows.length}
      className="inline-flex items-center gap-1 hover:text-foreground disabled:pointer-events-none disabled:opacity-50"
      onClick={() => setSort({ field, descending: sort.field === field ? !sort.descending : true })}
    >
      {label}
      {sort.field === field &&
        (sort.descending ? <ArrowDown className="h-3 w-3" /> : <ArrowUp className="h-3 w-3" />)}
    </button>
  )
  return (
    <Table
      aria-label="Matchup results"
      className="min-w-[38rem] table-fixed [&_tbody_tr]:h-14"
      wrapperClassName="data-table-scroll min-h-0 flex-1 overflow-auto"
    >
      <colgroup>
        <col />
        <col style={{ width: 80 }} />
        <col style={{ width: 140 }} />
        <col style={{ width: 196 }} />
      </colgroup>
      <TableHeader className="sticky top-0 z-10 bg-card">
        <TableRow className="hover:bg-transparent">
          <TableHead className="matchup-opponent-cell pl-3 text-xs">Opponent</TableHead>
          <TableHead
            className="w-20 text-right text-xs"
            aria-sort={
              sort.field === "matches" ? (sort.descending ? "descending" : "ascending") : "none"
            }
          >
            {sortHeader("matches", "Matches")}
          </TableHead>
          <TableHead
            className="text-xs"
            title="Overall match winrate"
            aria-sort={
              sort.field === "overall" ? (sort.descending ? "descending" : "ascending") : "none"
            }
          >
            {sortHeader("overall", "Overall Winrate")}
          </TableHead>
          <TableHead
            className="pr-3 text-xs"
            title="Play and draw game winrates on the same 0–100% scale. The center tick marks 50%."
            aria-sort={
              sort.field === "onPlay" || sort.field === "onDraw"
                ? (sort.descending ? "descending" : "ascending")
                : "none"
            }
          >
            <div className="flex items-center gap-1">
              {sortHeader("onPlay", "Play")}
              <span className="text-muted-foreground" aria-hidden="true">/</span>
              {sortHeader("onDraw", "Draw")}
            </div>
          </TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        {loading ? <TableBodySkeleton rows={5} columns={4} /> : !sorted.length && (
          <TableRow>
            <TableCell colSpan={4} className="p-3 text-xs text-muted-foreground">
              <p role="status" className="sticky left-3 w-fit max-w-[calc(100cqw-1.5rem)] leading-relaxed">{emptyMessage}</p>
            </TableCell>
          </TableRow>
        )}
        {!loading && sorted.map((row) => {
          const key = row.opponentArchetype ?? ""
          const opponent = opponents.get(key) ?? { name: key || "Unclassified" }
          const stats = row.matches
          const selected = key === selectedKey
          return (
            <TableRow
              key={key}
              data-state={selected ? "selected" : undefined}
              className="matchup-result-row cursor-pointer"
              onClick={() => onSelect(key)}
            >
              <TableCell className="matchup-opponent-cell py-2 pl-3">
                <button
                  type="button"
                  aria-label={`Select matchup against ${opponent.name} (${row.deck?.format})`}
                  aria-pressed={selected}
                  onClick={(event) => {
                    event.stopPropagation()
                    onSelect(key)
                  }}
                  className="block w-full rounded outline-none focus-visible:ring-1 focus-visible:ring-ring"
                >
                  <MatchupDeckLabel deck={opponent} showFormat={showFormat} selected={selected} />
                </button>
              </TableCell>
              <TableCell className="text-right text-xs tabular-nums text-muted-foreground">{stats?.total ?? 0}</TableCell>
              <TableCell
                className="text-right"
                title={`${recordText(stats)} over ${stats?.total ?? 0} matches`}
              >
                <OverallWinrate record={stats} />
              </TableCell>
              <TableCell className="pr-3">
                <PlayDrawComparison play={row.onPlay} draw={row.onDraw} />
              </TableCell>
            </TableRow>
          )
        })}
      </TableBody>
    </Table>
  )
}
