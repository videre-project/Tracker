"use client"

import { useMemo } from "react"
import { ColumnDef } from "@tanstack/react-table"
import { DataTable } from "@/components/ui/data-table"
import { Button } from "@/components/ui/button"
import { EventsTableSkeleton } from "@/components/events-table-skeleton"
import { usePaginatedData } from "@/hooks/use-paginated-data"
import { ChevronLeft, ChevronRight } from "lucide-react"

export interface Event {
  id: string
  description: string
  format: string
  totalPlayers: number
  minimumPlayers: number
  totalRounds: number
  startTime: string
  endTime: string
}

function formatDate(dateString: string) {
  const date = new Date(dateString)
  return date.toLocaleString(undefined, {
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

const columns: ColumnDef<Event>[] = [
  {
    accessorKey: "id",
    header: "ID",
  },
  {
    accessorKey: "description",
    header: "Name",
  },
  {
    accessorKey: "format",
    header: "Format",
  },
  {
    accessorKey: "totalPlayers",
    header: "Players",
    cell: ({ row }) => {
      const total = row.getValue("totalPlayers") as number
      const min = row.original.minimumPlayers
      return `${total} / ${min}`
    }
  },
  {
    accessorKey: "totalRounds",
    header: "Rounds",
  },
  {
    accessorKey: "startTime",
    header: "Start Time",
    cell: ({ row }) => formatDate(row.getValue("startTime")),
  },
  {
    accessorKey: "endTime",
    header: "End Time",
    cell: ({ row }) => formatDate(row.getValue("endTime")),
  },
]

export default function Events() {
  const {
    data: events,
    loading,
    error,
    pagination,
    page,
    nextPage,
    previousPage
  } = usePaginatedData<Event>({
    url: '/api/events/geteventslist',
    pageSize: 50
  })

  return (
    <div className="container mx-auto py-4 space-y-4">
      {error && (
        <div className="bg-red-50 border border-red-200 text-red-800 px-4 py-3 rounded">
          Error loading events: {error}
        </div>
      )}

      {loading ? (
        <div className="rounded-md border">
          <table className="w-full">
            <thead>
              <tr className="border-b bg-muted/50">
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

      {/* Pagination Controls */}
      {pagination && (
        <div className="flex items-center justify-between px-2">
          <div className="text-sm text-muted-foreground">
            Page {pagination.page} of {pagination.totalPages} ({pagination.totalCount} total events)
          </div>
          <div className="flex items-center space-x-2">
            <Button
              variant="outline"
              size="sm"
              onClick={previousPage}
              disabled={!pagination.hasPreviousPage || loading}
            >
              <ChevronLeft className="h-4 w-4 mr-1" />
              Previous
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={nextPage}
              disabled={!pagination.hasNextPage || loading}
            >
              Next
              <ChevronRight className="h-4 w-4 ml-1" />
            </Button>
          </div>
        </div>
      )}
    </div>
  )
}
