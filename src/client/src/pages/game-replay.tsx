import React, { useState, useEffect, useCallback, useRef } from "react"
import { useParams, useNavigate } from "react-router-dom"
import { getApiUrl } from "@/utils/api-config"
import { useClientState } from "@/hooks/use-client-state"
import { useNDJSONStream } from "@/hooks/use-ndjson-stream"
import { ArrowLeft } from "lucide-react"
import { Button } from "@/components/ui/button"
import type { GameLogDTO } from "@/types/api"
import type { ReplayData, BoardState, BoardTransition } from "@/types/replay-types"
import { computeBoardTransition, EMPTY_TRANSITION } from "@/types/replay-types"
import { ReplayStateEngine } from "@/components/replay/ReplayStateEngine"
import { BoardView } from "@/components/replay/BoardView"
import { ReplayTimeline } from "@/components/replay/ReplayTimeline"

export default function GameReplay() {
  const { matchId, gameId: gameIdParam } = useParams<{
    matchId: string
    gameId: string
  }>()
  const navigate = useNavigate()
  const { isReady: clientReady } = useClientState()

  const parsedMatchId = matchId ? parseInt(matchId, 10) : null
  const gameId = gameIdParam ? parseInt(gameIdParam, 10) : null

  const [replayData, setReplayData] = useState<ReplayData | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [refreshKey, setRefreshKey] = useState(0)

  const [engine] = useState(() => ({
    current: null as ReplayStateEngine | null,
  }))
  const [board, setBoard] = useState<BoardState | null>(null)
  const [currentIndex, setCurrentIndex] = useState(-1)
  const [transition, setTransition] = useState<BoardTransition>(EMPTY_TRANSITION)
  const prevBoardRef = useRef<BoardState | null>(null)
  const isFirstLoadRef = useRef(true)
  const snapshotCountRef = useRef(0)
  const currentIndexRef = useRef(-1)
  const wasAtLatestRef = useRef(true)
  const refreshScheduledRef = useRef(false)

  const queueRefresh = useCallback(() => {
    if (refreshScheduledRef.current) return
    refreshScheduledRef.current = true
    setTimeout(() => {
      refreshScheduledRef.current = false
      setRefreshKey(key => key + 1)
    }, 200)
  }, [])

  // Trigger replay refresh on new live events for this game.
  const streamUrl = parsedMatchId ? getApiUrl(`/api/games/match/${parsedMatchId}/watch`) : ""
  useNDJSONStream<GameLogDTO>({
    url: streamUrl,
    onMessage: (dto) => {
      if ((dto.gameId ?? 0) === gameId) {
        queueRefresh()
      }
    },
    enabled: clientReady && parsedMatchId != null && gameId != null,
    autoReconnect: true,
    reconnectDelay: 2000,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })

  useEffect(() => {
    if (!gameId) return
    const isInitialFetch = isFirstLoadRef.current
    if (isInitialFetch) {
      setLoading(true)
    }
    setError(null)

    const controller = new AbortController()
    fetch(getApiUrl(`/api/games/game/${gameId}/replay`), {
      signal: controller.signal,
    })
      .then((res) => {
        if (!res.ok) throw new Error(`HTTP ${res.status}`)
        return res.json()
      })
      .then((data: ReplayData) => {
        const isRefresh = !isFirstLoadRef.current
        isFirstLoadRef.current = false

        setReplayData(data)
        const eng = new ReplayStateEngine(data)
        engine.current = eng

        let targetIndex = 0
        if (isRefresh && wasAtLatestRef.current) {
          targetIndex = data.snapshots.length - 1
        } else if (isRefresh) {
          targetIndex = Math.min(currentIndexRef.current, data.snapshots.length - 1)
        }

        const newBoard = eng.stepTo(targetIndex)
        snapshotCountRef.current = data.snapshots.length
        wasAtLatestRef.current = eng.currentIndex >= data.snapshots.length - 1
        prevBoardRef.current = newBoard
        setBoard(newBoard)
        setTransition(computeBoardTransition(null, newBoard))
        currentIndexRef.current = eng.currentIndex
        setCurrentIndex(eng.currentIndex)
        if (isInitialFetch) {
          setLoading(false)
        }
      })
      .catch((err) => {
        if (err.name === "AbortError") return
        if (isInitialFetch) {
          setError(err.message ?? "Failed to load replay data")
          setLoading(false)
        } else {
          console.error("Replay refresh failed:", err)
        }
      })

    return () => controller.abort()
  }, [gameId, refreshKey])

  const handleStepTo = useCallback((index: number) => {
    if (!engine.current) return
    const newBoard = engine.current.jumpTo(index)
    const diff = computeBoardTransition(prevBoardRef.current, newBoard)
    prevBoardRef.current = newBoard
    setBoard(newBoard)
    setTransition(diff)
    currentIndexRef.current = engine.current.currentIndex
    setCurrentIndex(engine.current.currentIndex)
    wasAtLatestRef.current = engine.current.currentIndex >= snapshotCountRef.current - 1
  }, [])

  // Keyboard shortcuts
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (!engine.current) return
      if (e.key === "ArrowRight" || e.key === "l") {
        e.preventDefault()
        handleStepTo(currentIndex + 1)
      } else if (e.key === "ArrowLeft" || e.key === "h") {
        e.preventDefault()
        handleStepTo(currentIndex - 1)
      } else if (e.key === "Home") {
        e.preventDefault()
        handleStepTo(-1)
      } else if (e.key === "End") {
        e.preventDefault()
        handleStepTo(replayData ? replayData.snapshots.length - 1 : 0)
      }
    }
    window.addEventListener("keydown", onKey)
    return () => window.removeEventListener("keydown", onKey)
  }, [currentIndex, replayData, handleStepTo])

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full text-muted-foreground text-sm">
        Loading replay...
      </div>
    )
  }

  if (error) {
    return (
      <div className="flex flex-col items-center justify-center h-full gap-4">
        <span className="text-destructive text-sm">
          Failed to load replay: {error}
        </span>
        <Button variant="ghost" size="sm" onClick={() => navigate(-1)}>
          <ArrowLeft className="mr-2 h-4 w-4" /> Back
        </Button>
      </div>
    )
  }

  if (!replayData || !board) {
    return (
      <div className="flex items-center justify-center h-full text-muted-foreground text-sm">
        No replay data available
      </div>
    )
  }

  const snapshot =
    currentIndex >= 0 && currentIndex < replayData.snapshots.length
      ? replayData.snapshots[currentIndex]
      : null

  const headerContent = (
    <div className="flex items-center gap-2">
      <Button
        variant="ghost"
        size="icon"
        className="h-7 w-7 shrink-0"
        onClick={() => navigate(`/history/${matchId}`)}
      >
        <ArrowLeft className="h-4 w-4" />
      </Button>
      <div className="flex flex-col min-w-0">
        <span className="text-xs font-semibold text-muted-foreground truncate">
          Game {gameId}
        </span>
        <span className="text-[10px] text-muted-foreground/60">
          {replayData.snapshots.length} snapshots
        </span>
      </div>
    </div>
  )

  return (
    <div className="flex flex-col h-[calc(100vh-3.5rem)]">
      {/* Board — fills all remaining space */}
      <div className="flex-1 min-h-0">
        <BoardView
          board={board}
          transition={transition}
          promptText={snapshot?.promptText}
          promptOptions={snapshot?.promptOptions}
          headerContent={headerContent}
        />
      </div>

      {/* Timeline — fixed at bottom */}
      <div className="shrink-0">
        <ReplayTimeline
          snapshots={replayData.snapshots}
          currentIndex={currentIndex}
          onStepTo={handleStepTo}
        />
      </div>
    </div>
  )
}
