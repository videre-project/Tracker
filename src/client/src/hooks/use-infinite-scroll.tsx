import { useState, useEffect, useCallback, useRef } from "react"
import { parseNDJSONStream } from "@/lib/ndjson"

export interface UseInfiniteScrollOptions<T> {
  /**
   * The base URL to fetch from (without pagination params)
   */
  url: string

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

  /**
   * Threshold in pixels from bottom to trigger next page load
   * @default 500
   */
  threshold?: number
}

export interface UseInfiniteScrollResult<T> {
  data: T[]
  loading: boolean
  error: string | null
  hasMore: boolean
  loadMore: () => void
  reset: () => void
  observerRef: (node: HTMLElement | null) => void
}

/**
 * Hook for infinite scroll pagination
 *
 * @example
 * ```tsx
 * const { data, loading, hasMore, observerRef } = useInfiniteScroll({
 *   url: '/api/events/geteventslist',
 *   pageSize: 20
 * })
 *
 * return (
 *   <div>
 *     {data.map(item => <Item key={item.id} {...item} />)}
 *     {hasMore && <div ref={observerRef}>Loading...</div>}
 *   </div>
 * )
 * ```
 */
export function useInfiniteScroll<T = any>(
  options: UseInfiniteScrollOptions<T>
): UseInfiniteScrollResult<T> {
  const {
    url,
    pageSize = 50,
    enabled = true,
    transform,
    threshold = 500
  } = options

  const [data, setData] = useState<T[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [page, setPage] = useState(1)
  const [hasMore, setHasMore] = useState(true)

  const observerRef = useRef<IntersectionObserver | null>(null)
  const loadingRef = useRef(false)

  const fetchPage = useCallback(async (pageNum: number) => {
    if (!enabled || loadingRef.current || !hasMore) return

    loadingRef.current = true
    setLoading(true)
    setError(null)

    try {
      const params = new URLSearchParams({
        page: pageNum.toString(),
        pageSize: pageSize.toString(),
        includeCount: 'false'
      })

      const response = await fetch(`${url}?${params}`, {
        headers: { Accept: "application/x-ndjson" }
      })

      if (!response.ok) {
        throw new Error(`Failed to fetch: ${response.status} ${response.statusText}`)
      }

      const hasNextPage = response.headers.get('X-Has-Next-Page') === 'true'
      setHasMore(hasNextPage)

      // Parse NDJSON stream
      const reader = response.body?.getReader()
      if (!reader) {
        throw new Error("No stream reader available")
      }

      const items = await parseNDJSONStream(reader)

      const transformedData = transform
        ? items.map(transform)
        : items

      setData(prev => [...prev, ...transformedData])
    } catch (e) {
      const errorMessage = e instanceof Error ? e.message : 'Unknown error'
      setError(errorMessage)
      console.error('Infinite scroll fetch error:', e)
    } finally {
      setLoading(false)
      loadingRef.current = false
    }
  }, [url, pageSize, enabled, transform, hasMore])

  const loadMore = useCallback(() => {
    if (hasMore && !loadingRef.current) {
      setPage(p => p + 1)
    }
  }, [hasMore])

  const reset = useCallback(() => {
    setData([])
    setPage(1)
    setHasMore(true)
    setError(null)
  }, [])

  // Fetch data when page changes
  useEffect(() => {
    if (enabled) {
      fetchPage(page)
    }
  }, [page, fetchPage, enabled])

  // Set up intersection observer for automatic loading
  const setObserverRef = useCallback((node: HTMLElement | null) => {
    if (observerRef.current) {
      observerRef.current.disconnect()
    }

    if (!node || !hasMore) return

    observerRef.current = new IntersectionObserver(
      (entries) => {
        if (entries[0].isIntersecting && !loadingRef.current) {
          loadMore()
        }
      },
      {
        rootMargin: `${threshold}px`
      }
    )

    observerRef.current.observe(node)
  }, [hasMore, loadMore, threshold])

  // Cleanup observer on unmount
  useEffect(() => {
    return () => {
      if (observerRef.current) {
        observerRef.current.disconnect()
      }
    }
  }, [])

  return {
    data,
    loading,
    error,
    hasMore,
    loadMore,
    reset,
    observerRef: setObserverRef
  }
}
