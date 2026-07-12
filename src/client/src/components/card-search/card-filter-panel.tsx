/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { X } from "lucide-react"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { cn } from "@/lib/utils"
import {
  CARD_COLORS,
  CARD_COLOR_MODES,
  CARD_FORMATS,
  CARD_LEGALITIES,
  CARD_RARITIES,
  CARD_SEARCH_TEXT_MODES,
  CARD_TYPE_FILTERS,
  type CardBooleanMode,
  type CardColor,
  type CardColorMode,
  type CardComparisonOperator,
  type CardFilterState,
  type CardFormatFilter,
  type CardLegalityFilter,
  type CardRarityFilter,
  type CardSearchTextMode,
  type CardTokenFilterMode,
  type CardTypeFilter,
  type CardTypeFilterState,
  titleCaseCardFilter,
} from "./card-search-model"

type CardFilterPanelProps = {
  filters: CardFilterState
  activeFilterCount: number
  onUpdate: (patch: Partial<CardFilterState>) => void
  onClear: () => void
  onClose: () => void
  title?: string
  titleId?: string
  closeLabel?: string
  className?: string
}

export function CardFilterPanel({
  filters,
  activeFilterCount,
  onUpdate,
  onClear,
  onClose,
  title = "Card query",
  titleId,
  closeLabel = "Close query builder",
  className,
}: CardFilterPanelProps) {
  const sectionClass = "space-y-2 border-b border-sidebar-border/45 pb-3 last:border-b-0"
  const labelClass = "text-[11px] font-medium uppercase tracking-wide text-muted-foreground"
  const controlClass = "h-8 border-sidebar-border/60 bg-background/55 text-xs"

  const toggleColor = (color: CardColor) => {
    const colors = filters.colors.includes(color)
      ? filters.colors.filter(current => current !== color)
      : [...filters.colors, color]
    onUpdate({
      colors,
      colorMode: colors.length === 0 ? "any" : filters.colorMode === "any" ? "includes" : filters.colorMode,
    })
  }

  const cycleType = (type: CardTypeFilter) => {
    const current = filters.typeStates[type] ?? "off"
    const next: CardTypeFilterState = current === "off" ? "include" : current === "include" ? "exclude" : "off"
    onUpdate({ typeStates: { ...filters.typeStates, [type]: next } })
  }

  const updateBoolean = (
    key: "promoMode" | "multifaceMode" | "splitMode",
    value: CardBooleanMode
  ) => onUpdate({ [key]: value })

  return (
    <div className={cn("flex min-h-0 flex-1 flex-col overflow-hidden", className)}>
      <div className="flex items-start justify-between gap-3 border-b border-sidebar-border/60 px-3 py-2.5">
        <div className="flex min-w-0 flex-1 items-center gap-2">
          <div id={titleId} className="text-sm font-semibold text-foreground">{title}</div>
          {activeFilterCount > 0 ? (
            <Badge variant="secondary" className="rounded-md px-2 text-[11px]">
              {activeFilterCount} filter{activeFilterCount === 1 ? "" : "s"}
            </Badge>
          ) : null}
        </div>
        <Button
          variant="ghost"
          size="icon"
          onClick={onClose}
          className="-mr-1 -mt-1 h-8 w-8 shrink-0"
          aria-label={closeLabel}
        >
          <X className="h-4 w-4" />
        </Button>
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto px-3 py-2">
        <div className="grid grid-cols-1 gap-y-3">
          <section className={sectionClass}>
            <div className={labelClass}>Search text</div>
            <Select value={filters.textMode} onValueChange={value => onUpdate({ textMode: value as CardSearchTextMode })}>
              <SelectTrigger className={cn(controlClass, "w-full")}><SelectValue /></SelectTrigger>
              <SelectContent>
                {CARD_SEARCH_TEXT_MODES.map(mode => <SelectItem key={mode.value} value={mode.value}>{mode.label}</SelectItem>)}
              </SelectContent>
            </Select>
          </section>

          <section className={sectionClass}>
            <div className={labelClass}>Color</div>
            <div className="flex items-center gap-2">
              <Select value={filters.colorMode} onValueChange={value => onUpdate({ colorMode: value as CardColorMode })}>
                <SelectTrigger className={cn(controlClass, "w-32 shrink-0")}><SelectValue /></SelectTrigger>
                <SelectContent>
                  {CARD_COLOR_MODES.map(mode => <SelectItem key={mode.value} value={mode.value}>{mode.label}</SelectItem>)}
                </SelectContent>
              </Select>
              <div className="flex min-w-0 flex-wrap items-center gap-1.5">
                {CARD_COLORS.map(color => {
                  const selected = filters.colors.includes(color)
                  return (
                    <button
                      key={color}
                      type="button"
                      onClick={() => toggleColor(color)}
                      aria-pressed={selected}
                      aria-label={`${color} cards`}
                      className={cn(
                        "flex h-8 min-w-8 items-center justify-center rounded-md border px-2 text-xs font-medium transition-colors",
                        selected
                          ? "border-primary/70 bg-secondary text-secondary-foreground"
                          : "border-sidebar-border/60 bg-background/45 text-muted-foreground hover:border-sidebar-border hover:text-foreground"
                      )}
                    >
                      <img src={`/mana-symbols/${color}.svg`} alt={color} className="h-4 w-4 rounded-full bg-background ring-1 ring-background" />
                    </button>
                  )
                })}
              </div>
            </div>
          </section>

          <section className={sectionClass}>
            <div className={labelClass}>Mana</div>
            <div className="grid gap-2">
              <div className="grid grid-cols-[7rem_1fr] gap-2">
                <Select value={filters.manaValueOperator} onValueChange={value => onUpdate({ manaValueOperator: value as CardComparisonOperator })}>
                  <SelectTrigger className={controlClass}><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="any">Any value</SelectItem>
                    <SelectItem value="<">Less than</SelectItem>
                    <SelectItem value="<=">At most</SelectItem>
                    <SelectItem value="=">Exactly</SelectItem>
                    <SelectItem value=">">Greater than</SelectItem>
                    <SelectItem value=">=">At least</SelectItem>
                  </SelectContent>
                </Select>
                <Input
                  value={filters.manaValue}
                  onChange={event => onUpdate({ manaValue: event.target.value.replace(/[^\d]/g, "") })}
                  inputMode="numeric"
                  placeholder="Mana value"
                  className={controlClass}
                />
              </div>
              <Input value={filters.manaCost} onChange={event => onUpdate({ manaCost: event.target.value })} placeholder="Mana cost, e.g. {1}{W}" className={controlClass} />
            </div>
          </section>

          <section className={sectionClass}>
            <div className={labelClass}>Type line</div>
            <div className="flex flex-wrap gap-1.5">
              {CARD_TYPE_FILTERS.map(type => {
                const state = filters.typeStates[type] ?? "off"
                return (
                  <button
                    key={type}
                    type="button"
                    onClick={() => cycleType(type)}
                    className={cn(
                      "h-8 rounded-md border px-2.5 text-xs font-medium transition-colors",
                      state === "include" && "border-primary/70 bg-secondary text-secondary-foreground",
                      state === "exclude" && "border-destructive/60 bg-destructive/10 text-destructive",
                      state === "off" && "border-sidebar-border/60 bg-background/45 text-muted-foreground hover:border-sidebar-border hover:text-foreground"
                    )}
                  >
                    <span className="mr-1 text-[11px]">{state === "include" ? "+" : state === "exclude" ? "-" : ""}</span>
                    {titleCaseCardFilter(type)}
                  </button>
                )
              })}
            </div>
          </section>

          <section className={sectionClass}>
            <div className={labelClass}>Rarity</div>
            <div className="grid grid-cols-[7rem_1fr] gap-2">
              <Select value={filters.rarityOperator} onValueChange={value => onUpdate({ rarityOperator: value as CardComparisonOperator })}>
                <SelectTrigger className={controlClass}><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="<">Less than</SelectItem>
                  <SelectItem value="<=">At most</SelectItem>
                  <SelectItem value="=">Exactly</SelectItem>
                  <SelectItem value=">">Greater than</SelectItem>
                  <SelectItem value=">=">At least</SelectItem>
                </SelectContent>
              </Select>
              <Select value={filters.rarity} onValueChange={value => onUpdate({ rarity: value as CardRarityFilter })}>
                <SelectTrigger className={controlClass}><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="any">Any rarity</SelectItem>
                  {CARD_RARITIES.map(rarity => <SelectItem key={rarity} value={rarity}>{titleCaseCardFilter(rarity)}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
          </section>

          <section className={sectionClass}>
            <div className={labelClass}>Format</div>
            <div className="grid grid-cols-2 gap-2">
              <Select value={filters.format} onValueChange={value => onUpdate({ format: value as CardFormatFilter })}>
                <SelectTrigger className={controlClass}><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="any">Any format</SelectItem>
                  {CARD_FORMATS.map(format => <SelectItem key={format} value={format}>{titleCaseCardFilter(format)}</SelectItem>)}
                </SelectContent>
              </Select>
              <Select value={filters.legality} onValueChange={value => onUpdate({ legality: value as CardLegalityFilter })} disabled={filters.format === "any"}>
                <SelectTrigger className={controlClass}><SelectValue /></SelectTrigger>
                <SelectContent>
                  {CARD_LEGALITIES.map(legality => <SelectItem key={legality} value={legality}>{titleCaseCardFilter(legality)}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
          </section>

          <section className={sectionClass}>
            <div className={labelClass}>Catalog</div>
            <div className="grid gap-2 sm:grid-cols-3">
              <Input value={filters.setCode} onChange={event => onUpdate({ setCode: event.target.value })} placeholder="Set code" className={controlClass} />
              <Input value={filters.collectorNumber} onChange={event => onUpdate({ collectorNumber: event.target.value })} placeholder="Collector no." className={controlClass} />
              <Input value={filters.catalogId} onChange={event => onUpdate({ catalogId: event.target.value.replace(/[^\d]/g, "") })} inputMode="numeric" placeholder="MTGO id" className={controlClass} />
              <Input value={filters.artId} onChange={event => onUpdate({ artId: event.target.value.replace(/[^\d]/g, "") })} inputMode="numeric" placeholder="Art id" className={controlClass} />
              <Input value={filters.frameStyle} onChange={event => onUpdate({ frameStyle: event.target.value })} placeholder="Frame" className={controlClass} />
              <Input value={filters.promoLabel} onChange={event => onUpdate({ promoLabel: event.target.value })} placeholder="Promo label" className={controlClass} />
              <Input value={filters.artist} onChange={event => onUpdate({ artist: event.target.value })} placeholder="Artist" className={controlClass} />
              <Input value={filters.flavor} onChange={event => onUpdate({ flavor: event.target.value })} placeholder="Flavor text" className={cn(controlClass, "sm:col-span-2")} />
            </div>
          </section>

          <section className={sectionClass}>
            <div className={labelClass}>Flags</div>
            <div className="grid gap-2 sm:grid-cols-2">
              <Select value={filters.tokenMode} onValueChange={value => onUpdate({ tokenMode: value as CardTokenFilterMode })}>
                <SelectTrigger className={controlClass}><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="default">Non-token cards</SelectItem>
                  <SelectItem value="only">Tokens only</SelectItem>
                </SelectContent>
              </Select>
              {([
                ["Promo", "promoMode"],
                ["Multiface", "multifaceMode"],
                ["Split", "splitMode"],
              ] as const).map(([label, key]) => (
                <Select key={key} value={filters[key]} onValueChange={value => updateBoolean(key, value as CardBooleanMode)}>
                  <SelectTrigger className={controlClass}><SelectValue placeholder={label} /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="any">{label}: Any</SelectItem>
                    <SelectItem value="only">{label}: Only</SelectItem>
                    <SelectItem value="exclude">{label}: Exclude</SelectItem>
                  </SelectContent>
                </Select>
              ))}
            </div>
          </section>
        </div>
      </div>

      <div className="flex items-center justify-between gap-2 border-t border-sidebar-border/60 p-2">
        <Button variant="ghost" size="sm" onClick={onClear} disabled={activeFilterCount === 0}>Clear filters</Button>
        <Button variant="outline" size="sm" onClick={onClose} className="border-sidebar-border/70 bg-background/70">Close</Button>
      </div>
    </div>
  )
}
/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/
