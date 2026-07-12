/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import type { DateRange } from "react-day-picker"
import { ChevronDown } from "lucide-react"

import { Button } from "@/components/ui/button"
import { DatePickerWithRange } from "@/components/ui/date-range-picker"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { cn } from "@/lib/utils"

export type DashboardGameType = "All" | "Constructed" | "Limited"

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
      <div className="flex items-center gap-2">
        <div className="flex items-center rounded-lg border border-sidebar-border/60 bg-card p-1">
          {(["All", "Constructed", "Limited"] as const).map(type => (
            <Button
              key={type}
              variant={gameType === type ? "secondary" : "ghost"}
              size="sm"
              onClick={() => onGameTypeChange(type)}
              className={cn(
                "h-6 rounded-md px-3",
                gameType === type ? "shadow-sm" : "text-muted-foreground hover:text-foreground"
              )}
            >
              {type}
            </Button>
          ))}
        </div>

        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="outline" size="sm" className="h-8 gap-2 border-dashed border-sidebar-border/60">
              <span className="text-muted-foreground">Format:</span>
              <span className="font-medium">{selectedFormat || "All"}</span>
              <ChevronDown className="h-4 w-4 text-muted-foreground" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem onClick={() => onFormatChange("")}>All</DropdownMenuItem>
            {formats.map(format => (
              <DropdownMenuItem key={format} onClick={() => onFormatChange(format)}>
                {format}
              </DropdownMenuItem>
            ))}
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

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
