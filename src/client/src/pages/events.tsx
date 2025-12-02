"use client"

import { useMemo } from "react"
import { ColumnDef } from "@tanstack/react-table"
import { DataTable } from "@/components/ui/data-table"
import { EventsTableSkeleton } from "@/components/events-table-skeleton"
import { useEvents, ActiveGame } from "@/hooks/use-events"
import { Badge } from "@/components/ui/badge"

function formatDate(dateString?: string) {
  if (!dateString) return "-"
  const date = new Date(dateString)
  return date.toLocaleString(undefined, {
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

const columns: ColumnDef<ActiveGame>[] = [
  {
    accessorKey: "id",
    header: "ID",
  },
  {
    accessorKey: "name",
    header: "Name",
  },
  {
    accessorKey: "format",
    header: "Format",
  },
  {
    accessorKey: "status",
    header: "Status",
    cell: ({ row }) => {
      const status = row.original.status
      return (
        <Badge variant={status === "active" ? "default" : "secondary"} className="capitalize">
          {status}
        </Badge>
      )
    }
  },
  {
    accessorKey: "totalPlayers",
    header: "Players",
    cell: ({ row }) => {
      const total = row.original.totalPlayers ?? 0
      const min = row.original.minimumPlayers ?? 0
      return `${total} / ${min}`
    }
  },
  {
    accessorKey: "totalRounds",
    header: "Rounds",
  },
  {
    accessorKey: "_rawStartTime",
    header: "Start Time",
    cell: ({ row }) => formatDate(row.original._rawStartTime),
  },
  {
    accessorKey: "_rawEndTime",
    header: "End Time",
    cell: ({ row }) => formatDate(row.original._rawEndTime),
  },
]

export default function Events() {
  const { activeGames, upcomingGames, loading, error } = useEvents()

  const events = useMemo(() => {
    // Combine active and upcoming games (already sorted by start time in useEvents)
    return [...activeGames, ...upcomingGames]
  }, [activeGames, upcomingGames])

  return (
    <div className="container mx-auto py-4 px-4 space-y-4">
      {error && (
        <div className="bg-destructive/15 text-destructive px-4 py-3 rounded-md text-sm font-medium">
          Error loading events: {error}
        </div>
      )}

      {loading && events.length === 0 ? (
        <div className="rounded-md">
          <table className="w-full">
            <thead>
              <tr className="border-b border-sidebar-border/60 bg-muted/50">
                {columns.map((col, i) => (
                  <th key={i} className="h-12 px-4 text-left align-middle font-medium text-muted-foreground">
                    {typeof col.header === 'string' ? col.header : ''}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              <EventsTableSkeleton rows={10} columns={columns.length} />
            </tbody>
          </table>
        </div>
      ) : (
        <DataTable columns={columns} data={events} />
      )}
    </div>
  )
}
