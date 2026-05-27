import React from "react"
import type { GameLogType, GameStateData, ZoneTransferData, CardChangeData, PlayerChangeData } from "@/types/api"
import type { GameAction } from "@/types/game-types"
import { parseCardName, isCardAction } from "@/types/game-types"
import { GameLogText } from "@/utils/parse-game-log"

export const TYPE_CONFIG: Record<GameLogType, { label: string; short: string; color: string; bg: string }> = {
  GameState:    { label: "Game State",    short: "STATE",  color: "text-blue-400",    bg: "bg-blue-500/15" },
  GameAction:   { label: "Game Action",   short: "ACTION", color: "text-amber-400",   bg: "bg-amber-500/15" },
  ZoneChange:   { label: "Zone Change",   short: "ZONE",   color: "text-cyan-400",    bg: "bg-cyan-500/15" },
  CardChange:   { label: "Card Change",   short: "CARD",   color: "text-purple-400",  bg: "bg-purple-500/15" },
  PlayerChange: { label: "Player Change", short: "PLAYER", color: "text-emerald-400", bg: "bg-emerald-500/15" },
  LogMessage:   { label: "Log Message",   short: "LOG",    color: "text-muted-foreground", bg: "bg-muted/30" },
  DamageAssignment: { label: "Damage", short: "DMG", color: "text-red-400", bg: "bg-red-500/15" },
  Reveal:           { label: "Reveal",  short: "RVLR", color: "text-yellow-400", bg: "bg-yellow-500/15" },
}

export const ALL_TYPES: GameLogType[] = Object.keys(TYPE_CONFIG) as GameLogType[]

export const TYPE_ORDER: Record<string, number> = {
  GameState: 0, GameAction: 1, ZoneChange: 2, Reveal: 2, CardChange: 3,
  PlayerChange: 4, LogMessage: 5, DamageAssignment: 6,
}

/** Render a finalized game action from its ToJSON() output */
export function renderAction(action: GameAction): React.ReactNode {
  const name = action.name ?? ""

  // CardAction variants: show card name and targets
  if (isCardAction(action)) {
    const cardName = parseCardName(action.card)
    const targetNames: string[] = []
    if (action.targets) {
      for (const ts of action.targets) {
        if (ts.currentTargets) {
          for (const t of ts.currentTargets) {
            const parsed = parseCardName(t)
            if (parsed) targetNames.push(parsed)
          }
        }
      }
    }

    return (
      <span>
        <span className="text-amber-300 font-medium">{name}</span>
        <span className="text-zinc-500"> — </span>
        <span className="text-white font-medium">{cardName}</span>
        {targetNames.length > 0 && (
          <>
            <span className="text-zinc-500"> targeting </span>
            <span className="text-amber-200">{targetNames.join(", ")}</span>
          </>
        )}
        {action.isManaAbility && (
          <span className="text-zinc-600 text-[10px] ml-1">(mana)</span>
        )}
      </span>
    )
  }

  switch (action.$type) {
    case "SelectFromListAction": {
      const item = action.selectedItem
      return (
        <span>
          <span className="text-amber-300 font-medium">{name}</span>
          {item && (
            <>
              <span className="text-zinc-500"> — </span>
              <span className="text-amber-200">{item.name ?? String(item)}</span>
            </>
          )}
          {action.itemType && (
            <span className="text-zinc-600 text-[10px] ml-1">({action.itemType})</span>
          )}
        </span>
      )
    }
    case "NumericAction":
      return (
        <span>
          <span className="text-amber-300 font-medium">{name}</span>
          <span className="text-zinc-500"> = </span>
          <span className="text-white font-medium">{action.chosenNumber}</span>
          <span className="text-zinc-600 text-[10px] ml-1">
            ({action.minimum}–{action.maximum})
          </span>
        </span>
      )
    case "OrderingAction": {
      const source = parseCardName(action.source)
      const ordered = action.orderedTargets
        ?.map(t => parseCardName(t))
        .filter(Boolean) ?? []
      return (
        <span>
          <span className="text-amber-300 font-medium">{name}</span>
          {source && (
            <>
              <span className="text-zinc-500"> — </span>
              <span className="text-white font-medium">{source}</span>
            </>
          )}
          {ordered.length > 0 && (
            <>
              <span className="text-zinc-500"> → </span>
              <span className="text-amber-200">{ordered.join(", ")}</span>
            </>
          )}
        </span>
      )
    }
    case "SelectPlayerAction":
      return (
        <span>
          <span className="text-amber-300 font-medium">{name}</span>
          {action.selectedPlayer && (
            <>
              <span className="text-zinc-500"> — </span>
              <span className="text-white font-medium">{action.selectedPlayer}</span>
            </>
          )}
        </span>
      )
    case "CombatDamageAssignmentAction": {
      const source = parseCardName(action.source)
      return (
        <span>
          <span className="text-amber-300 font-medium">{name}</span>
          {source && (
            <>
              <span className="text-zinc-500"> — </span>
              <span className="text-white font-medium">{source}</span>
            </>
          )}
          <span className="text-zinc-600 text-[10px] ml-1">
            ({action.minimumTotal}–{action.maximumTotal} dmg)
          </span>
        </span>
      )
    }
    default:
      // PrimitiveAction, UndoAction, ConcedeGameAction, LocalAction, etc.
      return (
        <span>
          <span className="text-amber-300 font-medium">{name}</span>
          {action.type && action.type !== name && (
            <span className="text-zinc-600 text-[10px] ml-1">({action.type})</span>
          )}
        </span>
      )
  }
}

