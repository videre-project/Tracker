"use client"

import {
  ColumnDef,
  ExpandedState,
  flexRender,
  getCoreRowModel,
  getExpandedRowModel,
  getPaginationRowModel,
  useReactTable,
} from "@tanstack/react-table"

import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"

import { cn } from "@/lib/utils";
import { DataTablePagination } from "./data-table-pagination";
import { useEffect, useRef, useState } from "react";
import { ChevronRight } from "lucide-react";

interface DataTableProps<TData, TValue> {
  columns: ColumnDef<TData, TValue>[]
  data: TData[]
  getSubRows?: (row: TData) => TData[] | undefined
  defaultExpanded?: boolean
  onRowHover?: (row: TData) => void
  onRowLeave?: () => void
  onRowClick?: (row: TData) => void
  getRowClassName?: (row: TData) => string
  activeRowId?: string | null
  getRowId?: (row: TData) => string
  pageSize?: number
  autoResetPageIndex?: boolean
  className?: string
  wrapperClassName?: string
}

export function DataTable<TData, TValue>({
  columns,
  data,
  getSubRows,
  defaultExpanded = true,
  onRowHover,
  onRowLeave,
  onRowClick,
  getRowClassName,
  activeRowId,
  getRowId,
  pageSize,
  autoResetPageIndex,
  className,
  wrapperClassName,
}: DataTableProps<TData, TValue>) {
  const [expanded, setExpanded] = useState<ExpandedState>(defaultExpanded ? true : {})

  const table = useReactTable({
    data,
    columns,
    ...(pageSize ? { initialState: { pagination: { pageSize } } } : {}),
    state: { expanded },
    onExpandedChange: setExpanded,
    autoResetPageIndex,
    getSubRows,
    getCoreRowModel: getCoreRowModel(),
    getExpandedRowModel: getSubRows ? getExpandedRowModel() : undefined,
    getPaginationRowModel: getPaginationRowModel(),
  })

  // Navigate to the page containing the active row only when selection changes
  const prevActiveRowId = useRef(activeRowId)
  useEffect(() => {
    if (prevActiveRowId.current === activeRowId) return
    prevActiveRowId.current = activeRowId
    if (!activeRowId || !getRowId) return
    const index = data.findIndex(d => getRowId(d) === activeRowId)
    if (index === -1) return
    const pageSize = table.getState().pagination.pageSize
    const targetPage = Math.floor(index / pageSize)
    if (targetPage !== table.getState().pagination.pageIndex) {
      table.setPageIndex(targetPage)
    }
  }, [activeRowId, getRowId, data, table])

  return (
    <div className="space-y-2">
      <div className="rounded-md">
        <Table className={cn("table-fixed", className)} wrapperClassName={wrapperClassName}>
          <TableHeader>
            {table.getHeaderGroups().map((headerGroup) => (
              <TableRow key={headerGroup.id}>
                {headerGroup.headers.map((header) => {
                  const size = header.column.columnDef.size
                  return (
                    <TableHead
                      key={header.id}
                      style={size ? { width: size } : undefined}
                    >
                      {header.isPlaceholder
                        ? null
                        : flexRender(
                          header.column.columnDef.header,
                          header.getContext()
                        )}
                    </TableHead>
                  )
                })}
              </TableRow>
            ))}
          </TableHeader>
          <TableBody>
            {table.getRowModel().rows?.length ? (
              table.getRowModel().rows.map((row) => (
                <TableRow
                  key={row.id}
                  data-state={row.getIsSelected() && "selected"}
                  className={cn(
                    row.depth > 0 && "bg-muted/30",
                    onRowClick && "cursor-pointer",
                    getRowClassName?.(row.original),
                  )}
                  onMouseEnter={() => onRowHover?.(row.original)}
                  onMouseLeave={() => onRowLeave?.()}
                  onClick={() => onRowClick?.(row.original)}
                >
                  {row.getVisibleCells().map((cell, cellIndex) => (
                    <TableCell
                      key={cell.id}
                      className={cellIndex === 0 && row.depth > 0 ? "pl-8" : ""}
                    >
                      {cellIndex === 0 && row.getCanExpand() ? (
                        <span className="inline-flex items-center gap-1">
                          <button
                            onClick={row.getToggleExpandedHandler()}
                            className="cursor-pointer p-0.5 text-muted-foreground hover:text-foreground transition-transform"
                          >
                            <ChevronRight className={`h-4 w-4 transition-transform ${row.getIsExpanded() ? "rotate-90" : ""}`} />
                          </button>
                          {flexRender(cell.column.columnDef.cell, cell.getContext())}
                        </span>
                      ) : (
                        flexRender(cell.column.columnDef.cell, cell.getContext())
                      )}
                    </TableCell>
                  ))}
                </TableRow>
              ))
            ) : (
              <TableRow>
                <TableCell colSpan={columns.length} className="h-24 text-center">
                  No results.
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </div>
      <DataTablePagination table={table} />
    </div>
  )
}
