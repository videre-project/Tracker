/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { ChevronDown } from "lucide-react"

import { Button } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { cn } from "@/lib/utils"
import { getFormatLabel } from "@/utils/formats"

export type GameType = "All" | "Constructed" | "Limited"

const GAME_TYPES: GameType[] = ["All", "Constructed", "Limited"]

export function GameTypeFormatFilter({
  gameType,
  onGameTypeChange,
  selectedFormat,
  formats,
  onFormatChange,
  className,
}: {
  gameType: GameType
  onGameTypeChange: (value: GameType) => void
  selectedFormat: string
  formats: string[]
  onFormatChange: (value: string) => void
  className?: string
}) {
  return (
    <div className={cn("flex items-center gap-2", className)}>
      <div className="flex items-center rounded-lg border border-sidebar-border/60 bg-card p-1">
        {GAME_TYPES.map((type) => (
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
          <Button
            variant="outline"
            size="sm"
            className="h-8 gap-2 border-dashed border-sidebar-border/60"
          >
            <span className="text-muted-foreground">Format:</span>
            <span className="font-medium">
              {selectedFormat ? getFormatLabel(selectedFormat) : "All"}
            </span>
            <ChevronDown className="h-4 w-4 text-muted-foreground" />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end">
          <DropdownMenuItem onClick={() => onFormatChange("")}>All</DropdownMenuItem>
          {formats.map((format) => (
            <DropdownMenuItem key={format} onClick={() => onFormatChange(format)}>
              {getFormatLabel(format)}
            </DropdownMenuItem>
          ))}
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  )
}
