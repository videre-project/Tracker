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
import { type CSSProperties, useEffect, useRef, useState } from "react";
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
  activeRowScrollKey?: number
  getRowId?: (row: TData) => string
  onPageRowsChange?: (rows: TData[]) => void
  pageSize?: number
  autoResetPageIndex?: boolean
  containerClassName?: string
  tableContainerClassName?: string
  bodyWrapperClassName?: string
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
  activeRowScrollKey,
  getRowId,
  onPageRowsChange,
  pageSize,
  autoResetPageIndex,
  containerClassName,
  tableContainerClassName,
  bodyWrapperClassName,
  className,
  wrapperClassName,
}: DataTableProps<TData, TValue>) {
  const [expanded, setExpanded] = useState<ExpandedState>(defaultExpanded ? true : {})
  const bodyScrollRef = useRef<HTMLDivElement>(null)
  const rowRefs = useRef(new Map<string, HTMLTableRowElement>())
  const [bodyScrollSize, setBodyScrollSize] = useState({ width: 0, scrollbarWidth: 0 })
  // Tracks the last selection we navigated to so that live `data` updates
  // (which change the `data` reference) don't re-navigate and fight manual
  // pagination. Navigation should only happen when the selection or an
  // explicit scroll request actually changes.
  const lastNavigatedSelectionRef = useRef<{ id: string | null; key: number } | null>(null)

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
  const paginationRows = table.getPaginationRowModel().rows
  const previousPageRowsRef = useRef<TData[]>([])

  useEffect(() => {
    if (!onPageRowsChange) return

    const rows = paginationRows.map(row => row.original)
    const previousRows = previousPageRowsRef.current
    const unchanged = rows.length === previousRows.length &&
      rows.every((row, index) => Object.is(row, previousRows[index]))
    if (unchanged) return

    previousPageRowsRef.current = rows
    onPageRowsChange(rows)
  })

  useEffect(() => {
    if (!bodyWrapperClassName) return

    const element = bodyScrollRef.current
    if (!element) return

    const updateSize = () => {
      setBodyScrollSize({
        width: element.clientWidth,
        scrollbarWidth: Math.max(0, element.offsetWidth - element.clientWidth),
      })
    }

    updateSize()
    if (typeof ResizeObserver === "undefined") return

    const observer = new ResizeObserver(updateSize)
    observer.observe(element)
    return () => observer.disconnect()
  }, [bodyWrapperClassName])

  // Navigate to the page containing the active row when selection or an
  // explicit scroll request changes. We intentionally avoid re-running this
  // on every `data` update: `data` is a freshly-derived array on each live
  // stream tick, and re-navigating there would snap the user back to the
  // selected event's page and prevent manual pagination from working.
  useEffect(() => {
    if (!activeRowId || !getRowId) return
    const last = lastNavigatedSelectionRef.current
    if (last && last.id === activeRowId && last.key === activeRowScrollKey) return
    lastNavigatedSelectionRef.current = { id: activeRowId, key: activeRowScrollKey }

    const index = data.findIndex(d => getRowId(d) === activeRowId)
    if (index === -1) return
    const pageSize = table.getState().pagination.pageSize
    const targetPage = Math.floor(index / pageSize)
    if (targetPage !== table.getState().pagination.pageIndex) {
      table.setPageIndex(targetPage)
    }
  }, [activeRowId, activeRowScrollKey, getRowId, data, table])

  useEffect(() => {
    if (!activeRowId || !bodyWrapperClassName) return

    const container = bodyScrollRef.current
    const row = rowRefs.current.get(activeRowId)
    if (!container || !row) return

    const containerRect = container.getBoundingClientRect()
    const rowRect = row.getBoundingClientRect()
    const isVisible = rowRect.top >= containerRect.top && rowRect.bottom <= containerRect.bottom

    if (!isVisible) {
      row.scrollIntoView({ block: "nearest", behavior: "smooth" })
    }
  }, [activeRowId, activeRowScrollKey, bodyWrapperClassName, paginationRows])

  const renderHeader = () => (
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
  )

  const renderBody = () => (
    <TableBody>
      {paginationRows.length ? (
        paginationRows.map((row) => {
          const rowDomId = getRowId?.(row.original)

          return (
            <TableRow
              key={row.id}
              ref={(element) => {
                if (!rowDomId) return
                if (element) rowRefs.current.set(rowDomId, element)
                else rowRefs.current.delete(rowDomId)
              }}
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
            {row.getVisibleCells().map((cell, cellIndex) => {
              const size = cell.column.columnDef.size
              return (
                <TableCell
                  key={cell.id}
                  className={cellIndex === 0 && row.depth > 0 ? "pl-8" : ""}
                  style={size ? { width: size } : undefined}
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
              )
            })}
            </TableRow>
          )
        })
      ) : (
        <TableRow>
          <TableCell colSpan={columns.length} className="h-24 text-center">
            No results.
          </TableCell>
        </TableRow>
      )}
    </TableBody>
  )

  const bodyScrollStyle: CSSProperties = {
    scrollbarGutter: "stable",
    ...(bodyScrollSize.scrollbarWidth > 0
      ? { marginRight: -bodyScrollSize.scrollbarWidth }
      : {}),
  }
  const bodyTableStyle = bodyScrollSize.width > 0
    ? { width: bodyScrollSize.width }
    : undefined

  return (
    <div className={cn("space-y-2", containerClassName)}>
      <div className={cn("rounded-md", tableContainerClassName)}>
        {bodyWrapperClassName ? (
          <>
            <Table className={cn("table-fixed", className)} wrapperClassName="overflow-hidden">
              {renderHeader()}
            </Table>
            <div
              ref={bodyScrollRef}
              className={bodyWrapperClassName}
              style={bodyScrollStyle}
            >
              <Table
                className={cn("table-fixed", className)}
                wrapperClassName="overflow-visible"
                style={bodyTableStyle}
              >
                {renderBody()}
              </Table>
            </div>
          </>
        ) : (
          <Table className={cn("table-fixed", className)} wrapperClassName={wrapperClassName}>
            {renderHeader()}
            {renderBody()}
          </Table>
        )}
      </div>
      <DataTablePagination table={table} />
    </div>
  )
}
