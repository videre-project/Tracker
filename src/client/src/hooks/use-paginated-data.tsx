import { useState, useEffect, useCallback } from "react"
import { parseNDJSONStream } from "@/lib/ndjson"

export interface PaginationMetadata {
  totalCount: number
  page: number
  pageSize: number
  totalPages: number
  hasNextPage: boolean
  hasPreviousPage: boolean
}

export interface UsePaginatedDataOptions<T> {
  /**
   * The base URL to fetch from (without pagination params)
   */
  url: string

  /**
   * Initial page number (1-based)
   * @default 1
   */
  initialPage?: number

  /**
   * Number of items per page
   * @default 50
   */
  pageSize?: number

  /**
   * Whether to automatically fetch on mount
   * @default true
   */
  enabled?: boolean

  /**
   * Transform function to apply to each fetched item
   */
  transform?: (item: any) => T
}

export interface UsePaginatedDataResult<T> {
  data: T[]
  loading: boolean
  error: string | null
  pagination: PaginationMetadata | null
  page: number
  setPage: (page: number) => void
  nextPage: () => void
  previousPage: () => void
  refetch: () => void
}

/**
 * Hook for fetching paginated data from an API endpoint
 *
 * @example
 * ```tsx
 * const { data, loading, pagination, nextPage, previousPage } = usePaginatedData({
 *   url: '/api/events/geteventslist',
 *   pageSize: 20
 * })
 * ```
 */
export function usePaginatedData<T = any>(
  options: UsePaginatedDataOptions<T>
): UsePaginatedDataResult<T> {
  const {
    url,
    initialPage = 1,
    pageSize = 50,
    enabled = true,
    transform
  } = options

  const [data, setData] = useState<T[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [pagination, setPagination] = useState<PaginationMetadata | null>(null)
  const [page, setPage] = useState(initialPage)

  const fetchPage = useCallback(async (pageNum: number) => {
    if (!enabled) return

    setLoading(true)
    setError(null)

    try {
      const params = new URLSearchParams({
        page: pageNum.toString(),
        pageSize: pageSize.toString()
      })

      const response = await fetch(`${url}?${params}`, {
        headers: { Accept: "application/x-ndjson" }
      })

      if (!response.ok) {
        throw new Error(`Failed to fetch: ${response.status} ${response.statusText}`)
      }

      // Extract pagination metadata from headers
      const metadata: PaginationMetadata = {
        totalCount: parseInt(response.headers.get('X-Total-Count') || '0'),
        page: parseInt(response.headers.get('X-Page') || pageNum.toString()),
        pageSize: parseInt(response.headers.get('X-Page-Size') || pageSize.toString()),
        totalPages: parseInt(response.headers.get('X-Total-Pages') || '0'),
        hasNextPage: response.headers.get('X-Has-Next-Page') === 'true',
        hasPreviousPage: response.headers.get('X-Has-Previous-Page') === 'true'
      }

      setPagination(metadata)

      // Parse NDJSON stream
      const reader = response.body?.getReader()
      if (!reader) {
        throw new Error("No stream reader available")
      }

      const items = await parseNDJSONStream(reader)

      const transformedData = transform
        ? items.map(transform)
        : items

      setData(transformedData)
    } catch (e) {
      const errorMessage = e instanceof Error ? e.message : 'Unknown error'
      setError(errorMessage)
      console.error('Paginated data fetch error:', e)
    } finally {
      setLoading(false)
    }
  }, [url, pageSize, enabled, transform])

  // Fetch data when page changes
  useEffect(() => {
    fetchPage(page)
  }, [page, fetchPage])

  const nextPage = useCallback(() => {
    if (pagination?.hasNextPage) {
      setPage(p => p + 1)
    }
  }, [pagination])

  const previousPage = useCallback(() => {
    if (pagination?.hasPreviousPage) {
      setPage(p => p - 1)
    }
  }, [pagination])

  const refetch = useCallback(() => {
    fetchPage(page)
  }, [fetchPage, page])

  return {
    data,
    loading,
    error,
    pagination,
    page,
    setPage,
    nextPage,
    previousPage,
    refetch
  }
}
