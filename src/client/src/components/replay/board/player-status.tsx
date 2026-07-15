/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import React, { useState, useEffect } from "react"
import type { PlayerState } from "@/types/replay-types"
import { getApiUrl } from "@/utils/api-config"
import { getManaSymbolSvgPath } from "@/utils/mana-symbols"
import { getPlayerCounterUrl } from "@/utils/videre-cdn"

function formatClock(totalSeconds: number): string {
  const mins = Math.floor(totalSeconds / 60)
  const secs = Math.floor(totalSeconds % 60)
  return `${mins.toString().padStart(2, "0")}:${secs.toString().padStart(2, "0")}`
}

type ManaPoolRow = {
  symbol: string
  amount: number
}

function colorIdToSymbol(color: number): string {
  switch (color) {
    case 1: return "{W}"
    case 2: return "{U}"
    case 4: return "{B}"
    case 8: return "{R}"
    case 16: return "{G}"
    case 32: return "{C}"
    default: {
      const symbol = [
        (color & 1) !== 0 ? "{W}" : "",
        (color & 2) !== 0 ? "{U}" : "",
        (color & 4) !== 0 ? "{B}" : "",
        (color & 8) !== 0 ? "{R}" : "",
        (color & 16) !== 0 ? "{G}" : "",
        (color & 32) !== 0 ? "{C}" : "",
      ].join("")
      return symbol || `{${color}}`
    }
  }
}

function parseManaPoolRows(manaPool: string | null): ManaPoolRow[] {
  if (!manaPool) return []

  try {
    const parsed = JSON.parse(manaPool)
    if (!Array.isArray(parsed)) return []

    const rows: ManaPoolRow[] = []
    for (const entry of parsed as unknown[]) {
      if (!entry || typeof entry !== "object") continue
      const mana = entry as Record<string, unknown>
      const amountRaw = mana.amount ?? mana.Amount
      const amount = typeof amountRaw === "number"
        ? amountRaw
        : Number.parseInt(String(amountRaw ?? "0"), 10)
      if (!Number.isFinite(amount) || amount <= 0) continue

      const symbolRaw = mana.symbol ?? mana.Symbol
      const symbol = typeof symbolRaw === "string" && symbolRaw.length > 0
        ? symbolRaw
        : colorIdToSymbol(Number(mana.color ?? mana.Color ?? 0))

      rows.push({ symbol, amount })
    }

    return rows
  } catch {
    return []
  }
}

export function ManaPoolBox({ manaPool }: { manaPool: string | null }) {
  const rows = parseManaPoolRows(manaPool)
  if (rows.length === 0) return null
  const amountColumnWidth = `${Math.max(...rows.map(r => `${r.amount}`.length))}ch`

  const renderManaSymbols = (symbolText: string) => {
    const tokens = symbolText.match(/\{([^}]+)\}/g)?.map(t => t.slice(1, -1)) ?? [symbolText]
    return (
      <span className="inline-flex items-center gap-[2px]">
        {tokens.map((token, i) => {
          const path = getManaSymbolSvgPath(token.toUpperCase())
          if (path) {
            return (
              <img
                key={`${token}-${i}`}
                src={path}
                alt={token}
                className="h-3 w-3"
              />
            )
          }
          return <span key={`${token}-${i}`}>{`{${token}}`}</span>
        })}
      </span>
    )
  }

  return (
    <div className="inline-block bg-black/70 rounded-sm px-1.5 py-0.5 border border-sidebar-border/40">
      <div className="text-[9px] leading-tight space-y-0.5">
        {rows.map((row, idx) => (
          <div
            key={`${row.symbol}-${idx}`}
            className="grid items-center gap-x-1"
            style={{ gridTemplateColumns: "auto auto" }}
          >
            <span className="inline-flex items-center h-4 text-foreground/90 whitespace-nowrap">
              {renderManaSymbols(row.symbol)}
            </span>
            <span className="inline-flex items-center justify-end h-4 font-mono tabular-nums leading-none whitespace-nowrap">
              <span className="text-[9px] text-foreground/60 pr-0.5">x</span>
              <span
                className="text-[11px] font-semibold text-right text-foreground/90"
                style={{ width: amountColumnWidth }}
              >
                {row.amount}
              </span>
            </span>
          </div>
        ))}
      </div>
    </div>
  )
}

// -- Player avatar badge --

