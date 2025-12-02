import { useState, useEffect, useMemo } from "react"
import { useDashboardStats } from "@/hooks/use-dashboard-stats"
import { Link } from "react-router-dom"

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { ResponsiveContainer, YAxis, Tooltip, ScatterChart, Scatter, CartesianGrid, XAxis, Customized, ReferenceArea, ComposedChart, Line, Area, ReferenceLine } from "recharts"
import { Button } from "@/components/ui/button"
import { Calendar, ChevronDown, Trophy, Clock, Swords, Dices } from "lucide-react"
import { CardArt } from "@/components/card-art"
import { Skeleton } from "@/components/ui/skeleton"

const DensityLayer = (props: any) => {
  const { xAxisMap, yAxisMap, data } = props
  if (!xAxisMap || !yAxisMap) return null

  const xScale = (Object.values(xAxisMap)[0] as any).scale
  const yScale = (Object.values(yAxisMap)[0] as any).scale

  return (
    <g>
      <defs>
        <radialGradient id="densityGradient">
          <stop offset="0%" stopColor="hsl(var(--primary))" stopOpacity={0.2} />
          <stop offset="100%" stopColor="hsl(var(--primary))" stopOpacity={0} />
        </radialGradient>
      </defs>
      {data.map((deck: any) => {
        const x = xScale(deck.winrate)
        const y = yScale(deck.matches)

        const p = deck.winrate / 100
        const n = deck.matches
        const sd = Math.sqrt((p * (1 - p)) / n) * 100

        // 2 sigma width (approx 95% CI)
        const rx = Math.abs(xScale(deck.winrate + sd * 2) - xScale(deck.winrate))
        const ry = 20

        return (
          <ellipse
            key={deck.name}
            cx={x}
            cy={y}
            rx={rx}
            ry={ry}
            fill="url(#densityGradient)"
          />
        )
      })}
    </g>
  )
}

function BetaChart({ winrate, matches, color }: { winrate: number; matches: number; color: string }) {
  const width = 100
  const height = 32
  const wins = Math.round(matches * (winrate / 100))
  const losses = matches - wins
  const alpha = wins + 1
  const beta = losses + 1

  const points: [number, number][] = []
  let maxLogY = -Infinity

  for (let i = 0; i <= 200; i++) {
    const x = i / 200
    if (x <= 0 || x >= 1) continue

    const logY = (alpha - 1) * Math.log(x) + (beta - 1) * Math.log(1 - x)
    if (logY > maxLogY) maxLogY = logY
    points.push([x * 100, logY])
  }

  const pathData = points
    .map(([x, logY]) => {
      const normalizedY = Math.exp(logY - maxLogY)
      return `${x},${height - normalizedY * height}`
    })
    .join(" L ")

  return (
    <svg
      className={`absolute -top-[28px] left-0 h-[32px] w-full overflow-visible opacity-30 ${color}`}
      viewBox={`0 0 ${width} ${height}`}
      preserveAspectRatio="none"
    >
      <path d={`M 0,${height} L ${pathData} L ${width},${height} Z`} fill="currentColor" />
      <path d={`M 0,${height} L ${pathData} L ${width},${height}`} stroke="currentColor" strokeWidth="0.5" fill="none" vectorEffect="non-scaling-stroke" />
    </svg>
  )
}

function getBetaCI(winrate: number, matches: number) {
  const wins = Math.round(matches * (winrate / 100))
  const losses = matches - wins
  const alpha = wins + 1
  const beta = losses + 1

  const mean = alpha / (alpha + beta)
  const variance = (alpha * beta) / (Math.pow(alpha + beta, 2) * (alpha + beta + 1))
  const sd = Math.sqrt(variance)

  // 95% CI approx (Mean +/- 2SD)
  const start = Math.max(0, (mean - 2 * sd) * 100)
  const end = Math.min(100, (mean + 2 * sd) * 100)

  return { start, end }
}


