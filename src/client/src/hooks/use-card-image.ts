import { useEffect, useState } from "react"

import { getCardImage, getCardImageSync } from "@/utils/card-image-cache"

/**
 * Returns the direct CDN URL for a card image.
 *
 * The CDN URL is rendered directly through an <img>, because R2 does not
 * currently emit CORS headers for programmatic fetches from the app origin.
 * Callers that need a fallback should handle image onError.
 */
export function useCardImage(catalogId: number | null): string | null {
  const [src, setSrc] = useState<string | null>(() =>
    getCardImageSync(catalogId),
  )

  useEffect(() => {
    if (catalogId == null || catalogId <= 0) {
      setSrc(null)
      return
    }

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
