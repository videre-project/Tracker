import React, { useState, useEffect, useCallback, useRef } from "react"
import { useParams, useNavigate } from "react-router-dom"
import { getApiUrl } from "@/utils/api-config"
import { useClientState } from "@/hooks/use-client-state"
import { useNDJSONStream } from "@/hooks/use-ndjson-stream"
import { Button } from "@/components/ui/button"
import { ArrowLeft, Clock, Calendar, Trophy, Radio, Play, ChevronDown, ChevronUp } from "lucide-react"
import { Badge } from "@/components/ui/badge"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Skeleton } from "@/components/ui/skeleton"
import type { GameLogDTO, MatchDetailsDTO } from "@/types/api"
import { TYPE_ORDER } from "@/utils/game-log-rendering"
import { GameLogViewer } from "@/components/logs/GameLogViewer"

export function useMatchDetails(matchId: number | null) {
  const [data, setData] = useState<MatchDetailsDTO | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<any>(null)

  const { isReady: clientReady, loading: clientLoading } = useClientState()

  useEffect(() => {
    if (clientLoading || !matchId) return
    if (!clientReady) {
      setLoading(false)
      return
    }

    setLoading(true)
    setError(null)

    fetch(getApiUrl(`/api/games/match/${matchId}`))
      .then(res => {
        if (!res.ok) throw new Error(`HTTP error! status: ${res.status}`)
        return res.json()
      })
      .then(json => {
        setData(json)
        setLoading(false)
      })
      .catch(err => {
        console.error("Failed to fetch match details:", err)
        setError(err)
        setLoading(false)
      })
  }, [matchId, clientReady, clientLoading])

  return { data, loading, error }
}

