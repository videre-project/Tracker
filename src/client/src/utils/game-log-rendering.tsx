/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import React from "react"
import { ArrowRight } from "lucide-react"

import type {
  CardChangeData,
  GameLogType,
  GameStateData,
  PlayerChangeData,
  ZoneTransferData,
} from "@/types/api"
import type { GameAction } from "@/types/game-types"
import { isCardAction, parseCardName } from "@/types/game-types"
import { GameLogText } from "@/utils/parse-game-log"

export const TYPE_CONFIG: Record<
  GameLogType,
  { label: string; short: string; tone: string }
> = {
  GameState: {
    label: "Game State",
    short: "STATE",
    tone: "border-sky-400/20 bg-sky-500/[0.08] text-sky-700 dark:text-sky-200/75",
  },
  GameAction: {
    label: "Game Action",
    short: "ACTION",
    tone: "border-amber-400/20 bg-amber-500/[0.08] text-amber-700 dark:text-amber-200/75",
  },
  ZoneChange: {
    label: "Zone Change",
    short: "ZONE",
    tone: "border-teal-400/20 bg-teal-500/[0.08] text-teal-700 dark:text-teal-200/75",
  },
  CardChange: {
    label: "Card Change",
    short: "CARD",
    tone: "border-violet-400/20 bg-violet-500/[0.08] text-violet-700 dark:text-violet-200/75",
  },
  PlayerChange: {
    label: "Player Change",
    short: "PLAYER",
    tone: "border-emerald-400/20 bg-emerald-500/[0.08] text-emerald-700 dark:text-emerald-200/75",
  },
  LogMessage: {
    label: "Log Message",
    short: "LOG",
    tone: "border-sidebar-border/60 bg-muted/30 text-muted-foreground",
  },
  DamageAssignment: {
    label: "Damage",
    short: "DAMAGE",
    tone: "border-rose-400/20 bg-rose-500/[0.08] text-rose-700 dark:text-rose-200/75",
  },
  Reveal: {
    label: "Reveal",
    short: "REVEAL",
    tone: "border-orange-400/20 bg-orange-500/[0.08] text-orange-700 dark:text-orange-200/75",
  },
}

export const ALL_TYPES: GameLogType[] = Object.keys(TYPE_CONFIG) as GameLogType[]

export const TYPE_ORDER: Record<string, number> = {
  GameState: 0,
  GameAction: 1,
  ZoneChange: 2,
  Reveal: 2,
  CardChange: 3,
  PlayerChange: 4,
  LogMessage: 5,
  DamageAssignment: 6,
}

type DamageAssignment = {
  sourceName?: string
  sourceId: number
  totalDamage: number
  targets: Array<{
    targetName?: string
    targetId: number
    amount: number
  }>
}

type DataRow = {
  subject?: React.ReactNode
  event?: React.ReactNode
  details?: React.ReactNode
  fullWidth?: boolean
}

const DATA_GRID_CLASS =
  "grid grid-cols-[minmax(8rem,0.8fr)_minmax(8rem,0.8fr)_minmax(14rem,2fr)] gap-x-4 gap-y-1"

function DataRows({ rows }: { rows: DataRow[] }) {
  return (
    <div className={DATA_GRID_CLASS}>
      {rows.map((row, index) => row.fullWidth ? (
        <div key={index} className="col-span-3 min-w-0 break-words">
          {row.details}
        </div>
      ) : (
        <React.Fragment key={index}>
          <div className="min-w-0 break-words font-medium text-foreground">
            {row.subject ?? <span className="text-muted-foreground/30">-</span>}
          </div>
          <div className="min-w-0 break-words">
            {row.event ?? <span className="text-muted-foreground/30">-</span>}
          </div>
          <div className="min-w-0 break-words text-muted-foreground">
            {row.details ?? <span className="text-muted-foreground/30">-</span>}
          </div>
        </React.Fragment>
      ))}
    </div>
  )
}

function ValueChange({ before, after }: { before?: string | null; after?: string | null }) {
  return (
    <div className="grid min-w-0 grid-cols-[minmax(0,1fr)_1rem_minmax(0,1fr)] items-start gap-2">
      <span className="break-words text-red-400/70">{before ?? "null"}</span>
      <ArrowRight className="mt-0.5 h-3.5 w-3.5 text-muted-foreground/45" />
      <span className="break-words text-green-400/70">{after ?? "null"}</span>
    </div>
  )
}

