import { useState, useEffect } from "react"

export interface DashboardStats {
  overallWinrate: number
  playWinrate: number
  drawWinrate: number
  totalMatches: number
  playMatches: number
  drawMatches: number
  wins: number
  losses: number
  ties: number
  averageDuration: string
  durationTwoGames: string
  durationThreeGames: string
  performanceTrend: {
    date: string
    winrate: number
    playPoints: number
  }[]
  deckPerformance: {
    name: string
    matches: number
    winrate: number
    colors: string[] // e.g., ['U', 'R'] for blue-red
    keyCard: string // Card name for art display
  }[]
}

const MOCK_STATS: DashboardStats = {
  overallWinrate: 65.1,
  playWinrate: 71.3,
  drawWinrate: 59.8,
  totalMatches: 759,
  playMatches: 380,
  drawMatches: 379,
  wins: 494,
  losses: 265,
  ties: 12,
  averageDuration: "17m 31s",
  durationTwoGames: "14m 20s",
  durationThreeGames: "22m 45s",
  performanceTrend: [
    { date: "Sep 01", winrate: 55, playPoints: 100 },
    { date: "Sep 05", winrate: 60, playPoints: 120 },
    { date: "Sep 10", winrate: 58, playPoints: 110 },
    { date: "Sep 15", winrate: 65, playPoints: 150 },
    { date: "Sep 20", winrate: 70, playPoints: 180 },
    { date: "Sep 25", winrate: 68, playPoints: 170 },
    { date: "Sep 30", winrate: 72, playPoints: 200 },
  ],
  deckPerformance: [
    { name: "UR Murktide", matches: 150, winrate: 72.4, colors: ['U', 'R'], keyCard: 'Murktide Regent' },
    { name: "4c Creativity", matches: 98, winrate: 68.1, colors: ['W', 'U', 'B', 'R'], keyCard: 'Indomitable Creativity' },
    { name: "Hammer Time", matches: 82, winrate: 65.3, colors: ['W'], keyCard: 'Colossus Hammer' },
    { name: "Living End", matches: 55, winrate: 61.5, colors: ['B', 'R', 'G'], keyCard: 'Living End' },
    { name: "Burn", matches: 41, winrate: 58.9, colors: ['W', 'R'], keyCard: 'Lightning Bolt' },
  ],
}

export function useDashboardStats() {
  const [stats, setStats] = useState<DashboardStats | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    // Simulate API delay
    const timer = setTimeout(() => {
      setStats(MOCK_STATS)
      setLoading(false)
    }, 500)

    return () => clearTimeout(timer)
  }, [])

  return { stats, loading }
}