export default function Home() {
  const { stats, loading } = useDashboardStats()

  // Calculate trend data with CI bands
  // Generate dummy trend data with real calculations
  const [timeRange, setTimeRange] = useState('14D')

  const trendData = useMemo(() => {
    const days = 90
    const data = []
    const now = new Date()

    for (let i = days; i >= 0; i--) {
      const date = new Date(now)
      date.setDate(date.getDate() - i)
      const dateStr = date.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })

      // Random number of matches between 10 and 30
      const n = Math.floor(Math.random() * 20) + 10
      // Target winrate fluctuates randomly around 55%
      const targetWr = 0.55 + (Math.random() * 0.2 - 0.1)

      let wins = 0
      for (let j = 0; j < n; j++) {
        if (Math.random() < targetWr) wins++
      }

      const p = wins / n
      const winrate = p * 100
      const se = Math.sqrt((p * (1 - p)) / n) * 100

      data.push({
        date: dateStr,
        winrate,
        matches: n,
        ci95: [Math.max(0, winrate - 1.96 * se), Math.min(100, winrate + 1.96 * se)],
        ci80: [Math.max(0, winrate - 1.28 * se), Math.min(100, winrate + 1.28 * se)],
        ci50: [Math.max(0, winrate - 0.674 * se), Math.min(100, winrate + 0.674 * se)],
      })
    }

    // Calculate 7-day rolling average
    for (let i = 0; i < data.length; i++) {
      const windowStart = Math.max(0, i - 6)
      const windowData = data.slice(windowStart, i + 1)
      const avgWinrate = windowData.reduce((sum, d) => sum + d.winrate, 0) / windowData.length
      data[i].rollingAvg = avgWinrate
    }

    return data
  }, [])

  // Zoom state
  const [left, setLeft] = useState<string | number>('dataMin')
  const [right, setRight] = useState<string | number>('dataMax')
  const [refAreaLeft, setRefAreaLeft] = useState<string | number | null>(null)
  const [refAreaRight, setRefAreaRight] = useState<string | number | null>(null)

  const handleTimeRangeChange = (range: string) => {
    setTimeRange(range)
    if (range === 'ALL') {
      setLeft('dataMin')
      setRight('dataMax')
      return
    }

    const daysMap: Record<string, number> = { '7D': 7, '14D': 14, '30D': 30 }
    const days = daysMap[range]

    if (trendData.length > 0) {
      const startIndex = Math.max(0, trendData.length - days)
      setLeft(trendData[startIndex].date)
      setRight('dataMax')
    }
  }

  // Set initial zoom to 14D on mount
  useEffect(() => {
    if (trendData.length > 0) {
      handleTimeRangeChange('14D')
    }
  }, [trendData])

  const zoomedData = useMemo(() => {
    if (left === 'dataMin' && right === 'dataMax') return trendData

    const leftIndex = left === 'dataMin' ? 0 : trendData.findIndex((d: any) => d.date === left)
    const rightIndex = right === 'dataMax' ? trendData.length - 1 : trendData.findIndex((d: any) => d.date === right)

    if (leftIndex === -1 || rightIndex === -1) return trendData

    return trendData.slice(leftIndex, rightIndex + 1)
  }, [trendData, left, right])

  const xAxisTicks = useMemo(() => {
    if (zoomedData.length === 0) return []
    if (zoomedData.length <= 14) return zoomedData.map(d => d.date)

    const maxTicks = 14
    const ticks = []
    const step = Math.ceil((zoomedData.length - 1) / (maxTicks - 1))

    for (let i = 0; i < zoomedData.length - 1; i += step) {
      ticks.push(zoomedData[i].date)
    }

    // Always include the last date
    ticks.push(zoomedData[zoomedData.length - 1].date)

    return ticks
  }, [zoomedData])

  const gradientOffsets = useMemo(() => {
    const getOff = (key: 'winrate' | 'ci95' | 'ci80' | 'ci50') => {
      let values: number[] = []
      if (key === 'winrate') {
        values = zoomedData.map(d => d.winrate)
      } else {
        values = zoomedData.flatMap(d => d[key] as number[])
      }

      const max = Math.max(...values)
      const min = Math.min(...values)

      if (max <= 50) return 0
      if (min >= 50) return 1
      return (max - 50) / (max - min)
    }

    return {
      winrate: getOff('winrate'),
      ci95: getOff('ci95'),
      ci80: getOff('ci80'),
      ci50: getOff('ci50'),
    }
  }, [zoomedData])

  const [hoverPercent, setHoverPercent] = useState<number | null>(null)
  const [hoverPosition, setHoverPosition] = useState<{ x: number; y: number } | null>(null)

  const zoom = () => {
    if (refAreaLeft === refAreaRight || refAreaRight === null || refAreaLeft === null) {
      setRefAreaLeft(null)
      setRefAreaRight(null)
      return
    }

    let l = refAreaLeft
    let r = refAreaRight

    // Ensure correct order
    const lIndex = trendData.findIndex((d: any) => d.date === l)
    const rIndex = trendData.findIndex((d: any) => d.date === r)

    if (lIndex === -1 || rIndex === -1) {
      setRefAreaLeft(null)
      setRefAreaRight(null)
      return
    }

    if (lIndex > rIndex) {
      [l, r] = [r, l]
    }

    setRefAreaLeft(null)
    setRefAreaRight(null)
    setLeft(l)
    setRight(r)
    setTimeRange('CUSTOM') // Clear time range selection on manual zoom
  }

  const zoomOut = () => {
    handleTimeRangeChange('ALL')
  }

  const handleMouseMove = (e: React.MouseEvent<HTMLDivElement>) => {
    const rect = e.currentTarget.getBoundingClientRect()
    const x = e.clientX - rect.left
    const percent = Math.max(0, Math.min(100, (x / rect.width) * 100))
    setHoverPercent(percent)
    setHoverPosition({ x: e.clientX, y: e.clientY })
  }

  const handleMouseLeave = () => {
    setHoverPercent(null)
    setHoverPosition(null)
  }

  const getDurationPercentage = (duration: string) => {
    const match = duration.match(/(\d+)m\s*(\d+)s/)
    if (!match) return 0
    const minutes = parseInt(match[1])
    const seconds = parseInt(match[2])
    const totalSeconds = minutes * 60 + seconds
    // 25 min clock (1500s) for full bar
    return Math.min(100, (totalSeconds / 1500) * 100)
  }

  if (loading || !stats) {
    return (
      <div className="flex flex-col gap-4 p-4 pt-0">
        <div className="grid gap-4 lg:grid-cols-3">
          <div className="lg:col-span-2 flex flex-col gap-4">
            {/* Filters Skeleton */}
            <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between mt-2">
              <div className="flex items-center gap-2">
                <Skeleton className="h-8 w-[200px]" />
                <Skeleton className="h-8 w-[120px]" />
              </div>
              <Skeleton className="h-8 w-[180px]" />
            </div>

            {/* Stat Cards Skeleton */}
            <div className="grid gap-4 md:grid-cols-3">
              <Card className="border-sidebar-border/60">
                <CardHeader className="flex flex-row items-center justify-between space-y-0 p-4 pb-2">
                  <Skeleton className="h-5 w-[100px]" />
                  <Skeleton className="h-4 w-4" />
                </CardHeader>
                <CardContent className="p-4 pt-0">
                  <Skeleton className="h-9 w-[60px] mb-4" />
                  <Skeleton className="h-2 w-full rounded-full mb-2" />
                  <div className="flex justify-between">
                    <Skeleton className="h-4 w-[50px]" />
                    <Skeleton className="h-4 w-[50px]" />
                  </div>
                </CardContent>
              </Card>

              <Card className="border-sidebar-border/60">
                <CardHeader className="flex flex-row items-center justify-between space-y-0 p-4 pb-2">
                  <Skeleton className="h-5 w-[120px]" />
                  <Skeleton className="h-4 w-4" />
                </CardHeader>
                <CardContent className="p-4 pt-0">
                  <div className="space-y-4 pt-2">
                    <div className="space-y-2">
                      <div className="flex justify-between">
                        <Skeleton className="h-4 w-[70px]" />
                        <Skeleton className="h-4 w-[30px]" />
                      </div>
                      <Skeleton className="h-2 w-full" />
                    </div>
                    <div className="space-y-2">
                      <div className="flex justify-between">
                        <Skeleton className="h-4 w-[70px]" />
                        <Skeleton className="h-4 w-[30px]" />
                      </div>
                      <Skeleton className="h-2 w-full" />
                    </div>
                  </div>
                </CardContent>
              </Card>

              <Card className="border-sidebar-border/60">
                <CardHeader className="flex flex-row items-start justify-between space-y-0 p-4 pb-0">
                  <div className="space-y-1">
                    <Skeleton className="h-5 w-[100px]" />
                    <Skeleton className="h-4 w-[40px]" />
                  </div>
                  <Skeleton className="h-4 w-4" />
                </CardHeader>
                <CardContent className="p-4 pt-0 -mt-6">
                  <div className="mt-2 flex justify-end">
                    <Skeleton className="h-9 w-[80px]" />
                  </div>
                  <div className="mt-2 flex flex-col gap-2">
                    <Skeleton className="h-5 w-full" />
                    <Skeleton className="h-5 w-full" />
                  </div>
                </CardContent>
              </Card>
            </div>

            {/* Performance Trend Skeleton */}
            <Card className="border-sidebar-border/60">
              <CardHeader className="flex flex-row items-center justify-between px-4 py-2">
                <Skeleton className="h-6 w-[140px]" />
                <Skeleton className="h-6 w-[180px]" />
              </CardHeader>
              <CardContent className="p-4 pt-0">
                <Skeleton className="h-[310px] w-full" />
              </CardContent>
            </Card>
          </div>

          {/* Right Column: Deck Performance Skeleton */}
          <Card className="border-sidebar-border/60 h-full">
            <CardHeader className="flex flex-row items-center justify-between px-4 py-3">
              <Skeleton className="h-6 w-[140px]" />
            </CardHeader>
            <CardContent className="p-0">
              <div className="p-4">
                <Skeleton className="h-[175px] w-full mb-4" />
              </div>
              <div className="flex flex-col gap-0">
                {[1, 2, 3, 4, 5].map(i => (
                  <div key={i} className="flex items-center gap-3 border-b border-sidebar-border/40 p-3">
                    <Skeleton className="h-8 w-8 rounded-full" />
                    <div className="flex-1 space-y-1">
                      <Skeleton className="h-3 w-[120px]" />
                      <Skeleton className="h-2 w-[80px]" />
                    </div>
                    <Skeleton className="h-4 w-[40px]" />
                  </div>
                ))}
              </div>
            </CardContent>
          </Card>
        </div>
      </div>
    )
  }

  const playCI = getBetaCI(stats.playWinrate, stats.playMatches)
  const drawCI = getBetaCI(stats.drawWinrate, stats.drawMatches)

  return (
    <div className="flex flex-col gap-4 p-4 pt-0">
      {/* Main Content Area */}
      <div className="grid gap-4 lg:grid-cols-3">
        {/* Left Column: KPI Cards + Performance Trend */}
        <div className="lg:col-span-2 flex flex-col gap-4">
          {/* Filters & Date Picker */}
          <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between mt-2">
            <div className="flex items-center gap-2">
              <div className="flex items-center rounded-lg border border-sidebar-border/60 bg-card p-1">
                <Button variant="ghost" size="sm" className="h-6 rounded-md px-3 text-muted-foreground hover:text-foreground">
                  All
                </Button>
                <Button variant="secondary" size="sm" className="h-6 rounded-md px-3 shadow-sm">
                  Constructed
                </Button>
                <Button variant="ghost" size="sm" className="h-6 rounded-md px-3 text-muted-foreground hover:text-foreground">
                  Limited
                </Button>
              </div>
              <Button variant="outline" size="sm" className="h-8 gap-2 border-dashed border-sidebar-border/60">
                <span className="text-muted-foreground">Format:</span>
                <span className="font-medium">Modern</span>
                <ChevronDown className="h-4 w-4 text-muted-foreground" />
              </Button>
            </div>
            <Button variant="outline" size="sm" className="h-8 gap-2 border-sidebar-border/60">
              <Calendar className="h-4 w-4 text-muted-foreground" />
              <span>{(() => {
                const today = new Date()
                const start = new Date(today)
                start.setDate(today.getDate() - 90)
                const fmt = (d: Date) => d.toLocaleDateString('en-US', { month: '2-digit', day: '2-digit', year: 'numeric' })
                return `${fmt(start)} - ${fmt(today)}`
              })()}</span>
              <ChevronDown className="h-4 w-4 text-muted-foreground" />
            </Button>
          </div>
          {/* Stat Cards */}
          <div className="grid gap-4 md:grid-cols-3">
            <Card className="border-sidebar-border/60">
              <CardHeader className="flex flex-row items-center justify-between space-y-0 p-4 pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">Overall Winrate</CardTitle>
                <Trophy className="h-4 w-4 text-muted-foreground" />
              </CardHeader>
              <CardContent className="p-4 pt-0">
                <div className="text-3xl font-bold">{stats.overallWinrate}%</div>
                <div className="mt-4 flex flex-col gap-2">
                  <div className="relative">
                    <div className="absolute bottom-full right-0 mb-2 text-xs font-medium text-muted-foreground">
                      {stats.ties} Ties
                    </div>
                    <div className="relative flex h-2 w-full overflow-hidden rounded-full">
                      <div className="h-full bg-emerald-500" style={{ width: `${stats.overallWinrate}%` }} />
                      <div className="h-full flex-1 bg-rose-500" />
                      <div className="h-full bg-muted-foreground/30" style={{ width: `${(stats.ties / stats.totalMatches) * 100}%` }} />
                      <div className="absolute left-1/2 top-0 h-full w-[2px] -translate-x-1/2 bg-background" />
                    </div>
                  </div>
                  <div className="flex justify-between text-xs">
                    <span className="font-medium text-emerald-500">{stats.wins} Wins</span>
                    <span className="font-medium text-rose-500">{stats.losses} Losses</span>
                  </div>
                </div>
              </CardContent>
            </Card>

            <Card className="border-sidebar-border/60">
              <CardHeader className="flex flex-row items-center justify-between space-y-0 p-4 pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">Play / Draw Winrate</CardTitle>
                <Dices className="h-4 w-4 text-muted-foreground" />
              </CardHeader>
              <CardContent className="p-4 pt-0">
                <div className="flex flex-col gap-4 pt-2">
                  {/* Play Slider */}
                  <div className="space-y-2">
                    <div className="relative z-10 flex items-center justify-between text-xs">
                      <span className="text-muted-foreground">On the Play</span>
                      <span className={`font-bold ${stats.playWinrate >= 50 ? "text-emerald-500" : "text-rose-500"}`}>
                        {stats.playWinrate}%
                      </span>
                    </div>
                    <div
                      className="relative h-2 w-full"
                      onMouseMove={handleMouseMove}
                      onMouseLeave={handleMouseLeave}
                    >
                      <BetaChart
                        winrate={stats.playWinrate}
                        matches={stats.playMatches}
                        color={stats.playWinrate >= 50 ? "text-emerald-500" : "text-rose-500"}
                      />
                      <div className="absolute top-1/2 h-1 w-full -translate-y-1/2 rounded-full bg-secondary" />
                      <div
                        className={`absolute top-1/2 h-1 -translate-y-1/2 rounded-full ${stats.playWinrate >= 50 ? "bg-emerald-500" : "bg-rose-500"
                          }`}
                        style={{ left: `${playCI.start}%`, width: `${playCI.end - playCI.start}%` }}
                      />
                      {/* CI Whiskers */}
                      <div
                        className={`absolute top-1/2 h-2 w-px -translate-y-1/2 ${stats.playWinrate >= 50 ? "bg-emerald-500" : "bg-rose-500"
                          }`}
                        style={{ left: `${playCI.start}%` }}
                      />
                      <div
                        className={`absolute top-1/2 h-2 w-px -translate-y-1/2 ${stats.playWinrate >= 50 ? "bg-emerald-500" : "bg-rose-500"
                          }`}
                        style={{ left: `${playCI.end}%` }}
                      />
                      <div className="absolute left-1/2 top-1/2 h-3 w-px -translate-y-1/2 bg-muted-foreground/30" />
                      {hoverPercent !== null && hoverPosition && (
                        <>
                          <div
                            className="absolute top-1/2 z-20 h-3 w-px -translate-y-1/2 bg-white shadow-[0_0_4px_rgba(0,0,0,0.5)]"
                            style={{ left: `${hoverPercent}%` }}
                          />
                          <div
                            className="pointer-events-none fixed z-50 rounded border bg-popover px-2 py-1 text-xs font-medium text-popover-foreground shadow-md"
                            style={{ left: `${hoverPosition.x + 8}px`, top: `${hoverPosition.y + 8}px` }}
                          >
                            {hoverPercent.toFixed(1)}%
                          </div>
                        </>
                      )}
                      <div
                        className={`absolute top-1/2 z-10 h-3 w-3 -translate-x-1/2 -translate-y-1/2 rounded-full border-2 border-background shadow-sm ${stats.playWinrate >= 50 ? "bg-emerald-500" : "bg-rose-500"
                          }`}
                        style={{ left: `${stats.playWinrate}%` }}
                      />
                    </div>
                  </div>

                  {/* Draw Slider */}
                  <div className="space-y-2">
                    <div className="relative z-10 flex items-center justify-between text-xs">
                      <span className="text-muted-foreground">On the Draw</span>
                      <span className={`font-bold ${stats.drawWinrate >= 50 ? "text-emerald-500" : "text-rose-500"}`}>
                        {stats.drawWinrate}%
                      </span>
                    </div>
                    <div
                      className="relative h-2 w-full"
                      onMouseMove={handleMouseMove}
                      onMouseLeave={handleMouseLeave}
                    >
                      <BetaChart
                        winrate={stats.drawWinrate}
                        matches={stats.drawMatches}
                        color={stats.drawWinrate >= 50 ? "text-emerald-500" : "text-rose-500"}
                      />
                      <div className="absolute top-1/2 h-1 w-full -translate-y-1/2 rounded-full bg-secondary" />
                      <div
                        className={`absolute top-1/2 h-1 -translate-y-1/2 rounded-full ${stats.drawWinrate >= 50 ? "bg-emerald-500" : "bg-rose-500"
                          }`}
                        style={{ left: `${drawCI.start}%`, width: `${drawCI.end - drawCI.start}%` }}
                      />
                      {/* CI Whiskers */}
                      <div
                        className={`absolute top-1/2 h-2 w-px -translate-y-1/2 ${stats.drawWinrate >= 50 ? "bg-emerald-500" : "bg-rose-500"
                          }`}
                        style={{ left: `${drawCI.start}%` }}
                      />
                      <div
                        className={`absolute top-1/2 h-2 w-px -translate-y-1/2 ${stats.drawWinrate >= 50 ? "bg-emerald-500" : "bg-rose-500"
                          }`}
                        style={{ left: `${drawCI.end}%` }}
                      />
                      <div className="absolute left-1/2 top-1/2 h-3 w-px -translate-y-1/2 bg-muted-foreground/30" />
                      {hoverPercent !== null && hoverPosition && (
                        <>
                          <div
                            className="absolute top-1/2 z-20 h-3 w-px -translate-y-1/2 bg-white shadow-[0_0_4px_rgba(0,0,0,0.5)]"
                            style={{ left: `${hoverPercent}%` }}
                          />
                          <div
                            className="pointer-events-none fixed z-50 rounded border bg-popover px-2 py-1 text-xs font-medium text-popover-foreground shadow-md"
                            style={{ left: `${hoverPosition.x + 8}px`, top: `${hoverPosition.y + 8}px` }}
                          >
                            {hoverPercent.toFixed(1)}%
                          </div>
                        </>
                      )}
                      <div
                        className={`absolute top-1/2 z-10 h-3 w-3 -translate-x-1/2 -translate-y-1/2 rounded-full border-2 border-background shadow-sm ${stats.drawWinrate >= 50 ? "bg-emerald-500" : "bg-rose-500"
                          }`}
                        style={{ left: `${stats.drawWinrate}%` }}
                      />
                    </div>
                  </div>
                </div>
              </CardContent>
            </Card>

            <Card className="border-sidebar-border/60">
              <CardHeader className="flex flex-row items-start justify-between space-y-0 p-4 pb-0">
                <CardTitle className="text-sm font-medium text-muted-foreground">
                  Avg. Match Clock
                  <br />
                  (25m)
                </CardTitle>
                <Clock className="mt-1 h-4 w-4 text-muted-foreground" />
              </CardHeader>
              <CardContent className="p-4 pt-0 -mt-6">
                <div className="mt-2 text-right text-[1.6rem] font-bold">{stats.averageDuration}</div>
                <div className="mt-2 flex flex-col gap-2">
                  <div className="relative h-5 w-full overflow-hidden rounded-md bg-secondary/50">
                    <div
                      className="h-full bg-muted-foreground/20"
                      style={{ width: `${getDurationPercentage(stats.durationTwoGames)}%` }}
                    />
                    <div className="absolute inset-0 grid grid-cols-2">
                      <div className="border-r-[3px] border-background/50" />
                      <div />
                    </div>
                    <div className="absolute inset-0 flex items-center justify-between px-2 text-xs font-medium">
                      <span className="text-muted-foreground">2 Games</span>
                      <span>{stats.durationTwoGames}</span>
                    </div>
                  </div>
                  <div className="relative h-5 w-full overflow-hidden rounded-md bg-secondary/50">
                    <div
                      className="h-full bg-muted-foreground/20"
                      style={{ width: `${getDurationPercentage(stats.durationThreeGames)}%` }}
                    />
                    <div className="absolute inset-0 grid grid-cols-3">
                      <div className="border-r-[3px] border-background/50" />
                      <div className="border-r-[3px] border-background/50" />
                      <div />
                    </div>
                    <div className="absolute inset-0 flex items-center justify-between px-2 text-xs font-medium">
                      <span className="text-muted-foreground">3 Games</span>
                      <span>{stats.durationThreeGames}</span>
                    </div>
                  </div>
                </div>
              </CardContent>
            </Card>
          </div>

          {/* Performance Trend Chart */}
          <Card className="border-sidebar-border/60">
            <CardHeader className="flex flex-row items-center justify-between px-4 py-2">
              <CardTitle className="text-base font-medium">Performance Trend</CardTitle>
              <div className="flex items-center gap-2">
                {left !== 'dataMin' && (
                  <Button variant="outline" size="sm" onClick={zoomOut} className="h-6 px-2 text-xs">
                    Reset
                  </Button>
                )}
                <div className="flex items-center gap-1 rounded-md bg-muted/50 p-0.5">
                  {['7D', '14D', '30D', 'ALL'].map((range) => (
                    <Button
                      key={range}
                      variant={timeRange === range ? 'secondary' : 'ghost'}
                      size="sm"
                      onClick={() => handleTimeRangeChange(range)}
                      className={`h-6 px-2 text-xs ${timeRange === range ? 'shadow-sm' : 'text-muted-foreground hover:text-foreground'}`}
                    >
                      {range}
                    </Button>
                  ))}
                </div>
              </div>
            </CardHeader>
            <CardContent className="p-4 pt-0">
              <div className="h-[310px] w-full select-none outline-none overflow-visible">
                <ResponsiveContainer width="100%" height="100%">
                  <ComposedChart
                    data={zoomedData}
                    margin={{ top: 10, right: 10, bottom: 0, left: -26 }}
                    onMouseDown={(e: any) => e && setRefAreaLeft(e.activeLabel)}
                    onMouseMove={(e: any) => refAreaLeft && e && setRefAreaRight(e.activeLabel)}
                    onMouseUp={zoom}
                  >
                    <defs>
                      <linearGradient id="splitColorWinrate" x1="0" y1="0" x2="0" y2="1">
                        <stop offset={gradientOffsets.winrate} stopColor="#10b981" stopOpacity={1} />
                        <stop offset={gradientOffsets.winrate} stopColor="#f43f5e" stopOpacity={1} />
                      </linearGradient>
                      <linearGradient id="splitColor95" x1="0" y1="0" x2="0" y2="1">
                        <stop offset={gradientOffsets.ci95} stopColor="#10b981" stopOpacity={1} />
                        <stop offset={gradientOffsets.ci95} stopColor="#f43f5e" stopOpacity={1} />
                      </linearGradient>
                      <linearGradient id="splitColor80" x1="0" y1="0" x2="0" y2="1">
                        <stop offset={gradientOffsets.ci80} stopColor="#10b981" stopOpacity={1} />
                        <stop offset={gradientOffsets.ci80} stopColor="#f43f5e" stopOpacity={1} />
                      </linearGradient>
                      <linearGradient id="splitColor50" x1="0" y1="0" x2="0" y2="1">
                        <stop offset={gradientOffsets.ci50} stopColor="#10b981" stopOpacity={1} />
                        <stop offset={gradientOffsets.ci50} stopColor="#f43f5e" stopOpacity={1} />
                      </linearGradient>
                    </defs>
                    <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" opacity={0.3} vertical={false} />
                    <ReferenceLine y={50} stroke="hsl(var(--muted-foreground))" strokeDasharray="3 3" opacity={0.5} />
                    <XAxis
                      dataKey="date"
                      tick={{ fontSize: 10, fill: 'hsl(var(--muted-foreground))' }}
                      tickLine={false}
                      axisLine={false}
                      dy={10}
                      ticks={xAxisTicks}
                      interval={0}
                    />
                    <YAxis
                      domain={[0, 100]}
                      tick={{ fontSize: 10, fill: 'hsl(var(--muted-foreground))' }}
                      tickLine={false}
                      axisLine={false}
                      unit="%"
                    />
                    <Tooltip
                      content={({ active, payload, label }) => {
                        if (active && payload && payload.length) {
                          return (
                            <div className="rounded-lg border bg-background p-2 shadow-sm">
                              <div className="text-xs font-medium">{label}</div>
                              <div className="mt-1 flex flex-col gap-1">
                                <span className="text-xs font-medium text-primary">
                                  Winrate: {payload.find(p => p.dataKey === 'winrate')?.value?.toString().slice(0, 4)}%
                                  <span className="ml-2 text-muted-foreground font-normal">
                                    ({payload[0].payload.matches} matches)
                                  </span>
                                </span>
                                <span className="text-[10px] text-muted-foreground">
                                  CI: {Math.round((payload.find(p => p.dataKey === 'ci95')?.value as number[])[0])}% - {Math.round((payload.find(p => p.dataKey === 'ci95')?.value as number[])[1])}%
                                </span>
                              </div>
                            </div>
                          )
                        }
                        return null
                      }}
                    />
                    {/* Rolling average line (behind other elements) */}
                    <Line
                      type="monotone"
                      dataKey="rollingAvg"
                      stroke="rgba(255, 255, 255, 0.4)"
                      strokeWidth={2}
                      dot={false}
                      activeDot={false}
                      animationDuration={150}
                    />
                    <Area
                      type="monotone"
                      dataKey="ci95"
                      stroke="none"
                      fill="url(#splitColor95)"
                      fillOpacity={0.05}
                      activeDot={false}
                      animationDuration={150}
                    />
                    <Area
                      type="monotone"
                      dataKey="ci80"
                      stroke="none"
                      fill="url(#splitColor80)"
                      fillOpacity={0.1}
                      activeDot={false}
                      animationDuration={150}
                    />
                    <Area
                      type="monotone"
                      dataKey="ci50"
                      stroke="none"
                      fill="url(#splitColor50)"
                      fillOpacity={0.15}
                      activeDot={false}
                      animationDuration={150}
                    />
                    <Line
                      type="monotone"
                      dataKey="winrate"
                      stroke="url(#splitColorWinrate)"
                      strokeWidth={2}
                      animationDuration={150}
                      dot={(props: any) => {
                        const { cx, cy, payload } = props
                        const color = payload.winrate >= 50 ? "#10b981" : "#f43f5e"
                        return (
                          <circle cx={cx} cy={cy} r={4} fill="hsl(var(--background))" stroke={color} strokeWidth={2} />
                        )
                      }}
                      activeDot={(props: any) => {
                        const { cx, cy, payload } = props
                        const color = payload.winrate >= 50 ? "#10b981" : "#f43f5e"
                        return (
                          <circle cx={cx} cy={cy} r={6} fill={color} stroke="none" />
                        )
                      }}
                    />
                    {refAreaLeft && refAreaRight ? (
                      <ReferenceArea x1={refAreaLeft} x2={refAreaRight} strokeOpacity={0.3} fill="hsl(var(--muted-foreground))" fillOpacity={0.1} />
                    ) : null}
                  </ComposedChart>
                </ResponsiveContainer>
              </div>
            </CardContent>
          </Card>
        </div>

        {/* Right Column: Deck Performance */}

        {/* Deck Performance List */}
        <Card className="border-sidebar-border/60">
          <CardHeader className="flex flex-row items-center justify-between px-4 py-3">
            <CardTitle className="text-base font-medium">Deck Performance</CardTitle>
          </CardHeader>
          <CardContent className="p-0 overflow-hidden">
            {/* Scatter Plot */}
            <div className="relative h-[175px] w-full outline-none mt-2">
              <ResponsiveContainer width="100%" height="100%">
                <ScatterChart margin={{ top: 2, right: 15, bottom: 10, left: 8 }}>
                  <CartesianGrid strokeDasharray="3 3" stroke="hsl(var(--border))" strokeOpacity={0.3} fill="hsl(var(--muted))" fillOpacity={0.2} />
                  <XAxis
                    type="number"
                    dataKey="winrate"
                    name="Winrate"
                    domain={[50, 80]}
                    tick={{ fontSize: 10, fill: 'hsl(var(--muted-foreground))' }}
                    tickLine={false}
                    axisLine={false}
                    height={20}
                  />
                  <YAxis
                    type="number"
                    dataKey="matches"
                    name="Matches"
                    tick={{ fontSize: 10, fill: 'hsl(var(--muted-foreground))' }}
                    tickLine={false}
                    axisLine={false}
                    width={30}
                  />
                  <Customized component={<DensityLayer data={stats.deckPerformance} />} />
                  <Tooltip
                    cursor={{ strokeDasharray: '3 3' }}
                    content={({ active, payload }) => {
                      if (active && payload && payload.length) {
                        const data = payload[0].payload
                        return (
                          <div className="rounded-lg border bg-background p-2 shadow-sm">
                            <div className="text-xs font-medium">{data.name}</div>
                            <div className="mt-1 flex gap-3 text-xs text-muted-foreground">
                              <span>{data.matches} matches</span>
                              <span className={data.winrate >= 50 ? 'text-emerald-500' : 'text-rose-500'}>
                                {data.winrate}% WR
                              </span>
                            </div>
                          </div>
                        )
                      }
                      return null
                    }}
                  />
                  <Scatter
                    data={stats.deckPerformance}
                    shape={(props: any) => {
                      const { cx, cy, payload } = props
                      return (
                        <image
                          href={`https://api.scryfall.com/cards/named?fuzzy=${encodeURIComponent(payload.keyCard)}&format=image&version=art_crop`}
                          x={cx - 6.5}
                          y={cy - 6.5}
                          width={15}
                          height={15}
                          style={{ borderRadius: '2px', opacity: 0.9 }}
                          preserveAspectRatio="xMidYMid slice"
                        />
                      )
                    }}
                  />
                </ScatterChart>
              </ResponsiveContainer>
              {/* Axis Labels Overlay */}
              <div className="pointer-events-none absolute inset-0">
                <div className="absolute bottom-8 right-5 text-[10px] font-medium text-muted-foreground">Winrate %</div>
                <div className="absolute left-10 top-0.5 text-[10px] font-medium text-muted-foreground">Matches</div>
              </div>
            </div>

            <div className="flex flex-col">
              {stats.deckPerformance.map((deck, index) => (
                <div key={deck.name} className={`flex items-center justify-between border-b border-sidebar-border/60 py-3 px-4 hover:bg-muted/50 ${index === 0 ? 'border-t' : ''} ${index === stats.deckPerformance.length - 1 ? 'border-b-0' : ''}`}>
                  <div className="flex items-center gap-3">
                    <CardArt
                      cardName={deck.keyCard}
                      className="h-10 w-10 rounded"
                    />
                    <div className="mb-1 flex h-10 flex-col justify-center gap-0.5">
                      <span className="font-medium">{deck.name}</span>
                      <div className="flex items-center gap-0.5">
                        {deck.colors.map((color) => (
                          <img
                            key={color}
                            src={`/mana-symbols/${color}.svg`}
                            alt={color}
                            className="h-3.5 w-3.5"
                          />
                        ))}
                      </div>
                    </div>
                  </div>
                  <div className="mb-1 flex h-10 flex-col items-end justify-center">
                    <span className={`font-bold ${deck.winrate >= 50 ? 'text-emerald-500' : 'text-rose-500'}`}>
                      {deck.winrate}%
                    </span>
                    <span className="text-xs text-muted-foreground">{deck.matches} Matches</span>
                  </div>
                </div>
              ))}
            </div>

            {/* View More Button */}
            <div className="border-t border-sidebar-border/60">
              <Link to="/decks" className="block">
                <Button variant="ghost" size="sm" className="h-6 w-full">
                  View More Decks
                </Button>
              </Link>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
