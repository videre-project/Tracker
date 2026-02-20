import { createContext, useContext, useState, useCallback, useEffect, ReactNode } from "react"
import { useClientState } from "@/hooks/use-client-state"
import { getApiUrl } from "@/utils/api-config"

interface CardArtContextType {
  getArtUrl: (cardName: string) => string | null
  prefetchCards: (cardNames: string[]) => Promise<void>
  isReady: boolean
}

const CardArtContext = createContext<CardArtContextType | null>(null)

// Global cache shared across all contexts
const globalCardArtCache = new Map<string, string>()
const pendingFetches = new Map<string, Promise<string | null>>()

export function CardArtProvider({ children }: { children: ReactNode }) {
  const { isReady: clientReady, loading: clientLoading } = useClientState()
  const [cacheVersion, setCacheVersion] = useState(0)

  const getArtUrl = useCallback((cardName: string): string | null => {
    return globalCardArtCache.get(cardName) ?? null
  }, [cacheVersion]) // eslint-disable-line react-hooks/exhaustive-deps

  const fetchSingleCard = useCallback(async (cardName: string): Promise<string | null> => {
    // Check cache first
    const cached = globalCardArtCache.get(cardName)
    if (cached) return cached

    // Check pending
    const pending = pendingFetches.get(cardName)
    if (pending) return pending

    const fetchPromise = (async () => {
      try {
        const response = await fetch(
          getApiUrl(`/api/collection/cards/${encodeURIComponent(cardName)}/art`)
        )

        if (!response.ok) return null

        const blob = await response.blob()
        const objectUrl = URL.createObjectURL(blob)
        globalCardArtCache.set(cardName, objectUrl)
        return objectUrl
      } catch {
        return null
      } finally {
        pendingFetches.delete(cardName)
      }
    })()

    pendingFetches.set(cardName, fetchPromise)
    return fetchPromise
  }, [])

  const prefetchCards = useCallback(async (cardNames: string[]): Promise<void> => {
    if (!clientReady) return

    const uncached = cardNames.filter(name => name && !globalCardArtCache.has(name))
    if (uncached.length === 0) return

    await Promise.all(uncached.map(fetchSingleCard))

    // Trigger re-render of all consumers
    setCacheVersion(v => v + 1)
  }, [clientReady, fetchSingleCard])

  const value: CardArtContextType = {
    getArtUrl,
    prefetchCards,
    isReady: clientReady && !clientLoading
  }

  return (
    <CardArtContext.Provider value={value}>
      {children}
    </CardArtContext.Provider>
  )
}

export function useCardArtContext() {
  const context = useContext(CardArtContext)
  if (!context) {
    throw new Error("useCardArtContext must be used within a CardArtProvider")
  }
  return context
}

interface CardArtProps {
  cardName: string
  className?: string
}

export function CardArt({ cardName, className = "" }: CardArtProps) {
  const { getArtUrl, prefetchCards, isReady } = useCardArtContext()
  const [localUrl, setLocalUrl] = useState<string | null>(() => getArtUrl(cardName))

  // Try to get from context cache on each render
  const cachedUrl = getArtUrl(cardName)

  // Update local state if context has new value
  useEffect(() => {
    if (cachedUrl && cachedUrl !== localUrl) {
      setLocalUrl(cachedUrl)
    }
  }, [cachedUrl, localUrl])

  // Fetch if not cached and client is ready
  useEffect(() => {
    if (!cardName || localUrl || !isReady) return

    prefetchCards([cardName])
  }, [cardName, localUrl, isReady, prefetchCards])

  const artUrl = localUrl || cachedUrl

  // Show skeleton while loading
  if (!artUrl && cardName) {
    return (
      <div className={`bg-muted/50 animate-pulse ${className}`} />
    )
  }

  if (!artUrl) {
    return (
      <div className={`flex items-center justify-center bg-muted/30 ${className}`}>
        <svg
          xmlns="http://www.w3.org/2000/svg"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.5"
          className="h-5 w-5 text-muted-foreground/50"
        >
          <rect x="3" y="3" width="18" height="18" rx="2" />
          <circle cx="8.5" cy="8.5" r="1.5" />
          <path d="M21 15l-5-5L5 21" />
        </svg>
      </div>
    )
  }

  return (
    <img
      src={artUrl}
      alt={cardName}
      className={`object-cover ${className}`}
    />
  )
}

/**
 * Get a cached card art URL synchronously (for use in SVG/charts)
 */
export function getCachedCardArtUrl(cardName: string): string | null {
  return globalCardArtCache.get(cardName) ?? null
}
