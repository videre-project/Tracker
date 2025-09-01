import { useEffect, useState } from "react"
import type { ITournament, IEventStructure, ITournamentStateUpdate } from "@/types/api"

export type EventType = "league" | "swiss" | "elimination" | "draft" | "unknown"
export type GameStatus = "active" | "paused" | "scheduled" | "completed"

export interface ActiveGame {
  id: string;
  name: string;
  type: EventType;
  status: GameStatus;
  format: string;
  url: string;
  deck?: string;
  wins?: number;
  losses?: number;
  totalRounds?: number;
  currentRound?: number;
  totalSwissRounds?: number;
  pod?: string;
  startTime?: string;
  endTime?: string;
  timeRemaining?: string; // e.g. '12:34' or '5:00'
}

// Use EventStructure if available, fallback to format string
// League events do not have eventStructure; treat as 'league' for now.
// When ILeague is added to the API, update this logic to handle both types.
function inferEventTypeFromStructure(structure?: IEventStructure | string | null): EventType {
  if (!structure) return "league" // League events have no eventStructure
  if (typeof structure === "object") {
    if (structure.isDraft) return "draft"
    if (structure.isSingleElimination) return "elimination"
    if (structure.isSwiss) return "swiss"
    if (structure.isConstructed) return "league" // fallback for constructed
    if (structure.isLimited) return "draft" // fallback for limited
    // fallback to name if present
    const s = structure.name || ""
    if (/league/i.test(s)) return "league"
    if (/swiss/i.test(s)) return "swiss"
    if (/elim/i.test(s)) return "elimination"
    if (/draft/i.test(s)) return "draft"
    return "unknown"
  } else if (typeof structure === "string") {
    const s = structure
    if (/league/i.test(s)) return "league"
    if (/swiss/i.test(s)) return "swiss"
    if (/elim/i.test(s)) return "elimination"
    if (/draft/i.test(s)) return "draft"
    return "unknown"
  }
  return "unknown"
}

function inferGameStatus(t: ITournament): GameStatus {
  const now = Date.now()
  const start = t.startTime ? new Date(t.startTime).getTime() : 0
  const end = t.endTime ? new Date(t.endTime).getTime() : 0
  if (now < start) return "scheduled"
  if (now > end) return "completed"
  return "active"
}

function formatTimeShort(dateStr?: string): string | undefined {
  if (!dateStr) return undefined
  const d = new Date(dateStr)
  // Format as '11:00 AM' (no seconds)
  return d.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' })
}

function mapTournamentToActiveGame(t: ITournament): ActiveGame {
  return {
    id: String(t.id),
    name: t.description,
    type: inferEventTypeFromStructure((t as any).eventStructure ?? t.format),
    status: inferGameStatus(t),
    format: t.format,
    url: `/events/${t.id}`,
    startTime: t.startTime ? formatTimeShort(t.startTime) : undefined,
    endTime: t.endTime ? formatTimeShort(t.endTime) : undefined,
    totalRounds: t.totalRounds,
  }
}

export function useActiveGames() {
  const [games, setGames] = useState<ActiveGame[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setLoading(true)
    const controller = new AbortController()
    const url = "/api/Events/GetEventsList?stream=true"
    fetch(url, { signal: controller.signal })
      .then(res => {
        if (!res.ok) throw new Error("Failed to fetch events")
        const reader = res.body?.getReader()
        if (!reader) throw new Error("No stream reader available")
        let buffer = ""
        let gamesMap = new Map<string, ActiveGame>()
        function readStream() {
          return reader.read().then(({ done, value }) => {
            if (done) {
              setLoading(false)
              setGames(Array.from(gamesMap.values()).filter(g => g.status === "active"))
              return
            }
            buffer += new TextDecoder().decode(value)
            let lines = buffer.split("\n")
            buffer = lines.pop() || ""
            for (const line of lines) {
              if (!line.trim()) continue
              try {
                const t = JSON.parse(line)
                const game = mapTournamentToActiveGame(t)
                gamesMap.set(game.id, game)
              } catch {}
            }
            setGames(Array.from(gamesMap.values()).filter(g => g.status === "active"))
            return readStream()
          })
        }
        return readStream()
      })
      .catch(e => {
        setError(e.message)
        setLoading(false)
      })

    // Stream tournament state updates for active events
    const stateController = new AbortController()
    const stateUrl = "/api/Events/WatchTournamentUpdates"
    fetch(stateUrl, { signal: stateController.signal, headers: { Accept: "application/x-ndjson" } })
      .then(res => {
        if (!res.ok) throw new Error("Failed to stream tournament updates")
        const reader = res.body?.getReader()
        if (!reader) throw new Error("No stream reader available")
        let buffer = ""
        function readStateStream() {
          return reader.read().then(({ done, value }) => {
            if (done) return
            buffer += new TextDecoder().decode(value)
            let lines = buffer.split("\n")
            buffer = lines.pop() || ""
            setGames(prevGames => {
              const gamesById = new Map(prevGames.map(g => [g.id, { ...g }]))
              for (const line of lines) {
                if (!line.trim()) continue
                try {
                  const update: ITournamentStateUpdate = JSON.parse(line)
                  const g = gamesById.get(String(update.id))
                  if (g) {
                    g.currentRound = update.currentRound
                    // Calculate time remaining in round
                    if (update.roundEndTime) {
                      const now = Date.now()
                      const end = new Date(update.roundEndTime).getTime()
                      const ms = Math.max(0, end - now)
                      const min = Math.floor(ms / 60000)
                      const sec = Math.floor((ms % 60000) / 1000)
                      g.timeRemaining = `${min}:${sec.toString().padStart(2, '0')}`
                    } else {
                      g.timeRemaining = undefined
                    }
                  }
                } catch {}
              }
              return Array.from(gamesById.values())
            })
            return readStateStream()
          })
        }
        return readStateStream()
      })
      .catch(() => {})

    return () => {
      controller.abort()
      stateController.abort()
    }
  }, [])

  return { games, loading, error }
}

export function useUpcomingGames() {
  const [games, setGames] = useState<ActiveGame[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setLoading(true)
    // Use NDJSON streaming for real-time updates
    const controller = new AbortController()
    const url = "/api/Events/GetEventsList?stream=true"
    fetch(url, { signal: controller.signal })
      .then(res => {
        if (!res.ok) throw new Error("Failed to fetch events")
        const reader = res.body?.getReader()
        if (!reader) throw new Error("No stream reader available")
        let buffer = ""
        let gamesMap = new Map<string, ActiveGame>()
        function readStream() {
          return reader.read().then(({ done, value }) => {
            if (done) {
              setLoading(false)
              setGames(Array.from(gamesMap.values()).filter(g => g.status === "scheduled"))
              return
            }
            buffer += new TextDecoder().decode(value)
            let lines = buffer.split("\n")
            buffer = lines.pop() || ""
            for (const line of lines) {
              if (!line.trim()) continue
              try {
                const t = JSON.parse(line)
                const game = mapTournamentToActiveGame(t)
                gamesMap.set(game.id, game)
              } catch {}
            }
            setGames(Array.from(gamesMap.values()).filter(g => g.status === "scheduled"))
            return readStream()
          })
        }
        return readStream()
      })
      .catch(e => {
        setError(e.message)
        setLoading(false)
      })
    return () => controller.abort()
  }, [])

  return { games, loading, error }
}