function getActionRows(action: GameAction): DataRow[] {
  const name = action.name ?? action.type ?? "Action"

  if (isCardAction(action)) {
    const targets = action.targets
      ?.flatMap(target => target.currentTargets ?? [])
      .map(target => parseCardName(target))
      .filter((target): target is string => Boolean(target)) ?? []

    return [{
      subject: parseCardName(action.card),
      event: <span className="font-medium text-amber-300">{name}</span>,
      details: (
        <div className="flex min-w-0 flex-wrap items-center gap-x-2 gap-y-0.5">
          {targets.length > 0 ? (
            <>
              <span className="text-muted-foreground/60">Targets</span>
              <span className="text-amber-200">{targets.join(", ")}</span>
            </>
          ) : null}
          {action.isManaAbility ? (
            <span className="text-[10px] text-muted-foreground/60">Mana ability</span>
          ) : null}
        </div>
      ),
    }]
  }

  switch (action.$type) {
    case "SelectFromListAction":
      return [{
        subject: action.selectedItem?.name ?? action.selectedItem?.value,
        event: <span className="font-medium text-amber-300">{name}</span>,
        details: action.itemType,
      }]
    case "NumericAction":
      return [{
        event: <span className="font-medium text-amber-300">{name}</span>,
        details: (
          <span>
            <span className="font-medium text-foreground">{action.chosenNumber}</span>
            <span className="ml-2 text-[10px] text-muted-foreground/60">
              Range {action.minimum}-{action.maximum}
            </span>
          </span>
        ),
      }]
    case "OrderingAction":
      return [{
        subject: parseCardName(action.source),
        event: <span className="font-medium text-amber-300">{name}</span>,
        details: action.orderedTargets
          ?.map(target => parseCardName(target))
          .filter(Boolean)
          .join(", ") || undefined,
      }]
    case "SelectPlayerAction":
      return [{
        subject: action.selectedPlayer,
        event: <span className="font-medium text-amber-300">{name}</span>,
      }]
    case "CombatDamageAssignmentAction":
      return [{
        subject: parseCardName(action.source),
        event: <span className="font-medium text-amber-300">{name}</span>,
        details: `Damage ${action.minimumTotal ?? 0}-${action.maximumTotal ?? 0}`,
      }]
    default:
      return [{
        event: <span className="font-medium text-amber-300">{name}</span>,
        details: action.response != null
          ? String(action.response)
          : action.type !== name
            ? action.type
            : undefined,
      }]
  }
}

export function GameLogDataHeader() {
  return (
    <div className={DATA_GRID_CLASS}>
      <span>Subject</span>
      <span>Event</span>
      <span>Details</span>
    </div>
  )
}

/** Format structured data as plain text for clipboard export. */
export function formatDataAsText(type: GameLogType, data: string): string {
  try {
    switch (type) {
      case "GameState": {
        const state: GameStateData = JSON.parse(data)
        const previous = state.previousTurn != null
          ? `Turn ${state.previousTurn} ${state.previousPhase ?? ""} -> `
          : ""
        return `${previous}Turn ${state.turn} ${state.phase}`
      }
      case "Reveal":
      case "ZoneChange": {
        const transfers: ZoneTransferData[] = JSON.parse(data)
        return transfers.map(transfer => {
          const source = transfer.fromZone ?? "null"
          const destination = transfer.toZone ?? "null"
          const sourceId = transfer.sourceId != null ? ` src:${transfer.sourceId}` : ""
          return `${transfer.cardName}  ${transfer.type}  ${source} -> ${destination}  id:${transfer.cardId}${sourceId}`
        }).join("\n")
      }
      case "CardChange": {
        const changes: CardChangeData[] = JSON.parse(data)
        return changes.map(change =>
          `${change.cardName}.${change.property} : ${change.oldValue ?? "null"} -> ${change.newValue ?? "null"}`
        ).join("\n")
      }
      case "PlayerChange": {
        const changes: PlayerChangeData[] = JSON.parse(data)
        return changes.map(change =>
          `${change.playerName}.${change.property} : ${change.oldValue ?? "null"} -> ${change.newValue ?? "null"}`
        ).join("\n")
      }
      case "GameAction": {
        const action: GameAction = JSON.parse(data)
        const name = action.name ?? ""
        if (isCardAction(action)) {
          return `${name} - ${parseCardName(action.card)}`
        }
        return action.response != null ? `${name} (${action.response})` : name
      }
      case "DamageAssignment": {
        const assignments: DamageAssignment[] = JSON.parse(data)
        return assignments.map(assignment => {
          const targets = assignment.targets.map(target =>
            assignment.targets.length > 1
              ? `${target.targetName ?? `#${target.targetId}`} (${target.amount})`
              : target.targetName ?? `#${target.targetId}`
          ).join(", ")
          return `${assignment.sourceName ?? `#${assignment.sourceId}`} deals ${assignment.totalDamage} to ${targets}`
        }).join("\n")
      }
      default:
        return data
    }
  } catch {
    return data
  }
}

