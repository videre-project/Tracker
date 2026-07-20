/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import type { DateRange } from "react-day-picker"

import { DatePickerWithRange } from "@/components/ui/date-range-picker"
import {
  GameTypeFormatFilter,
  type GameType,
} from "@/components/game-type-format-filter"

export type DashboardGameType = GameType

const datePresets = [
  { label: "All Time", getValue: () => undefined },
  { label: "Today", getValue: () => { const today = new Date(); return { from: today, to: today } } },
  { label: "Yesterday", getValue: () => { const date = new Date(); date.setDate(date.getDate() - 1); return { from: date, to: date } } },
  { label: "Last 7 Days", getValue: () => rangeEndingToday(7) },
  { label: "Last 30 Days", getValue: () => rangeEndingToday(30) },
  { label: "Last 90 Days", getValue: () => rangeEndingToday(90) },
]

function rangeEndingToday(days: number) {
  const to = new Date()
  const from = new Date()
  from.setDate(to.getDate() - days)
  return { from, to }
}

export function DashboardFilters({
  gameType,
  onGameTypeChange,
  selectedFormat,
  formats,
  onFormatChange,
  dateRange,
  onDateRangeChange,
}: {
  gameType: DashboardGameType
  onGameTypeChange: (value: DashboardGameType) => void
  selectedFormat: string
  formats: string[]
  onFormatChange: (value: string) => void
  dateRange?: DateRange
  onDateRangeChange: (range: DateRange | undefined) => void
}) {
  return (
    <div className="mt-2 flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
      <GameTypeFormatFilter
        gameType={gameType}
        onGameTypeChange={onGameTypeChange}
        selectedFormat={selectedFormat}
        formats={formats}
        onFormatChange={onFormatChange}
      />

      <DatePickerWithRange
        date={dateRange}
        setDate={onDateRangeChange}
        size="sm"
        className="justify-start border-dashed border-sidebar-border/60 text-left font-normal"
        presets={datePresets}
      />
    </div>
  )
}
