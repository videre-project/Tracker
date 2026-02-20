import { useState, useEffect, useMemo } from "react"
import { useDashboardStats } from "@/hooks/use-dashboard-stats"
import { useGames } from "@/hooks/use-games"
import { useAggregatedArchetypes } from "@/hooks/use-decks"
import { Link } from "react-router-dom"

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu"
import { ResponsiveContainer, YAxis, Tooltip, ScatterChart, Scatter, CartesianGrid, XAxis, Customized, ReferenceArea, ComposedChart, Line, Area, ReferenceLine } from "recharts"
import { Button } from "@/components/ui/button"
import { Calendar, ChevronDown, Trophy, Clock, Dices, ExternalLink } from "lucide-react"
import { CardArt, useCardArtContext } from "@/components/card-art"
import { Skeleton } from "@/components/ui/skeleton"
import { cn } from "@/lib/utils"


const NoDataState = ({ 
  icon: Icon, 
  title,
  description 
}: { 
  icon: React.ElementType
  title: string
  description?: string 
}) => {
  return (
    <div className="flex flex-col items-center justify-center py-8 text-center animate-in fade-in-50">
      <div className="flex h-12 w-12 items-center justify-center rounded-full bg-muted/50 mb-3">
        <Icon className="h-6 w-6 text-muted-foreground/50" />
      </div>
      <h3 className="text-sm font-medium text-foreground">{title}</h3>
      {description && (
        <p className="mt-1 text-xs text-muted-foreground max-w-[200px]">
          {description}
        </p>
      )}
    </div>
  )
}

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