/** Render structured game data into stable subject, event, and detail columns. */
export function renderData(type: GameLogType, data: string): React.ReactNode {
  try {
    switch (type) {
      case "GameState": {
        const state: GameStateData = JSON.parse(data)
        return <DataRows rows={[{
          subject: `Turn ${state.turn}`,
          event: <span className="text-blue-300">{state.phase}</span>,
          details: state.previousTurn != null
            ? `From Turn ${state.previousTurn} ${state.previousPhase ?? ""}`
            : undefined,
        }]} />
      }
      case "Reveal":
      case "ZoneChange": {
        const transfers: ZoneTransferData[] = JSON.parse(data)
        return <DataRows rows={transfers.map(transfer => ({
          subject: transfer.cardName,
          event: <span className="text-cyan-300">{transfer.type}</span>,
          details: (
            <div className="space-y-0.5">
              <ValueChange before={transfer.fromZone} after={transfer.toZone} />
              <div className="flex gap-2 text-[10px] text-muted-foreground/50">
                <span>ID {transfer.cardId}</span>
                {transfer.sourceId != null ? <span>Source {transfer.sourceId}</span> : null}
              </div>
            </div>
          ),
        }))} />
      }
      case "CardChange": {
        const changes: CardChangeData[] = JSON.parse(data)
        return <DataRows rows={changes.map(change => ({
          subject: change.cardName,
          event: <span className="text-purple-300">{change.property}</span>,
          details: <ValueChange before={change.oldValue} after={change.newValue} />,
        }))} />
      }
      case "PlayerChange": {
        const changes: PlayerChangeData[] = JSON.parse(data)
        return <DataRows rows={changes.map(change => ({
          subject: change.playerName,
          event: <span className="text-emerald-300">{change.property}</span>,
          details: <ValueChange before={change.oldValue} after={change.newValue} />,
        }))} />
      }
      case "GameAction": {
        const action: GameAction = JSON.parse(data)
        return <DataRows rows={getActionRows(action)} />
      }
      case "LogMessage":
        return <DataRows rows={[{
          fullWidth: true,
          details: (
            <GameLogText
              text={data}
              className={
                data.includes("wins the game")
                  ? "font-bold text-emerald-400"
                  : data.includes("has conceded")
                    ? "italic text-rose-400"
                    : ""
              }
            />
          ),
        }]} />
      case "DamageAssignment": {
        const assignments: DamageAssignment[] = JSON.parse(data)
        return <DataRows rows={assignments.map(assignment => ({
          subject: assignment.sourceName ?? `#${assignment.sourceId}`,
          event: <span className="font-medium text-red-300">{assignment.totalDamage} damage</span>,
          details: assignment.targets.map(target => (
            `${target.targetName ?? `#${target.targetId}`}${
              assignment.targets.length > 1 ? ` (${target.amount})` : ""
            }`
          )).join(", "),
        }))} />
      }
      default:
        return <DataRows rows={[{ fullWidth: true, details: data }]} />
    }
  } catch {
    const truncated = data.length > 300 ? `${data.slice(0, 300)}...` : data
    return <DataRows rows={[{
      fullWidth: true,
      details: <span className="text-muted-foreground">{truncated}</span>,
    }]} />
  }
}