/** Format structured data as plain text for clipboard export */
export function formatDataAsText(type: GameLogType, data: string): string {
  try {
    switch (type) {
      case "GameState": {
        const d: GameStateData = JSON.parse(data)
        const prev = d.previousTurn != null ? `Turn ${d.previousTurn} ${d.previousPhase ?? ""} → ` : ""
        return `${prev}Turn ${d.turn} ${d.phase}`
      }
      case "Reveal":
      case "ZoneChange": {
        const transfers: ZoneTransferData[] = JSON.parse(data)
        return transfers.map(zt => {
          let s = `${zt.cardName} (${zt.type})`
          if (zt.fromZone) s += ` ${zt.fromZone}`
          if (zt.fromZone && zt.toZone) s += ` →`
          if (zt.toZone) s += ` ${zt.toZone}`
          s += ` id:${zt.cardId}`
          if (zt.sourceId != null) s += ` src:${zt.sourceId}`
          return s
        }).join("\n")
      }
      case "CardChange": {
        const changes: CardChangeData[] = JSON.parse(data)
        return changes.map(cc =>
          `${cc.cardName}.${cc.property} : ${cc.oldValue ?? "null"} → ${cc.newValue ?? "null"}`
        ).join("\n")
      }
      case "PlayerChange": {
        const changes: PlayerChangeData[] = JSON.parse(data)
        return changes.map(pc =>
          `${pc.playerName}.${pc.property} : ${pc.oldValue ?? "null"} → ${pc.newValue ?? "null"}`
        ).join("\n")
      }
      case "GameAction": {
        const action: GameAction = JSON.parse(data)
        const name = action.name ?? ""
        if (isCardAction(action)) {
          const cn = parseCardName(action.card)
          return `${name} — ${cn}`
        }
        return action.response != null ? `${name} (${action.response})` : name
      }
      case "DamageAssignment": {
        const assignments: { sourceName?: string; sourceId: number; totalDamage: number; targets: { targetName?: string; targetId: number; amount: number }[] }[] = JSON.parse(data)
        return assignments.map(a => {
          const targets = a.targets.map(t =>
            a.targets.length > 1 ? `${t.targetName ?? `#${t.targetId}`} (${t.amount})` : (t.targetName ?? `#${t.targetId}`)
          ).join(", ")
          return `${a.sourceName ?? `#${a.sourceId}`} deals ${a.totalDamage} to ${targets}`
        }).join("\n")
      }
      default:
        return data
    }
  } catch {
    return data
  }
}

