import { useState, useMemo, useCallback } from "react"
import { useGamesHistory } from "@/hooks/use-games"
import { useGames } from "@/hooks/use-games"
import { useClientState } from "@/hooks/use-client-state"
import { useNDJSONStream } from "@/hooks/use-ndjson-stream"
import { getApiUrl } from "@/utils/api-config"
import { useNavigate } from "react-router-dom"
import { ColumnDef } from "@tanstack/react-table"
import { DataTable } from "@/components/ui/data-table"
import { EventsTableSkeleton } from "@/components/events-table-skeleton"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { DatePickerWithRange } from "@/components/ui/date-range-picker"
import {
  GameTypeFormatFilter,
  type GameType,
} from "@/components/game-type-format-filter"
import { DateRange } from "react-day-picker"
import { cn } from "@/lib/utils"
import { getManaSymbolSvgPath } from "@/utils/mana-symbols"
import { getDisplayCardColors } from "@/utils/card-colors"
import { compareFormats, isLimitedFormat } from "@/utils/formats"

export interface MatchHistoryDTO {
  id: number
  eventId: number
  eventName: string
  format: string
  startTime: string
  result: string
  record: string
  duration: string
  deckName?: string
  deckColors?: string[] | null
  opponentName?: string | null
  opponentDeckName?: string | null
  opponentDeckArchetype?: string | null
  opponentDeckColors?: string[] | null
  isActive?: boolean
  isEvent?: boolean
  matches?: MatchHistoryDTO[]
}