export default function MatchDetails() {
  const { matchId } = useParams<{ matchId: string }>()
  const navigate = useNavigate()
  const parsedMatchId = matchId ? parseInt(matchId, 10) : null

  const { data: fetchedData, loading, error } = useMatchDetails(parsedMatchId)
  const { isReady: clientReady } = useClientState()

  // Local mutable copy of match data — receives both initial fetch and live SSE updates
  const [data, setData] = useState<MatchDetailsDTO | null>(null)
  const [liveLogCount, setLiveLogCount] = useState(0)
  const [expandedLogs, setExpandedLogs] = useState<Set<number>>(new Set())

  useEffect(() => {
    if (fetchedData) {
      setData(fetchedData)
      setLiveLogCount(0)
    }
  }, [fetchedData])

  // Refetch trigger for when SSE delivers a log for an unknown gameId
  const [refetchTrigger, setRefetchTrigger] = useState(0)
  const unknownGameIdRef = useRef(new Set<number>())

  useEffect(() => {
    if (refetchTrigger === 0 || !parsedMatchId || !clientReady) return
    fetch(getApiUrl(`/api/games/match/${parsedMatchId}`))
      .then(r => r.json())
      .then(json => { setData(json); unknownGameIdRef.current.clear() })
      .catch(console.error)
  }, [refetchTrigger, parsedMatchId, clientReady])

  // SSE message handler — appends new logs to the correct game
  const onSSEMessage = useCallback((dto: GameLogDTO) => {
    setData(prev => {
      if (!prev?.games) return prev
      const idx = prev.games.findIndex(g => g.id === (dto.gameId ?? 0))
      if (idx === -1) {
        // Unknown game — trigger a full refetch
        if (dto.gameId && !unknownGameIdRef.current.has(dto.gameId)) {
          unknownGameIdRef.current.add(dto.gameId)
          setTimeout(() => setRefetchTrigger(t => t + 1), 0)
        }
        return prev
      }
      return {
        ...prev,
        games: prev.games.map((g, i) => {
          if (i !== idx) return g
          const logs = [...(g.logs ?? []), dto]
          logs.sort((a, b) => {
            const na = a.nonce ?? 0, nb = b.nonce ?? 0
            if (na !== 0 && nb !== 0 && na === nb) {
              const ta = TYPE_ORDER[a.gameLogType] ?? 6
              const tb = TYPE_ORDER[b.gameLogType] ?? 6
              if (ta !== tb) return ta - tb
            }
            return new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime()
          })
          return { ...g, logs }
        }),
      }
    })
    setLiveLogCount(c => c + 1)
  }, [])

  // Connect to SSE stream (only after initial fetch completes)
  const streamUrl = parsedMatchId
    ? getApiUrl(`/api/games/match/${parsedMatchId}/watch`)
    : ""

  useNDJSONStream<GameLogDTO>({
    url: streamUrl,
    onMessage: onSSEMessage,
    enabled: clientReady && parsedMatchId != null && data != null,
    autoReconnect: true,
    reconnectDelay: 2000,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })

  if (error) {
    return (
      <div className="container mx-auto py-4 px-4 space-y-4">
        <Button variant="ghost" onClick={() => navigate('/history')} className="-ml-4 mb-4">
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back
        </Button>
        <div className="bg-destructive/15 text-destructive px-4 py-3 rounded-md text-sm font-medium">
          Error loading match details: {error.message || String(error)}
        </div>
      </div>
    )
  }

  if (loading || !data) {
    return (
      <div className="container mx-auto py-4 px-4 space-y-6">
        <Button variant="ghost" onClick={() => navigate('/history')} className="-ml-4">
          <ArrowLeft className="mr-2 h-4 w-4" />
          Back
        </Button>
        <div className="grid gap-6 md:grid-cols-2">
           <Skeleton className="h-[200px]" />
           <Skeleton className="h-[200px]" />
        </div>
        <Skeleton className="h-10 w-full" />
        <Skeleton className="h-[400px] w-full" />
      </div>
    )
  }

  const isInProgress = data.isActive || data.result === "In Progress"
  const resultVariant = isInProgress ? "secondary" : data.result === "Win" ? "default" : data.result === "Loss" ? "destructive" : "secondary"

  return (
    <div className="container mx-auto py-4 px-4 space-y-6">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" onClick={() => navigate('/history')} className="shrink-0">
          <ArrowLeft className="h-5 w-5" />
        </Button>
        <div className="flex-1">
          <h1 className="text-2xl font-bold tracking-tight flex items-center gap-3">
            {data.eventName}
            <Badge
              variant={resultVariant}
              className={`capitalize text-sm px-3 py-1${isInProgress ? " bg-yellow-500/15 text-yellow-700 dark:text-yellow-400 border-yellow-500/30" : ""}`}
            >
              {isInProgress ? "In Progress" : `Match ${data.result}`}
            </Badge>
          </h1>
          <p className="text-muted-foreground flex items-center gap-4 mt-1">
            <span className="flex items-center gap-1.5"><Calendar className="h-4 w-4" /> {new Date(data.startTime).toLocaleString()}</span>
            <span className="flex items-center gap-1.5"><Clock className="h-4 w-4" /> {data.duration}</span>
            <span className="flex items-center gap-1.5"><Trophy className="h-4 w-4" /> Record: {data.record}</span>
          </p>
        </div>
        <div className="flex items-center gap-2">
          {liveLogCount > 0 && (
            <Badge variant="success" className="text-xs">
              +{liveLogCount} live
            </Badge>
          )}
          <Button
            variant="outline"
            size="sm"
            className="shrink-0 gap-1.5 border-sidebar-border/60"
            onClick={() => navigate(`/history/${parsedMatchId}/watch`)}
          >
            <Radio className="h-3.5 w-3.5" />
            Watch Live
          </Button>
        </div>
      </div>

      <div className="grid gap-6 md:grid-cols-2">
         {/* Metadata Card */}
         <Card className="border-sidebar-border/60">
           <CardHeader>
             <CardTitle>Match Overview</CardTitle>
           </CardHeader>
           <CardContent className="space-y-4">
             <div className="grid grid-cols-2 gap-4">
               <div>
                 <p className="text-sm font-medium text-muted-foreground">Format</p>
                 <p className="text-base font-medium">{data.format}</p>
               </div>
               <div>
                 <p className="text-sm font-medium text-muted-foreground">Deck</p>
                 <p className="text-base font-medium">{data.deckName || "Unknown"}</p>
               </div>
               <div>
                 <p className="text-sm font-medium text-muted-foreground">Games Played</p>
                 <p className="text-base font-medium">{data.games.length}</p>
               </div>
               <div>
                 <p className="text-sm font-medium text-muted-foreground">Event ID</p>
                 <p className="text-base font-medium">{data.eventId}</p>
               </div>
             </div>
           </CardContent>
         </Card>
      </div>

      <h2 className="text-xl font-bold tracking-tight mt-8 mb-4">Games ({data.games.length})</h2>
      
      <div className="w-full space-y-4">
        {data.games.map((game) => {
          const gameResultVariant = game.result === "Win" ? "default" : game.result === "Loss" ? "destructive" : "secondary"
          const logsExpanded = expandedLogs.has(game.id)
          return (
            <div key={game.id} className="border rounded-lg bg-card border-sidebar-border/60">
              {/* Game header */}
              <div className="flex items-center justify-between px-6 py-4">
                <div className="flex items-center gap-4">
                  <span className="font-semibold text-lg">Game {game.gameNumber}</span>
                  <Badge variant={gameResultVariant} className="capitalize">{game.result}</Badge>
                  <span className="text-sm text-muted-foreground px-2 py-0.5 rounded-full bg-sidebar-accent/50">
                     On the {game.playDraw}
                  </span>
                </div>
                <div className="flex items-center gap-3 text-muted-foreground font-normal text-sm">
                  <Clock className="w-4 h-4" />
                  {game.duration}
                  <Button
                    variant="outline"
                    size="sm"
                    className="shrink-0 gap-1 h-7 text-xs border-sidebar-border/60"
                    onClick={() => navigate(`/history/${parsedMatchId}/game/${game.id}/replay`)}
                  >
                    <Play className="h-3 w-3" /> Replay
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    className="shrink-0 gap-1 h-7 text-xs"
                    onClick={() => setExpandedLogs(prev => {
                      const next = new Set(prev)
                      if (next.has(game.id)) next.delete(game.id)
                      else next.add(game.id)
                      return next
                    })}
                  >
                    {logsExpanded ? <ChevronUp className="h-4 w-4" /> : <ChevronDown className="h-4 w-4" />}
                    Log
                  </Button>
                </div>
              </div>

              {/* Collapsible game log */}
              {logsExpanded && (
                <div className="px-6 pt-2 pb-6 border-t border-sidebar-border/60">
                  <GameLogViewer
                    logs={game.logs}
                    timePrecision="seconds"
                    emptyMessage="No game logs recorded for this game."
                  />
                </div>
              )}
            </div>
          )
        })}
      </div>
    </div>
  )
}
