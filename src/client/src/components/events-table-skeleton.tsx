import { Skeleton } from "@/components/ui/skeleton"
import { TableRow, TableCell } from "@/components/ui/table"

interface EventsTableSkeletonProps {
  rows?: number
  columns?: number
}

export function EventsTableSkeleton({ rows = 10, columns = 8 }: EventsTableSkeletonProps) {
  return (
    <>
      {Array.from({ length: rows }).map((_, i) => (
        <TableRow key={i}>
          {Array.from({ length: columns }).map((_, j) => (
            <TableCell key={j}>
              <Skeleton
                className="h-4"
                style={{ width: `${Math.floor(Math.random() * 40) + 60}%` }}
              />
            </TableCell>
          ))}
        </TableRow>
      ))}
    </>
  )
}
