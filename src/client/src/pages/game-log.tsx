/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useState, useEffect, useRef, useCallback, useMemo } from "react"
import { useParams, useNavigate, useSearchParams } from "react-router-dom"
import {
  GameLogLayout,
  compareLogEntries,
  type GameLogDTO,
  type GameLogEntry,
  type GameLogType,
} from "@videreproject/ui"
import { getApiUrl } from "@/utils/api-config"
import { useClientState } from "@/hooks/use-client-state"
import { useNDJSONStream } from "@/hooks/use-ndjson-stream"
import { useMatchDetails } from "@/hooks/use-match-details"

export default function GameLog() {
  const { matchId } = useParams<{ matchId: string }>()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const parsedMatchId = matchId ? parseInt(matchId, 10) : null
  const gameIdParam = searchParams.get("gameId")
  const parsedGameId = gameIdParam == null ? null : Number.parseInt(gameIdParam, 10)
  const selectedGameId =
    parsedGameId != null && Number.isFinite(parsedGameId) ? parsedGameId : null

  const { isReady: clientReady } = useClientState()

  // Historical backfill — fetches persisted logs on mount
  const { data: matchData, loading: historyLoading } = useMatchDetails(parsedMatchId)
  const historyMergedRef = useRef(false)

  const [entries, setEntries] = useState<GameLogEntry[]>([])
  const seqRef = useRef(0)
  const lastTsRef = useRef<Date | null>(null)
  const [connected, setConnected] = useState(false)
  const [eventCount, setEventCount] = useState(0)

  const onMessage = useCallback(
    (dto: GameLogDTO) => {
      if (selectedGameId != null && (dto.gameId ?? 0) !== selectedGameId) return

      const ts = new Date(dto.timestamp ?? Date.now())
      const seq = seqRef.current++
      const deltaMs = lastTsRef.current ? ts.getTime() - lastTsRef.current.getTime() : null
      lastTsRef.current = ts

      const entry: GameLogEntry = {
        id: dto.id ?? 0,
        gameId: dto.gameId ?? 0,
        timestamp: dto.timestamp ?? new Date().toISOString(),
        gameLogType: (dto.gameLogType ?? "LogMessage") as GameLogType,
        data: dto.data ?? "",
        nonce: dto.nonce ?? 0,
        seq,
        ts,
        deltaMs,
      }
      setEntries(prev => {
        // Cap at 2000 entries for performance
        const next = [...prev, entry]
        next.sort(compareLogEntries)
        return next.length > 2000 ? next.slice(-1500) : next
      })
      setEventCount(c => c + 1)
    },
    [selectedGameId],
  )

  const streamUrl = parsedMatchId
    ? getApiUrl(`/api/games/match/${parsedMatchId}/watch`)
    : ""

  useNDJSONStream<GameLogDTO>({
    url: streamUrl,
    onMessage,
    onEnd: () => setConnected(false),
    onError: () => setConnected(false),
    enabled: clientReady && parsedMatchId != null,
    autoReconnect: true,
    reconnectDelay: 2000,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })

  useEffect(() => {
    if (eventCount > 0) setConnected(true)
  }, [eventCount])

  // Reset all state when match or selected game changes
  useEffect(() => {
    historyMergedRef.current = false
    setEntries([])
    seqRef.current = 0
    lastTsRef.current = null
    setEventCount(0)
    setConnected(false)
  }, [parsedMatchId, selectedGameId])

  // Merge historical logs when they arrive — prepend before any live entries
  useEffect(() => {
    if (!matchData?.games || historyMergedRef.current) return
    historyMergedRef.current = true

    setEntries(prevLive => {
      const allLogs: GameLogDTO[] = (matchData.games ?? [])
        .filter(g => selectedGameId == null || (g.id ?? g.gameNumber ?? 0) === selectedGameId)
        .flatMap(g => g.logs ?? [])

      let seq = 0
      let prevTs: Date | null = null
      const historical: GameLogEntry[] = allLogs.map(dto => {
        const ts = new Date(dto.timestamp ?? Date.now())
        const deltaMs = prevTs ? ts.getTime() - prevTs.getTime() : null
        prevTs = ts
        return {
          id: dto.id ?? 0,
          gameId: dto.gameId ?? 0,
          timestamp: dto.timestamp ?? "",
          gameLogType: (dto.gameLogType ?? "LogMessage") as GameLogType,
          data: dto.data ?? "",
          nonce: dto.nonce ?? 0,
          seq: seq++,
          ts,
          deltaMs,
        }
      })

      const resequenced = prevLive.map((e, i) => ({
        ...e,
        seq: seq + i,
        deltaMs: i === 0 && prevTs ? e.ts.getTime() - prevTs.getTime() : e.deltaMs,
      }))

      seqRef.current = seq + resequenced.length
      const last = resequenced.at(-1) ?? historical.at(-1)
      if (last) lastTsRef.current = last.ts

      const combined = [...historical, ...resequenced]
      return combined.length > 2000 ? combined.slice(-1500) : combined
    })
  }, [matchData, selectedGameId])

  const selectedGame = useMemo(() => {
    if (selectedGameId == null) return null
    return (
      (matchData?.games ?? []).find(g => (g.id ?? g.gameNumber ?? 0) === selectedGameId) ?? null
    )
  }, [matchData, selectedGameId])

  const logTitle =
    selectedGameId == null
      ? "Game Log"
      : `Game ${selectedGame?.gameNumber ?? selectedGameId} Log`

  return (
    <GameLogLayout
      entries={entries}
      title={logTitle}
      matchId={parsedMatchId}
      connected={connected}
      liveEventCount={eventCount}
      loading={historyLoading}
      onBack={() => navigate(-1)}
    />
  )
}
