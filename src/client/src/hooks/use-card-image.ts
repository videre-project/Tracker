import { useState, useEffect } from "react"
import { getCardImage, getCardImageSync } from "@/utils/card-image-cache"

/**
 * Returns a persistent blob URL for a card image.
 *
 * - First render: returns the memory-cached URL (instant if previously
 *   resolved in this session), or null while resolving.
 * - Resolves through Cache API → CDN → MTGO fallback, then re-renders
 *   with the blob URL.
 * - Subsequent renders (same session or after reload) are instant.
 */
export function useCardImage(catalogId: number | null): string | null {
  const [src, setSrc] = useState<string | null>(() =>
    getCardImageSync(catalogId),
  )

  useEffect(() => {
    if (catalogId == null || catalogId <= 0) return

    // Already resolved synchronously
    const sync = getCardImageSync(catalogId)
    if (sync) {
      setSrc(sync)
      return
    }

    let cancelled = false
    getCardImage(catalogId).then((url) => {
      if (!cancelled) setSrc(url)
    })
    return () => {
      cancelled = true
    }
  }, [catalogId])

  return src
}