function formatDate(dateString?: string) {
  if (!dateString) return "-"
  const date = new Date(dateString)
  return date.toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function DeckManaSymbols({ colors }: { colors?: string[] | null }) {
  if (!colors) return null

  const visibleColors = getDisplayCardColors(colors)

  return (
    <span className="inline-flex h-4 items-center gap-0.5 translate-y-px leading-none">
      {visibleColors.map((color, index) => (
        <img
          key={`${color}-${index}`}
          src={getManaSymbolSvgPath(color) ?? undefined}
          alt={color}
          className="block h-3.5 w-3.5 rounded-full bg-background shadow-sm ring-1 ring-background"
        />
      ))}
    </span>
  )
}

function MatchResultPill({ result, isActive }: { result: string; isActive?: boolean }) {
  const inProgress = isActive || result === "In Progress"
  const variant = inProgress ? "secondary" : result === "Win" ? "default" : result === "Loss" ? "destructive" : "secondary"

  return (
    <Badge
      variant={variant}
      className={cn(
        "rounded-md capitalize",
        inProgress && "border-yellow-500/30 bg-yellow-500/15 text-yellow-700 dark:text-yellow-400"
      )}
    >
      {inProgress ? "In Progress" : result}
    </Badge>
  )
}

function OpponentSummary({ match }: { match: MatchHistoryDTO }) {
  if (match.isEvent) {
    return <span className="text-muted-foreground">-</span>
  }

  const opponentName = match.opponentName?.trim()
  const opponentDeckLabel = match.opponentDeckArchetype?.trim()
    || match.opponentDeckName?.trim()
    || "Deck unknown"

  return (
    <div className="min-w-0">
      <div className="truncate text-sm font-medium">
        {opponentName ? `vs ${opponentName}` : "Opponent unknown"}
      </div>
      <div className="mt-0.5 inline-flex max-w-full items-center gap-1.5 text-xs text-muted-foreground">
        <span className="truncate">{opponentDeckLabel}</span>
        <DeckManaSymbols colors={match.opponentDeckColors} />
      </div>
    </div>
  )
}

const columns: ColumnDef<MatchHistoryDTO>[] = [
  {
    accessorKey: "eventName",
    header: "Event",
  },
  {
    accessorKey: "format",
    header: "Format",
    size: 120,
  },
  {
    accessorKey: "deckName",
    header: "Deck",
    cell: ({ row }) => {
      const deckName = row.original.deckName
      if (!deckName) {
        return <span className="text-muted-foreground italic">Unknown</span>
      }

      return (
        <span className="inline-flex min-w-0 items-center gap-1.5">
          <span className="truncate">{deckName}</span>
          <DeckManaSymbols colors={row.original.deckColors} />
        </span>
      )
    }
  },
  {
    id: "opponent",
    header: "Opponent",
    size: 190,
    cell: ({ row }) => <OpponentSummary match={row.original} />,
  },
  {
    accessorKey: "startTime",
    header: "Date",
    size: 140,
    cell: ({ row }) => formatDate(row.original.startTime),
  },
  {
    accessorKey: "result",
    header: "Result",
    size: 80,
    cell: ({ row }) => {
      return <MatchResultPill result={row.original.result} isActive={row.original.isActive} />
    }
  },
  {
    accessorKey: "record",
    header: "Record",
    size: 80,
  },
  {
    accessorKey: "duration",
    header: "Duration",
    size: 70,
  },
]

function getHistoryRowHref(match: MatchHistoryDTO): string | null {
  if (match.isEvent) return `/events/${match.eventId}`
  return `/history/${match.id}`
}

export default function History() {
  const navigate = useNavigate()
  const [dateRange, setDateRange] = useState<DateRange | undefined>(() => {
    const to = new Date()
    const from = new Date()
    from.setDate(to.getDate() - 30)
    return { from, to }
  })

  const [selectedFormat, setSelectedFormat] = useState<string>("")
  const [gameType, setGameType] = useState<GameType>("All")
  const [page, setPage] = useState(1)
  const pageSize = 50

  // The format list is shared with the dashboard and deck filters.
  const { formats } = useGames('ALL', '')

  const filteredFormats = useMemo(() => {
    let result = formats
    if (gameType === "Limited") {
      result = formats.filter(isLimitedFormat)
    } else if (gameType === "Constructed") {
      result = formats.filter(format => !isLimitedFormat(format))
    }

    return [...result].sort(compareFormats)
  }, [formats, gameType])

  const effectiveRange = dateRange || 'ALL'
  const { data, loading, error } = useGamesHistory(page, pageSize, effectiveRange, selectedFormat)
  const [liveItems, setLiveItems] = useState<MatchHistoryDTO[]>([])
  const { isReady: clientReady } = useClientState()

  // Subscribe to event/match creation/completion SSE stream
  const onSSEMessage = useCallback((dto: MatchHistoryDTO) => {
    setLiveItems(prev => {
      // For event-level updates (isEvent), match by eventId
      if (dto.isEvent) {
        const idx = prev.findIndex(m => m.isEvent && m.eventId === dto.eventId)
        if (idx !== -1) {
          const updated = [...prev]
          updated[idx] = dto
          return updated
        }
        return [dto, ...prev]
      }

      // For match updates, try to nest under an existing event parent
      const parentIdx = prev.findIndex(m => m.isEvent && m.eventId === dto.eventId)
      if (parentIdx !== -1) {
        const updated = [...prev]
        const parent = { ...updated[parentIdx] }
        const children = [...(parent.matches || [])]
        const childIdx = children.findIndex(c => c.id === dto.id)
        if (childIdx !== -1) {
          children[childIdx] = dto
        } else {
          children.push(dto)
        }
        parent.matches = children
        // Update parent active state
        if (children.some(c => c.isActive)) {
          parent.isActive = true
          parent.result = "In Progress"
        }
        updated[parentIdx] = parent
        return updated
      }

      // No parent event — standalone match, replace or prepend
      const idx = prev.findIndex(m => m.id === dto.id)
      if (idx !== -1) {
        const updated = [...prev]
        updated[idx] = dto
        return updated
      }
      return [dto, ...prev]
    })
  }, [])

  useNDJSONStream<MatchHistoryDTO>({
    url: getApiUrl("/api/games/history/watch"),
    onMessage: onSSEMessage,
    enabled: clientReady,
    autoReconnect: true,
    reconnectDelay: 2000,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })

  // Merge live items into the displayed data (prepend, avoiding duplicates)
  const mergedItems = useMemo(() => {
    const fetched = data?.items || []
    const fetchedEventIds = new Set(fetched.map((m: MatchHistoryDTO) => m.eventId))
    const fetchedMatchIds = new Set(fetched.map((m: MatchHistoryDTO) => m.id))

    // Filter live items: skip those already in fetched data
    const newLive = liveItems.filter(m => {
      if (m.isEvent) return !fetchedEventIds.has(m.eventId)
      return !fetchedMatchIds.has(m.id)
    })
    return [...newLive, ...fetched]
  }, [data, liveItems])

  const handleFormatSelect = (f: string) => {
    setSelectedFormat(f)
    setPage(1)
    setLiveItems([])
  }

  const handleGameTypeSelect = (value: GameType) => {
    setGameType(value)
    if (selectedFormat &&
        value !== "All" &&
        (value === "Limited") !== isLimitedFormat(selectedFormat)) {
      handleFormatSelect("")
    } else {
      setPage(1)
      setLiveItems([])
    }
  }

  const handleDateChange = (range: DateRange | undefined) => {
    setDateRange(range)
    setPage(1)
    setLiveItems([])
  }

  const handleRowClick = useCallback((match: MatchHistoryDTO) => {
    const href = getHistoryRowHref(match)
    if (href) navigate(href)
  }, [navigate])

  return (
    <div className="container mx-auto space-y-4 px-4 pb-4 pt-1">
      <div className="flex flex-wrap items-center justify-start gap-2">
        <div className="flex flex-wrap items-center gap-2">
          <GameTypeFormatFilter
            gameType={gameType}
            onGameTypeChange={handleGameTypeSelect}
            selectedFormat={selectedFormat}
            formats={filteredFormats}
            onFormatChange={handleFormatSelect}
          />
        </div>

        <DatePickerWithRange
          date={dateRange}
          setDate={handleDateChange}
          size="sm"
          className="ml-auto justify-start text-left font-normal border-dashed border-sidebar-border/60"
            presets={[
              { label: 'All Time', getValue: () => undefined },
              { label: 'Today', getValue: () => { const today = new Date(); return { from: today, to: today } } },
              { label: 'Last 7 Days', getValue: () => { const today = new Date(); const prev = new Date(); prev.setDate(today.getDate() - 7); return { from: prev, to: today } } },
              { label: 'Last 30 Days', getValue: () => { const today = new Date(); const prev = new Date(); prev.setDate(today.getDate() - 30); return { from: prev, to: today } } },
            ]}
          />
      </div>

      {error ? (
        <div className="bg-destructive/15 text-destructive px-4 py-3 rounded-md text-sm font-medium">
          Error loading history: {error.message || String(error)}
        </div>
      ) : loading && (!data || data.items.length === 0) ? (
        <div className="rounded-md border border-sidebar-border/60">
          <table className="w-full">
            <thead>
              <tr className="border-b border-sidebar-border/60 bg-muted/50">
                {columns.map((col, i) => (
                  <th key={i} className="h-10 px-4 text-left align-middle font-medium text-muted-foreground text-sm">
                    {typeof col.header === 'string' ? col.header : ''}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              <EventsTableSkeleton rows={15} columns={columns.length} />
            </tbody>
          </table>
        </div>
      ) : (
        <div className="space-y-4">
          <DataTable
            columns={columns}
            data={mergedItems}
            getSubRows={(row) => row.matches}
            onRowClick={handleRowClick}
          />

          {data && data.totalPages > 1 && (
            <div className="flex items-center justify-between px-2">
              <div className="text-sm text-muted-foreground">
                Showing page {data.page} of {data.totalPages} ({data.totalCount} matches)
              </div>
              <div className="flex items-center space-x-2">
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setPage(p => Math.max(1, p - 1))}
                  disabled={data.page <= 1 || loading}
                >
                  Previous
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setPage(p => Math.min(data.totalPages, p + 1))}
                  disabled={data.page >= data.totalPages || loading}
                >
                  Next
                </Button>
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
