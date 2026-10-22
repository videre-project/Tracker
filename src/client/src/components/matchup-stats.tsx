/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { DeckWinrateBar } from "@videreproject/ui"
import type { MatchupData, WinLossRecord } from "@/types/api"
import { rateText, recordText } from "@/utils/match-results"

export function OverallWinrate({ record, unit = "matches" }: {
  record?: WinLossRecord
  unit?: "matches" | "games"
}) {
  return (
    <div
      className="flex min-w-0 flex-col whitespace-nowrap tabular-nums"
      title={`${recordText(record)} over ${record?.total ?? 0} ${unit}`}
    >
      <span className="text-right text-sm font-medium leading-5">{rateText(record)}</span>
      <div className="mt-1 flex h-2 items-center">
        <DeckWinrateBar
          stats={{
            matches: record?.total ?? 0,
            wins: record?.wins ?? 0,
            losses: record?.losses ?? 0,
            ties: record?.draws ?? 0,
            winrate: record?.winrate ?? null
          }}
        />
      </div>
    </div>
  )
}

export function PlayDrawComparison({ play, draw, compact = false }: {
  play?: WinLossRecord
  draw?: WinLossRecord
  compact?: boolean
}) {
  const rates = [play?.winrate ?? null, draw?.winrate ?? null]
  const description = `Play: ${recordText(play)} over ${play?.total ?? 0} games. Draw: ${recordText(draw)} over ${draw?.total ?? 0} games. Shared 0–100% scale; center tick is 50%.`
  const format = (value: number | null) => value == null ? "—" : `${value.toFixed(1)}%`
  return (
    <div className={`grid items-center text-xs leading-5 tabular-nums ${compact
      ? "grid-cols-[1.75rem_minmax(0,1fr)_3rem] gap-x-1"
      : "grid-cols-[2rem_minmax(0,1fr)_3.5rem] gap-x-2"}`}>
      <span className="text-muted-foreground">Play</span>
      <div className="relative row-span-2 h-10 min-w-0" role="img" aria-label={description} title={description}>
        <svg className="absolute inset-0 h-full w-full overflow-visible" viewBox="0 0 100 40" preserveAspectRatio="none" aria-hidden="true">
          <path d="M0 10H100 M0 30H100" fill="none" stroke="hsl(var(--muted-foreground) / 0.2)" vectorEffect="non-scaling-stroke" />
          <path d="M50 4V36" stroke="hsl(var(--muted-foreground) / 0.45)" vectorEffect="non-scaling-stroke" />
          {rates[0] != null && rates[1] != null && (
            <line x1={rates[0]} y1="10" x2={rates[1]} y2="30" stroke="hsl(var(--muted-foreground) / 0.6)" vectorEffect="non-scaling-stroke" />
          )}
        </svg>
        {rates.map((rate, index) => rate == null ? null : (
          <span key={index} aria-hidden="true"
            className={`absolute h-1.5 w-1.5 -translate-x-1/2 -translate-y-1/2 rounded-full ${rate >= 50 ? "bg-emerald-500" : "bg-rose-500"}`}
            style={{ left: `${rate}%`, top: index === 0 ? "25%" : "75%" }} />
        ))}
      </div>
      <span className="text-right" title={`Play: ${recordText(play)} over ${play?.total ?? 0} games`}>{format(play?.winrate ?? null)}</span>
      <span className="text-muted-foreground">Draw</span>
      <span className="text-right" title={`Draw: ${recordText(draw)} over ${draw?.total ?? 0} games`}>{format(draw?.winrate ?? null)}</span>
    </div>
  )
}

export type GamePerformanceSummary = Pick<MatchupData,
  "gameOne" | "postboard" | "gameOneOnPlay" | "gameOneOnDraw" | "postboardOnPlay" | "postboardOnDraw">

export function GamePerformance({ summary, headingLevel = 3 }: {
  summary: GamePerformanceSummary
  headingLevel?: 2 | 3
}) {
  const Heading = headingLevel === 2 ? "h2" : "h3"
  return (
    <section aria-label="Game performance" className="min-w-0 overflow-x-auto">
      <table aria-label="Game performance" className="w-full min-w-[22rem] text-xs">
        <colgroup><col className="w-px" /><col className="w-[36%]" /><col /></colgroup>
        <thead>
          <tr className="border-b border-sidebar-border/60 text-muted-foreground">
            <th scope="col" className="relative h-7 pb-2 text-left align-top font-normal">
              {/* Keep the section heading out of the game-label column's intrinsic width. */}
              <Heading className="absolute left-0 top-0 whitespace-nowrap text-sm font-medium text-foreground">Game performance</Heading>
            </th>
            <th scope="col" className="pb-2 pl-3 text-right align-top font-normal">Winrate</th>
            <th scope="col" className="pb-2 pl-4 text-left align-top font-normal">Play / Draw</th>
          </tr>
        </thead>
        <tbody>
          {([
            ["Game 1", summary.gameOne, summary.gameOneOnPlay, summary.gameOneOnDraw],
            ["Games 2–3", summary.postboard, summary.postboardOnPlay, summary.postboardOnDraw]
          ] as const).map(([label, record, play, draw]) => (
            <tr key={label} aria-label={label}>
              <th scope="row" className="whitespace-nowrap py-2 text-left font-normal">
                <span className="block">{label}</span>
                <span className="mt-1 block text-[11px] tabular-nums text-muted-foreground">
                  {record?.total ?? 0} games
                </span>
              </th>
              <td className="py-2 pl-3"><OverallWinrate record={record} unit="games" /></td>
              <td className="py-2 pl-4"><PlayDrawComparison play={play} draw={draw} compact /></td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  )
}
