import { useState, useMemo, useCallback } from "react"
import { useGamesHistory } from "@/hooks/use-games"
import { useGames } from "@/hooks/use-games"
import { useClientState } from "@/hooks/use-client-state"
import { useNDJSONStream } from "@/hooks/use-ndjson-stream"
import { getApiUrl } from "@/utils/api-config"
import { Link } from "react-router-dom"
import { ColumnDef } from "@tanstack/react-table"
import { DataTable } from "@/components/ui/data-table"
import { EventsTableSkeleton } from "@/components/events-table-skeleton"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { ChevronDown } from "lucide-react"
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "@/components/ui/dropdown-menu"
import { DatePickerWithRange } from "@/components/ui/date-range-picker"
import { DateRange } from "react-day-picker"

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
      return <span className={deckName ? "" : "text-muted-foreground italic"}>{deckName || "Unknown"}</span>
    }
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
      const { result, isActive } = row.original
      if (isActive || result === "In Progress") {
        return (
          <Badge variant="secondary" className="capitalize bg-yellow-500/15 text-yellow-700 dark:text-yellow-400 border-yellow-500/30">
            In Progress
          </Badge>
        )
      }
      let variant: "default" | "secondary" | "destructive" | "outline" = "outline"
      if (result === "Win") variant = "default"
      if (result === "Loss") variant = "destructive"
      return (
        <Badge variant={variant} className="capitalize">
          {result}
        </Badge>
      )
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
  {
    id: "actions",
    size: 60,
    cell: ({ row }) => {
      const { id, isActive, isEvent } = row.original
      if (isEvent) return null
      return (
        <Button variant="ghost" size="sm" asChild>
          <Link to={`/history/${id}`}>{isActive ? "Watch" : "View"}</Link>
        </Button>
      )
    }
  }
]

export default function History() {
  const [dateRange, setDateRange] = useState<DateRange | undefined>(() => {
    const to = new Date()
    const from = new Date()
    from.setDate(to.getDate() - 30)
    return { from, to }
  })

  const [selectedFormat, setSelectedFormat] = useState<string>("")
  const [page, setPage] = useState(1)
  const pageSize = 50

  // The format list for the dropdown
  const { formats } = useGames('ALL', '')

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

  const handleDateChange = (range: DateRange | undefined) => {
    setDateRange(range)
    setPage(1)
    setLiveItems([])
  }

  return (
    <div className="container mx-auto py-4 px-4 space-y-4">
      <div className="flex flex-col gap-4 md:flex-row md:items-center md:justify-between">
        <h1 className="text-2xl font-bold tracking-tight">Match History</h1>
        <div className="flex items-center gap-2">
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="outline" size="sm" className="h-8 gap-2 border-dashed border-sidebar-border/60">
                <span className="text-muted-foreground">Format:</span>
                <span className="font-medium">{selectedFormat || "All"}</span>
                <ChevronDown className="h-4 w-4 text-muted-foreground" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem onClick={() => handleFormatSelect("")}>
                All
              </DropdownMenuItem>
              {formats.map((f: string) => (
                <DropdownMenuItem key={f} onClick={() => handleFormatSelect(f)}>
                  {f}
                </DropdownMenuItem>
              ))}
            </DropdownMenuContent>
          </DropdownMenu>

          <DatePickerWithRange
            date={dateRange}
            setDate={handleDateChange}
            size="sm"
            className="justify-start text-left font-normal border-dashed border-sidebar-border/60"
            presets={[
              { label: 'All Time', getValue: () => undefined },
              { label: 'Today', getValue: () => { const today = new Date(); return { from: today, to: today } } },
              { label: 'Last 7 Days', getValue: () => { const today = new Date(); const prev = new Date(); prev.setDate(today.getDate() - 7); return { from: prev, to: today } } },
              { label: 'Last 30 Days', getValue: () => { const today = new Date(); const prev = new Date(); prev.setDate(today.getDate() - 30); return { from: prev, to: today } } },
            ]}
          />
        </div>
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