export function PlayerAvatar({
  player,
  clockSeconds,
}: {
  player: PlayerState
  clockSeconds?: number
}) {
  const [artUrl, setArtUrl] = useState<string | null>(null)

  useEffect(() => {
    if (player.avatarId <= 0) return

    let cancelled = false
    ;(async () => {
      try {
        const res = await fetch(
          getApiUrl(`/api/collection/cards/${player.avatarId}/art`)
        )
        if (!res.ok || cancelled) return
        const blob = await res.blob()
        if (cancelled) return
        setArtUrl(URL.createObjectURL(blob))
      } catch { /* ignore */ }
    })()

    return () => { cancelled = true }
  }, [player.avatarId])

  const showClock = clockSeconds != null && clockSeconds > 0
  const counters = Object.entries(player.counters)
    .filter(([, count]) => count > 0)

  return (
    <div className={[
      "flex items-center gap-2 bg-black/70 rounded pl-0.5 pr-2.5 py-0.5 border",
      player.hasPriority ? "border-white/80" : "border-transparent",
    ].filter(Boolean).join(" ")}>
      <div className="relative w-8 h-8 rounded overflow-hidden shrink-0 bg-muted/40">
        {artUrl ? (
          <img
            src={artUrl}
            alt={player.name}
            className="w-full h-full object-cover"
          />
        ) : (
          <div className="w-full h-full flex items-center justify-center text-[9px] text-muted-foreground font-bold">
            {player.name.charAt(0).toUpperCase()}
          </div>
        )}
        <span
          className="absolute inset-0 flex items-center justify-center mt-1 text-lg font-bold text-white leading-none font-mono tabular-nums"
          style={{ WebkitTextStroke: "2px black", paintOrder: "stroke fill" }}
        >
          {player.life}
        </span>
      </div>
      <div className="flex flex-col">
        <span className="text-xs font-semibold text-foreground/90 whitespace-nowrap">
          {player.name}
        </span>
        {(showClock || counters.length > 0) && (
          <div className="flex items-center gap-1.5">
            {showClock && (
              <span className="text-[10px] font-mono font-bold text-foreground/60 tabular-nums">
                {formatClock(clockSeconds)}
              </span>
            )}
            {counters.map(([name, count]) => (
              <span
                key={name}
                className="inline-flex items-center gap-0.5 text-[10px] font-mono font-bold text-foreground/80 tabular-nums"
                title={`${name}: ${count}`}
              >
                <img
                  src={getPlayerCounterUrl(name) ?? undefined}
                  alt=""
                  className="h-3 w-3 object-contain"
                />
                {count}
              </span>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

// -- Phase ladder --

const PHASE_LADDER = [
  { key: "Untap", label: "Untap" },
  { key: "Upkeep", label: "Upkeep" },
  { key: "Draw", label: "Draw" },
  { key: "PreCombatMain", label: "Main 1" },
  { key: "BeginCombat", label: "Combat" },
  { key: "DeclareAttackers", label: "Attack" },
  { key: "DeclareBlockers", label: "Block" },
  { key: "CombatDamage", label: "Damage" },
  { key: "EndOfCombat", label: "End Combat" },
  { key: "PostCombatMain", label: "Main 2" },
  { key: "EndOfTurn", label: "End" },
  { key: "Cleanup", label: "Cleanup" },
]

export function PhaseLadder({
  currentPhase,
  turn,
  activePlayerName,
}: {
  currentPhase: string
  turn: number
  activePlayerName?: string
}) {
  return (
    <div className="flex items-center py-1 bg-muted/20 border-t border-sidebar-border/60 shrink-0">
      {/* Turn indicator — same width as the prompt box so phases align with hand */}
      <div className="w-[200px] shrink-0 px-3 border-r border-transparent flex items-center">
        {turn > 0 && (
          <span className="text-[11px] font-medium text-foreground/70 whitespace-nowrap leading-none">
            Turn {turn}{activePlayerName ? `: ${activePlayerName}` : ""}
          </span>
        )}
      </div>

      {/* Phase steps — left-aligned with hand content */}
      <div className="flex-1 flex items-center gap-0 min-w-0 pl-2.5 pr-4">
        {PHASE_LADDER.map((phase, i) => {
          const isCurrent = phase.key === currentPhase
          const isPast = PHASE_LADDER.findIndex(p => p.key === currentPhase) > i
          return (
            <React.Fragment key={phase.key}>
              {i > 0 && (
                <div className={`w-2 h-px shrink-0 ${
                  isPast ? "bg-sidebar-accent/40" : "bg-sidebar-border/40"
                }`} />
              )}
              <span
                className={[
                  "text-[11px] px-1.5 py-1 rounded-md whitespace-nowrap transition-colors leading-none",
                  isCurrent
                    ? "bg-sidebar-accent text-sidebar-accent-foreground font-medium"
                    : isPast
                      ? "text-muted-foreground/60"
                      : "text-muted-foreground/35",
                ].join(" ")}
              >
                {phase.label}
              </span>
            </React.Fragment>
          )
        })}
      </div>
    </div>
  )
}
