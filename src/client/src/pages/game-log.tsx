/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useMemo, useRef } from "react"
import { useParams, useNavigate, useSearchParams } from "react-router-dom"
import {
  GameLogLayout,
  Button,
  compareLogEntries,
  type GameLogEntry,
  type GameLogType,
} from "@videreproject/ui"
import { useMatchDetails } from "@/hooks/use-match-details"
import type { GameLogDTO } from "@/types/api"

export default function GameLog() {
  const { matchId } = useParams<{ matchId: string }>()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const parsedMatchId = matchId ? parseInt(matchId, 10) : null
  const gameIdParam = searchParams.get("gameId")
  const parsedGameId = gameIdParam == null ? null : Number.parseInt(gameIdParam, 10)
  const selectedGameId =
    parsedGameId != null && Number.isFinite(parsedGameId) ? parsedGameId : null
  const eventSummaryId = searchParams.get("eventSummary")
  const parentPath = eventSummaryId && /^\d+$/.test(eventSummaryId)
    ? `/history/events/${eventSummaryId}?matchId=${matchId}&gameId=${selectedGameId ?? ""}`
    : `/history/${matchId}${selectedGameId != null ? `?gameId=${selectedGameId}` : ""}`

  const { data: matchData, liveLogs, connected, loading, error, refresh } = useMatchDetails(parsedMatchId)
  const rowIds = useRef(new Map<string, number>())
  const { entries, liveEventCount } = useMemo(() => {
    const occurrences = new Map<string, number>()
    const visible = (log: GameLogDTO) => log.gameLogType !== "Interaction" &&
      (selectedGameId == null || log.gameId === selectedGameId)
    const entries: GameLogEntry[] = (matchData?.games ?? []).flatMap(game => game.logs ?? [])
      .filter(visible).map(log => {
        // A reconnect can insert older events without changing existing row identities.
        const key = JSON.stringify([log.gameId, log.gameLogType, log.timestamp, log.data])
        const occurrence = occurrences.get(key) ?? 0
        occurrences.set(key, occurrence + 1)
        const rowKey = `${key}:${occurrence}`
        let seq = rowIds.current.get(rowKey)
        if (seq === undefined) {
          seq = rowIds.current.size
          rowIds.current.set(rowKey, seq)
        }
        return {
          id: log.id ?? 0, gameId: log.gameId ?? 0, timestamp: log.timestamp ?? "",
          gameLogType: log.gameLogType as GameLogType, data: log.data ?? "", nonce: log.nonce ?? 0,
          seq, ts: new Date(log.timestamp ?? 0), deltaMs: null,
        }
      }).sort(compareLogEntries).slice(-2000)
    return {
      entries: entries.map((entry, index) => ({
        ...entry,
        deltaMs: index ? entry.ts.getTime() - entries[index - 1].ts.getTime() : null,
      })),
      liveEventCount: liveLogs?.filter(visible).length ?? 0,
    }
  }, [matchData, liveLogs, selectedGameId])
  const selectedGame = matchData?.games?.find(game => game.id === selectedGameId)

  const logTitle =
    selectedGameId == null
      ? "Game Log"
      : `Game ${selectedGame?.gameNumber ?? selectedGameId} Log`

  return <>
    {error && <div role="alert" className="mx-4 flex shrink-0 items-center justify-between gap-3 rounded-md border border-sidebar-border/60 bg-card px-3 py-2 text-sm">
      <span>Could not load the latest game log.</span>
      <Button variant="outline" size="sm" onClick={refresh}>Try again</Button>
    </div>}
    <GameLogLayout
      className="mx-0 max-w-none"
      entries={entries}
      title={logTitle}
      matchId={parsedMatchId}
      connected={connected}
      liveEventCount={liveEventCount}
      loading={loading && !matchData}
      onBack={() => navigate(parentPath)}
    />
  </>
}