/** Render structured data inline for each event type */
export function renderData(type: GameLogType, data: string): React.ReactNode {
  try {
    switch (type) {
      case "GameState": {
        const d: GameStateData = JSON.parse(data)
        const prev = d.previousTurn != null ? `Turn ${d.previousTurn} ${d.previousPhase ?? ""}` : null
        return (
          <span>
            {prev && <span className="text-zinc-500">{prev.trim()} → </span>}
            <span className="font-semibold">Turn {d.turn}</span>{" "}
            <span className="text-blue-300">{d.phase}</span>
          </span>
        )
      }
      case "Reveal":
      case "ZoneChange": {
        const transfers: ZoneTransferData[] = JSON.parse(data)
        return (
          <span className="space-y-0.5">
            {transfers.map((zt, i) => (
              <span key={i} className="block">
                <span className="text-white font-medium">{zt.cardName}</span>
                <span className="text-zinc-500"> ({zt.type}) </span>
                {zt.fromZone && <span className="text-red-400/80">{zt.fromZone}</span>}
                {zt.fromZone && zt.toZone && <span className="text-zinc-500"> → </span>}
                {zt.toZone && <span className="text-green-400/80">{zt.toZone}</span>}
                <span className="text-zinc-600 text-[10px] ml-1">id:{zt.cardId}</span>
                {zt.sourceId != null && (
                  <span className="text-zinc-600 text-[10px] ml-1">src:{zt.sourceId}</span>
                )}
              </span>
            ))}
          </span>
        )
      }
      case "CardChange": {
        const changes: CardChangeData[] = JSON.parse(data)
        return (
          <span className="space-y-0.5">
            {changes.map((cc, i) => (
              <span key={i} className="block">
                <span className="text-white font-medium">{cc.cardName}</span>
                <span className="text-zinc-500">.</span>
                <span className="text-purple-300">{cc.property}</span>
                <span className="text-zinc-500"> : </span>
                <span className="text-red-400/70">{cc.oldValue ?? "null"}</span>
                <span className="text-zinc-500"> → </span>
                <span className="text-green-400/70">{cc.newValue ?? "null"}</span>
              </span>
            ))}
          </span>
        )
      }
      case "PlayerChange": {
        const changes: PlayerChangeData[] = JSON.parse(data)
        return (
          <span className="space-y-0.5">
            {changes.map((pc, i) => (
              <span key={i} className="block">
                <span className="text-white font-medium">{pc.playerName}</span>
                <span className="text-zinc-500">.</span>
                <span className="text-emerald-300">{pc.property}</span>
                <span className="text-zinc-500"> : </span>
                <span className="text-red-400/70">{pc.oldValue ?? "null"}</span>
                <span className="text-zinc-500"> → </span>
                <span className="text-green-400/70">{pc.newValue ?? "null"}</span>
              </span>
            ))}
          </span>
        )
      }
      case "GameAction": {
        try {
          const action: GameAction = JSON.parse(data)
          return renderAction(action)
        } catch { /* fall through to raw display */ }
        return <span className="text-amber-200/70">{data.length > 200 ? data.slice(0, 200) + "…" : data}</span>
      }
      case "LogMessage":
        return (
          <GameLogText
            text={data}
            className={
              data.includes("wins the game") ? "text-emerald-400 font-bold" :
              data.includes("has conceded") ? "text-rose-400 italic" :
              ""
            }
          />
        )
      case "DamageAssignment": {
        const assignments: { sourceName?: string; sourceId: number; totalDamage: number; targets: { targetName?: string; targetId: number; amount: number }[] }[] = JSON.parse(data)
        return (
          <span className="space-y-0.5">
            {assignments.map((a, i) => (
              <span key={i} className="block">
                <span className="text-white font-medium">{a.sourceName ?? `#${a.sourceId}`}</span>
                <span className="text-zinc-500"> deals </span>
                <span className="text-red-300 font-medium">{a.totalDamage}</span>
                <span className="text-zinc-500"> to </span>
                <span className="text-red-200">
                  {a.targets.map((t, j) => (
                    <span key={j}>
                      {j > 0 && <span className="text-zinc-500">, </span>}
                      {t.targetName ?? `#${t.targetId}`}
                      {a.targets.length > 1 && <span className="text-zinc-500"> ({t.amount})</span>}
                    </span>
                  ))}
                </span>
              </span>
            ))}
          </span>
        )
      }
      default:
        return <span>{data}</span>
    }
  } catch {
    // Failed to parse — show raw
    return <span className="text-zinc-500">{data.length > 300 ? data.slice(0, 300) + "…" : data}</span>
  }
}