function BetaChart({ winrate, matches }: { winrate: number; matches: number }) {
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
      className="absolute -top-[28px] left-0 h-[32px] w-full overflow-visible opacity-30"
      viewBox={`0 0 ${width} ${height}`}
      preserveAspectRatio="none"
    >
      <defs>
        {/* Horizontal gradient: red on left (0-50%), green on right (50-100%) */}
        <linearGradient id="betaSplitGradient" x1="0%" y1="0%" x2="100%" y2="0%">
          <stop offset="0%" stopColor="#f43f5e" />
          <stop offset="50%" stopColor="#f43f5e" />
          <stop offset="50%" stopColor="#10b981" />
          <stop offset="100%" stopColor="#10b981" />
        </linearGradient>
      </defs>
      <path d={`M 0,${height} L ${pathData} L ${width},${height} Z`} fill="url(#betaSplitGradient)" />
      <path d={`M 0,${height} L ${pathData} L ${width},${height}`} stroke="url(#betaSplitGradient)" strokeWidth="0.5" fill="none" vectorEffect="non-scaling-stroke" />
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


import { DatePickerWithRange } from "@/components/ui/date-range-picker"
import { DateRange } from "react-day-picker"

export default function Home() {
  const { stats: mockStats } = useDashboardStats()
  const [dateRange, setDateRange] = useState<DateRange | undefined>(() => {
    const to = new Date()
    const from = new Date()
    from.setDate(to.getDate() - 30)
    return { from, to }
  })

  // If dateRange is set, use it. Otherwise default to ALL since presets are removed.
  // We need to pass the effective range to useGames
  const effectiveRange = dateRange || 'ALL'

  const [chartTimeRange, setChartTimeRange] = useState('14D')
  const [selectedFormat, setSelectedFormat] = useState<string>("")
  const [gameType, setGameType] = useState<'All' | 'Constructed' | 'Limited'>('Constructed')

  const { formats, stats, trend: trendData, loading } = useGames(effectiveRange, selectedFormat)

  const filteredFormats = useMemo(() => {
    // Filter first
    let result = formats
    const isLimited = (f: string) => /draft|sealed|limited/i.test(f)
    
    if (gameType === 'Limited') {
      result = formats.filter(isLimited)
    } else if (gameType === 'Constructed') {
      result = formats.filter(f => !isLimited(f))
    }

    // Then sort
    const constructedOrder = [
      "Standard",
      "Modern",
      "Pioneer",
      "Vintage",
      "Legacy",
      "Pauper",
      "Premodern"
    ]

    return [...result].sort((a, b) => {
      const indexA = constructedOrder.indexOf(a)
      const indexB = constructedOrder.indexOf(b)

      if (indexA !== -1 && indexB !== -1) return indexA - indexB
      if (indexA !== -1) return -1
      if (indexB !== -1) return 1
      
      return a.localeCompare(b)
    })
  }, [formats, gameType])

  // Clear selected format if it's no longer in the filtered list
  useEffect(() => {
    if (selectedFormat && !filteredFormats.includes(selectedFormat)) {
      setSelectedFormat("")
    }
  }, [gameType, filteredFormats, selectedFormat])
  const { archetypes, loading: archetypesLoading } = useAggregatedArchetypes(effectiveRange, selectedFormat || undefined)
  const { getArtUrl, prefetchCards, isReady: clientReady } = useCardArtContext()



  // Handler for date picker
  const handleDateRangeChange = (range: DateRange | undefined) => {
    setDateRange(range)
    if (range) {
       // Ideally we clear preset highlight, but we can keep it as is or set to ''
       // setTimeRangePreset('') 
    }
  }

  // Prefetch card art for scatterplot when archetypes load and client is ready
  useEffect(() => {
    console.log('[CardArt Prefetch] Check:', { archetypesLoading, archetypesCount: archetypes.length, clientReady })
    if (archetypesLoading || archetypes.length === 0 || !clientReady) return

    const topCards = archetypes.slice(0, 10).map(a => a.topCard).filter(Boolean)
    console.log('[CardArt Prefetch] Prefetching cards:', topCards)
    prefetchCards(topCards)
  }, [clientReady, archetypesLoading, archetypes, prefetchCards])

  // Zoom state
  const [left, setLeft] = useState<string | number>('dataMin')
  const [right, setRight] = useState<string | number>('dataMax')
  const [refAreaLeft, setRefAreaLeft] = useState<string | number | null>(null)
  const [refAreaRight, setRefAreaRight] = useState<string | number | null>(null)

  const handleChartRangeChange = (range: string) => {
    setChartTimeRange(range)
    setLeft('dataMin')
    setRight('dataMax')
  }

  // Set initial zoom to 14D on mount (handled by useGames default, but we can ensure zoom reset)
  useEffect(() => {
    if (trendData.length > 0) {
      setLeft('dataMin')
      setRight('dataMax')
    }
  }, [trendData])

  // Auto-scale chart range if no data in current view
  useEffect(() => {
    // Helper to check data availability for a specific range
    const checkDataAvailability = (range: string) => {
      if (!trendData || trendData.length === 0) return false
      if (range === 'ALL') {
         // efficient check for ALL
         return trendData.some((d: any) => d.matches > 0)
      }

      const days = parseInt(range.replace('D', ''))
      if (isNaN(days)) return false

      const anchorDate = dateRange?.to || dateRange?.from || new Date()
      const cutoff = new Date(anchorDate)
      cutoff.setDate(cutoff.getDate() - days)
      cutoff.setHours(0, 0, 0, 0)
      
      return trendData.some((d: any) => {
         const date = new Date(d.rawDate)
         return date >= cutoff && date <= anchorDate && d.matches > 0
      })
    }

    if (trendData.length > 0) {
      // If current range has data, keep it
      if (checkDataAvailability(chartTimeRange)) return

      // Fallback logic
      const ranges = ['7D', '14D', '30D', 'ALL']
      const currentIndex = ranges.indexOf(chartTimeRange)
      
      // Try next ranges
      for (let i = currentIndex + 1; i < ranges.length; i++) {
        const nextRange = ranges[i]
        if (checkDataAvailability(nextRange)) {
            setChartTimeRange(nextRange)
            return
        }
      }
      
      // Fallback to ALL if nothing else fits (and we aren't already there)
      if (chartTimeRange !== 'ALL') {
          setChartTimeRange('ALL')
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [trendData, dateRange])

  const zoomedData = useMemo(() => {
    // First filter by chartTimeRange
    let filteredData = trendData
    if (chartTimeRange !== 'ALL') {
      const days = parseInt(chartTimeRange.replace('D', ''))
      if (!isNaN(days)) {
        // Use the end of the selected range, or Today if not selected
        const anchorDate = dateRange?.to || dateRange?.from || new Date()
        const cutoff = new Date(anchorDate)
        cutoff.setDate(cutoff.getDate() - days)
        // Set cutoff to start of day to include all matches on that day
        cutoff.setHours(0, 0, 0, 0)
        
        filteredData = trendData.filter((d: any) => new Date(d.rawDate) >= cutoff && new Date(d.rawDate) <= anchorDate)
      }
    }

    if (left === 'dataMin' && right === 'dataMax') return filteredData

    const leftIndex = left === 'dataMin' ? 0 : filteredData.findIndex((d: any) => d.date === left)
    const rightIndex = right === 'dataMax' ? filteredData.length - 1 : filteredData.findIndex((d: any) => d.date === right)

    if (leftIndex === -1 || rightIndex === -1) return filteredData

    return filteredData.slice(leftIndex, rightIndex + 1)
  }, [trendData, chartTimeRange, left, right])

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
    // We don't change timeRange here, just local zoom
  }

  const zoomOut = () => {
    setLeft('dataMin')
    setRight('dataMax')
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

  const playCI = stats ? getBetaCI(stats.playWinrate, stats.playMatches) : { start: 50, end: 50 }
  const drawCI = stats ? getBetaCI(stats.drawWinrate, stats.drawMatches) : { start: 50, end: 50 }


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
                 {['All', 'Constructed', 'Limited'].map((type) => (
                    <Button
                      key={type}
                      variant={gameType === type ? "secondary" : "ghost"}
                      size="sm"
                      onClick={() => setGameType(type as any)}
                      className={cn(
                        "h-6 rounded-md px-3",
                         gameType === type ? "shadow-sm" : "text-muted-foreground hover:text-foreground"
                      )}
                    >
                      {type}
                    </Button>
                  ))}
              </div>

              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button variant="outline" size="sm" className="h-8 gap-2 border-dashed border-sidebar-border/60">
                    <span className="text-muted-foreground">Format:</span>
                    <span className="font-medium">{selectedFormat || "All"}</span>
                    <ChevronDown className="h-4 w-4 text-muted-foreground" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuItem onClick={() => setSelectedFormat("")}>
                    All
                  </DropdownMenuItem>
                  {filteredFormats.map(f => (
                    <DropdownMenuItem key={f} onClick={() => setSelectedFormat(f)}>
                      {f}
                    </DropdownMenuItem>
                  ))}
                </DropdownMenuContent>
              </DropdownMenu>
            </div>
            
            <DatePickerWithRange 
              date={dateRange} 
              setDate={handleDateRangeChange}
              size="sm"
              className="justify-start text-left font-normal border-dashed border-sidebar-border/60"
              presets={[
                { label: 'All Time', getValue: () => undefined },
                { 
                  label: 'Today', 
                  getValue: () => {
                    const today = new Date()
                    return { from: today, to: today }
                  }
                },
                { 
                  label: 'Yesterday', 
                  getValue: () => {
                    const yesterday = new Date()
                    yesterday.setDate(yesterday.getDate() - 1)
                    return { from: yesterday, to: yesterday }
                  }
                },
                { 
                  label: 'Last 7 Days', 
                  getValue: () => {
                    const today = new Date()
                    const prev = new Date()
                    prev.setDate(today.getDate() - 7)
                    return { from: prev, to: today }
                  }
                },
                { 
                  label: 'Last 30 Days', 
                  getValue: () => {
                    const today = new Date()
                    const prev = new Date()
                    prev.setDate(today.getDate() - 30)
                    return { from: prev, to: today }
                  }
                },
                { 
                  label: 'Last 90 Days', 
                  getValue: () => {
                    const today = new Date()
                    const prev = new Date()
                    prev.setDate(today.getDate() - 90)
                    return { from: prev, to: today }
                  }
                },
              ]}
            />
          </div>
          {/* Stat Cards */}
          <div className="grid gap-4 md:grid-cols-3">
            <Card className="border-sidebar-border/60">
              <CardHeader className="flex flex-row items-center justify-between space-y-0 p-4 pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">Overall Winrate</CardTitle>
                <Trophy className="h-4 w-4 text-muted-foreground" />
              </CardHeader>
              <CardContent className="p-4 pt-0">
                {!stats || loading ? (
                  <>
                    <Skeleton className="h-9 w-[60px] mb-4" />
                    <Skeleton className="h-2 w-full rounded-full mb-2" />
                    <div className="flex justify-between">
                      <Skeleton className="h-4 w-[50px]" />
                      <Skeleton className="h-4 w-[50px]" />
                    </div>
                  </>
                ) : (
                  <>
                    <div className="text-3xl font-bold">{stats.totalMatches > 0 ? `${stats.overallWinrate}%` : "N/A"}</div>
                    <div className="mt-4 flex flex-col gap-2">
                      <div className="relative">
                        <div className="absolute bottom-full right-0 mb-2 text-xs font-medium text-muted-foreground">
                          {stats.ties} Ties
                        </div>
                        <div className="relative flex h-2 w-full overflow-hidden rounded-full transition-all">
                          {stats.totalMatches > 0 ? (
                            <>
                              <div className="h-full bg-emerald-500" style={{ width: `${stats.overallWinrate}%` }} />
                              <div className="h-full flex-1 bg-rose-500" />
                              <div className="h-full bg-muted-foreground/30" style={{ width: `${(stats.ties / stats.totalMatches) * 100}%` }} />
                              <div className="absolute left-1/2 top-0 h-full w-[2px] -translate-x-1/2 bg-background" />
                            </>
                          ) : (
                            <div className="h-full w-full bg-secondary" />
                          )}
                        </div>
                      </div>
                      <div className="flex justify-between text-xs">
                        {stats.totalMatches > 0 ? (
                          <>
                            <span className="font-medium text-emerald-500">{stats.wins} Wins</span>
                            <span className="font-medium text-rose-500">{stats.losses} Losses</span>
                          </>
                        ) : (
                          <span className="text-muted-foreground">No matches recorded</span>
                        )}
                      </div>
                    </div>
                  </>
                )}
              </CardContent>
            </Card>

            <Card className="border-sidebar-border/60">
              <CardHeader className="flex flex-row items-center justify-between space-y-0 p-4 pb-2">
                <CardTitle className="text-sm font-medium text-muted-foreground">Play / Draw Winrate</CardTitle>
                <Dices className="h-4 w-4 text-muted-foreground" />
              </CardHeader>
              <CardContent className="p-4 pt-0">
                {!stats || loading ? (
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
                ) : (
                  <div className="flex flex-col gap-4 pt-2">
                    {/* Play Slider */}
                    <div className="space-y-2">
                      <div className="relative z-10 flex items-center justify-between text-xs">
                        <span className="text-muted-foreground">On the Play</span>
                        <span className={`font-bold ${stats.playMatches > 0 ? (stats.playWinrate >= 50 ? "text-emerald-500" : "text-rose-500") : "text-muted-foreground"}`}>
                          {stats.playMatches > 0 ? `${stats.playWinrate}%` : "N/A"}
                        </span>
                      </div>
                      <div
                        className="relative h-2 w-full"
                        onMouseMove={handleMouseMove}
                        onMouseLeave={handleMouseLeave}
                      >
                        <div className="absolute top-1/2 h-1 w-full -translate-y-1/2 rounded-full bg-secondary" />

                        {stats.playMatches > 0 && (
                          <>
                            <BetaChart
                              winrate={stats.playWinrate}
                              matches={stats.playMatches}
                            />
                            {/* CI bar with split gradient */}
                            <div
                              className="absolute top-1/2 h-1 -translate-y-1/2 rounded-full overflow-hidden"
                              style={{ left: `${playCI.start}%`, width: `${playCI.end - playCI.start}%` }}
                            >
                              <div
                                className="h-full w-full"
                                style={{
                                  background: `linear-gradient(to right, #f43f5e ${Math.max(0, (50 - playCI.start) / (playCI.end - playCI.start) * 100)}%, #10b981 ${Math.max(0, (50 - playCI.start) / (playCI.end - playCI.start) * 100)}%)`
                                }}
                              />
                            </div>
                            {/* CI Whiskers - colored based on position */}
                            <div
                              className={`absolute top-1/2 h-2 w-px -translate-y-1/2 ${playCI.start >= 50 ? "bg-emerald-500" : "bg-rose-500"}`}
                              style={{ left: `${playCI.start}%` }}
                            />
                            <div
                              className={`absolute top-1/2 h-2 w-px -translate-y-1/2 ${playCI.end >= 50 ? "bg-emerald-500" : "bg-rose-500"}`}
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
                          </>
                        )}
                      </div>
                    </div>

                    {/* Draw Slider */}
                    <div className="space-y-2">
                      <div className="relative z-10 flex items-center justify-between text-xs">
                        <span className="text-muted-foreground">On the Draw</span>
                        <span className={`font-bold ${stats.drawMatches > 0 ? (stats.drawWinrate >= 50 ? "text-emerald-500" : "text-rose-500") : "text-muted-foreground"}`}>
                          {stats.drawMatches > 0 ? `${stats.drawWinrate}%` : "N/A"}
                        </span>
                      </div>
                      <div
                        className="relative h-2 w-full"
                        onMouseMove={handleMouseMove}
                        onMouseLeave={handleMouseLeave}
                      >
                        <div className="absolute top-1/2 h-1 w-full -translate-y-1/2 rounded-full bg-secondary" />

                        {stats.drawMatches > 0 && (
                          <>
                            <BetaChart
                              winrate={stats.drawWinrate}
                              matches={stats.drawMatches}
                            />
                            {/* CI bar with split gradient */}
                            <div
                              className="absolute top-1/2 h-1 -translate-y-1/2 rounded-full overflow-hidden"
                              style={{ left: `${drawCI.start}%`, width: `${drawCI.end - drawCI.start}%` }}
                            >
                              <div
                                className="h-full w-full"
                                style={{
                                  background: `linear-gradient(to right, #f43f5e ${Math.max(0, (50 - drawCI.start) / (drawCI.end - drawCI.start) * 100)}%, #10b981 ${Math.max(0, (50 - drawCI.start) / (drawCI.end - drawCI.start) * 100)}%)`
                                }}
                              />
                            </div>
                            {/* CI Whiskers - colored based on position */}
                            <div
                              className={`absolute top-1/2 h-2 w-px -translate-y-1/2 ${drawCI.start >= 50 ? "bg-emerald-500" : "bg-rose-500"}`}
                              style={{ left: `${drawCI.start}%` }}
                            />
                            <div
                              className={`absolute top-1/2 h-2 w-px -translate-y-1/2 ${drawCI.end >= 50 ? "bg-emerald-500" : "bg-rose-500"}`}
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
                          </>
                        )}
                      </div>
                    </div>
                  </div>
                )}
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
                {!stats || loading ? (
                  <>
                    <div className="mt-2 flex justify-end">
                      <Skeleton className="h-9 w-[80px]" />
                    </div>
                    <div className="mt-2 flex flex-col gap-2">
                      <Skeleton className="h-5 w-full" />
                      <Skeleton className="h-5 w-full" />
                    </div>
                  </>
                ) : (
                  <>
                    <div className="mt-2 text-right text-[1.6rem] font-bold">
                      {stats.totalMatches > 0 ? stats.averageDuration : "N/A"}
                    </div>
                    <div className="mt-2 flex flex-col gap-2">
                      <div className="relative h-5 w-full overflow-hidden rounded-md bg-secondary/50">
                        {stats.totalMatches > 0 && (
                          <div
                            className="h-full bg-muted-foreground/20"
                            style={{ width: `${getDurationPercentage(stats.durationTwoGames)}%` }}
                          />
                        )}
                        <div className="absolute inset-0 grid grid-cols-2">
                          <div className="border-r-[3px] border-background/50" />
                          <div />
                        </div>
                        <div className="absolute inset-0 flex items-center justify-between px-2 text-xs font-medium">
                          <span className="text-muted-foreground">2 Games</span>
                          <span>{stats.totalMatches > 0 ? stats.durationTwoGames : "-"}</span>
                        </div>
                      </div>
                      <div className="relative h-5 w-full overflow-hidden rounded-md bg-secondary/50">
                        {stats.totalMatches > 0 && (
                          <div
                            className="h-full bg-muted-foreground/20"
                            style={{ width: `${getDurationPercentage(stats.durationThreeGames)}%` }}
                          />
                        )}
                        <div className="absolute inset-0 grid grid-cols-3">
                          <div className="border-r-[3px] border-background/50" />
                          <div className="border-r-[3px] border-background/50" />
                          <div />
                        </div>
                        <div className="absolute inset-0 flex items-center justify-between px-2 text-xs font-medium">
                          <span className="text-muted-foreground">3 Games</span>
                          <span>{stats.totalMatches > 0 ? stats.durationThreeGames : "-"}</span>
                        </div>
                      </div>
                    </div>
                  </>
                )}
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
                      variant={chartTimeRange === range ? 'secondary' : 'ghost'}
                      size="sm"
                      onClick={() => handleChartRangeChange(range)}
                      className={`h-6 px-2 text-xs ${chartTimeRange === range ? 'shadow-sm' : 'text-muted-foreground hover:text-foreground'}`}
                    >
                      {range}
                    </Button>
                  ))}
                </div>
              </div>
            </CardHeader>
            <CardContent className="p-4 pt-0 min-h-[310px] flex flex-col justify-center">
              {loading ? (
                <Skeleton className="h-[310px] w-full" />
              ) : zoomedData.length === 0 ? (
                <NoDataState 
                  icon={Clock} 
                  title="No Performance Trend"
                  description="Play matches over time to see your winrate trends."
                />
              ) : (
              <div className="h-[310px] w-full select-none outline-none overflow-visible">
                <ResponsiveContainer width="100%" height="100%">
                  <ComposedChart
                    data={zoomedData}
                    margin={{ top: 10, right: 10, bottom: 0, left: -26 }}
                    onMouseDown={(e: any) => e && setRefAreaLeft(e.activeLabel)}
                    onMouseMove={(e: any) => refAreaLeft && e && setRefAreaRight(e.activeLabel)}
                    onMouseUp={zoom}
                  >
                    {/* Gradients using userSpaceOnUse with calculated Y coordinates */}
                    {/* Chart: 310px height, 10px top margin, ~27px bottom for X-axis labels */}
                    {/* Y=100% at y=10, Y=0% at y=283, Y=50% at y=146.5 */}
                    <defs>
                      <linearGradient id="splitColorWinrate" x1="0" y1="10" x2="0" y2="283" gradientUnits="userSpaceOnUse">
                        <stop offset="0%" stopColor="#10b981" />
                        <stop offset="50%" stopColor="#10b981" />
                        <stop offset="50%" stopColor="#f43f5e" />
                        <stop offset="100%" stopColor="#f43f5e" />
                      </linearGradient>
                      <linearGradient id="splitColor95" x1="0" y1="10" x2="0" y2="283" gradientUnits="userSpaceOnUse">
                        <stop offset="0%" stopColor="#10b981" />
                        <stop offset="50%" stopColor="#10b981" />
                        <stop offset="50%" stopColor="#f43f5e" />
                        <stop offset="100%" stopColor="#f43f5e" />
                      </linearGradient>
                      <linearGradient id="splitColor80" x1="0" y1="10" x2="0" y2="283" gradientUnits="userSpaceOnUse">
                        <stop offset="0%" stopColor="#10b981" />
                        <stop offset="50%" stopColor="#10b981" />
                        <stop offset="50%" stopColor="#f43f5e" />
                        <stop offset="100%" stopColor="#f43f5e" />
                      </linearGradient>
                      <linearGradient id="splitColor50" x1="0" y1="10" x2="0" y2="283" gradientUnits="userSpaceOnUse">
                        <stop offset="0%" stopColor="#10b981" />
                        <stop offset="50%" stopColor="#10b981" />
                        <stop offset="50%" stopColor="#f43f5e" />
                        <stop offset="100%" stopColor="#f43f5e" />
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
                          const winrateValue = payload.find(p => p.dataKey === 'winrate')?.value
                          const ci95Value = payload.find(p => p.dataKey === 'ci95')?.value as number[] | null
                          const matchCount = payload[0].payload.matches

                          // Don't show tooltip for days with no data
                          if (winrateValue === null || matchCount === 0) {
                            return (
                              <div className="rounded-lg border bg-background p-2 shadow-sm">
                                <div className="text-xs font-medium">{label}</div>
                                <div className="mt-1 text-xs text-muted-foreground">No matches</div>
                              </div>
                            )
                          }

                          return (
                            <div className="rounded-lg border bg-background p-2 shadow-sm">
                              <div className="text-xs font-medium">{label}</div>
                              <div className="mt-1 flex flex-col gap-1">
                                <span className="text-xs font-medium text-primary">
                                  Winrate: {winrateValue?.toString().slice(0, 4)}%
                                  <span className="ml-2 text-muted-foreground font-normal">
                                    ({matchCount} matches)
                                  </span>
                                </span>
                                {ci95Value && (
                                  <span className="text-[10px] text-muted-foreground">
                                    CI: {Math.round(ci95Value[0])}% - {Math.round(ci95Value[1])}%
                                  </span>
                                )}
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
                      connectNulls={true}
                    />
                    <Area
                      type="monotone"
                      dataKey="ci95"
                      stroke="none"
                      fill="url(#splitColor95)"
                      fillOpacity={0.05}
                      activeDot={false}
                      animationDuration={150}
                      connectNulls={true}
                    />
                    <Area
                      type="monotone"
                      dataKey="ci80"
                      stroke="none"
                      fill="url(#splitColor80)"
                      fillOpacity={0.1}
                      activeDot={false}
                      animationDuration={150}
                      connectNulls={true}
                    />
                    <Area
                      type="monotone"
                      dataKey="ci50"
                      stroke="none"
                      fill="url(#splitColor50)"
                      fillOpacity={0.15}
                      activeDot={false}
                      animationDuration={150}
                      connectNulls={true}
                    />
                    <Line
                      type="monotone"
                      dataKey="winrate"
                      stroke="url(#splitColorWinrate)"
                      strokeWidth={2}
                      animationDuration={150}
                      connectNulls={true}
                      dot={(props: any) => {
                        const { cx, cy, payload } = props
                        if (payload.winrate == null || payload.matches === 0) return <g />
                        const color = payload.winrate >= 50 ? "#10b981" : "#f43f5e"
                        return (
                          <circle cx={cx} cy={cy} r={4} fill="hsl(var(--background))" stroke={color} strokeWidth={2} />
                        )
                      }}
                      activeDot={(props: any) => {
                        const { cx, cy, payload } = props
                        if (payload.winrate == null || payload.matches === 0) return <g />
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
            )}
            </CardContent>
          </Card>
        </div>

        {/* Right Column: Deck Performance */}

        {/* Deck Performance List */}
        <Card className="border-sidebar-border/60 h-full flex flex-col">
          <CardHeader className="flex flex-row items-center justify-between px-4 py-3">
            <CardTitle className="text-base font-medium">Deck Performance</CardTitle>
          </CardHeader>
          <CardContent className="p-0 overflow-hidden flex-1 flex flex-col">
            {/* Scatter Plot */}
            {(archetypesLoading || archetypes.length > 0) && (
              <div className="relative h-[175px] w-full outline-none mt-2">
                {archetypesLoading ? (
                  <div className="p-4">
                    <Skeleton className="h-[175px] w-full mb-4" />
                  </div>
                ) : (
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
                      <Customized component={<DensityLayer data={archetypes.slice(0, 10).map(a => ({ name: a.archetype, winrate: a.winrate, matches: a.matches, keyCard: a.topCard }))} />} />
                      <Tooltip
                        cursor={{ strokeDasharray: '3 3' }}
                        content={({ active, payload }) => {
                          if (active && payload && payload.length) {
                            const data = payload[0].payload
                            return (
                              <div className="rounded-lg border bg-background p-2 shadow-sm">
                                <div className="text-xs font-medium">{data.archetype || data.name}</div>
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
                        data={archetypes.slice(0, 10).map(a => ({ name: a.archetype, winrate: a.winrate, matches: a.matches, topCard: a.topCard }))}
                        shape={(props: any) => {
                          const { cx, cy, payload } = props
                          const cachedUrl = getArtUrl(payload.topCard)

                          // Show skeleton circle if not cached yet
                          if (!cachedUrl) {
                            return (
                              <circle
                                cx={cx}
                                cy={cy}
                                r={7}
                                fill="hsl(var(--muted))"
                                opacity={0.5}
                              >
                                <animate
                                  attributeName="opacity"
                                  values="0.3;0.7;0.3"
                                  dur="1.5s"
                                  repeatCount="indefinite"
                                />
                              </circle>
                            )
                          }

                          return (
                            <image
                              href={cachedUrl}
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
                )}
                {/* Axis Labels Overlay */}
                <div className="pointer-events-none absolute inset-0">
                  <div className="absolute bottom-8 right-5 text-[10px] font-medium text-muted-foreground">Winrate %</div>
                  <div className="absolute left-10 top-0.5 text-[10px] font-medium text-muted-foreground">Matches</div>
                </div>
              </div>
            )}

            <div className="flex flex-col flex-1">
              {archetypesLoading ? (
                // Loading skeleton
                [1, 2, 3, 4, 5].map(i => (
                  <div key={i} className="flex items-center gap-3 border-b border-sidebar-border/40 p-3">
                    <Skeleton className="h-10 w-10 rounded" />
                    <div className="flex-1 space-y-1">
                      <Skeleton className="h-4 w-[120px]" />
                      <Skeleton className="h-3 w-[80px]" />
                    </div>
                    <Skeleton className="h-4 w-[40px]" />
                  </div>
                ))
              ) : archetypes.length === 0 ? (

                <div className="flex-1 flex flex-col items-center justify-center min-h-[200px]">
                  <NoDataState 
                    icon={Trophy} 
                    title="No deck data"
                    description="Play some matches to see deck performance statistics."
                  />
                </div>
              ) : (
                archetypes.slice(0, 5).map((arch, index) => (
                  <div key={arch.archetype} className={`flex items-center justify-between border-b border-sidebar-border/60 py-3 px-4 hover:bg-muted/50 ${index === 0 ? 'border-t' : ''} ${index === Math.min(archetypes.length, 5) - 1 ? 'border-b-0' : ''}`}>
                    <div className="flex items-center gap-3">
                      <CardArt
                        cardName={arch.topCard}
                        className="h-10 w-10 rounded"
                      />
                      <div className="mb-1 flex h-10 flex-col justify-center gap-0.5">
                        <span className="font-medium">{arch.archetype}</span>
                        <div className="flex items-center gap-0.5">
                          {arch.colors && arch.colors.length > 0 ? (
                            arch.colors.map((color) => (
                              <img
                                key={color}
                                src={`/mana-symbols/${color}.svg`}
                                alt={color}
                                className="h-3.5 w-3.5"
                              />
                            ))
                          ) : (
                            <span className="text-xs text-muted-foreground">Colorless</span>
                          )}
                        </div>
                      </div>
                    </div>
                    <div className="mb-1 flex h-10 flex-col items-end justify-center">
                      <span className={`font-bold ${arch.winrate >= 50 ? 'text-emerald-500' : 'text-rose-500'}`}>
                        {arch.winrate}%
                      </span>
                      <span className="text-xs text-muted-foreground">{arch.matches} Matches</span>
                    </div>
                  </div>
                ))
              )}
            </div>


            {/* View More Button */}
            {(archetypesLoading || archetypes.length > 0) && (
              <div className="border-t border-sidebar-border/60 mt-auto">
                <Link to="/decks" className="block">
                  <Button variant="ghost" size="sm" className="h-6 w-full">
                    View More Decks
                  </Button>
                </Link>
              </div>
            )}
          </CardContent>
        </Card>
      </div>

    </div>
  )
}
