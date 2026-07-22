/**
 * Card image URL helpers and decode-state tracking.
 *
 * CDN images are returned as direct image URLs instead of fetched as blobs.
 * R2 does not currently emit CORS headers, so programmatic fetches from the
 * Vite/Tauri origin are blocked even though browser image loading works.
 */

function cdnUrl(catalogId: number): string {
  return `https://r2.videreproject.com/cards/${catalogId}-300px.png`
}

/**
 * Returns the CDN URL asynchronously to preserve the old hook contract.
 */
export function getCardImage(
  catalogId: number | null,
): Promise<string | null> {
  return Promise.resolve(getCardImageSync(catalogId))
}

/**
 * Returns the direct CDN URL for a card (always non-null for valid IDs).
 */
export function getCardImageSync(
  catalogId: number | null,
): string | null {
  if (catalogId == null || catalogId <= 0) return null
  return cdnUrl(catalogId)
}

/**
 * Returns the API fallback URL for a card.
 */
export function getCardImageFallbackUrl(
  catalogId: number,
  apiBase: string,
): string {
  return `${apiBase}/api/collection/cards/${catalogId}/image`
}

// ----- decode-state tracking (module-level singletons) -----

/** URLs that have been fully decoded and are ready to paint instantly. */
const decodedUrls = new Set<string>()

/** CDN URLs that returned an error (use fallback for these). */
const failedCdnUrls = new Set<string>()

export function isUrlDecoded(url: string): boolean {
  return decodedUrls.has(url)
}

export function isUrlFailed(url: string): boolean {
  return failedCdnUrls.has(url)
}

export function markUrlDecoded(url: string): void {
  decodedUrls.add(url)
}

export function markUrlFailed(url: string): void {
  failedCdnUrls.add(url)
}

/**
 * Returns the best-known working URL for a catalogId.
 * Skips the CDN if it is known to fail for this URL.
 */
export function getBestImageUrl(
  catalogId: number | null,
  fallbackSrc: string | null,
): string | null {
  if (catalogId == null || catalogId <= 0) return null
  const cdn = cdnUrl(catalogId)
  if (isUrlFailed(cdn)) return fallbackSrc
  return cdn
}

// ----- preloading -----

const preloadedUrls = new Set<string>()

/**
 * Eagerly decodes card images into the browser's decoded-image cache.
 * Once decode() resolves the URL is marked as decoded and CardImage will
 * paint it on the very first render with zero blank frames.
 */
export function preloadCardImages(catalogIds: number[]): void {
  for (const catalogId of catalogIds) {
    if (catalogId == null || catalogId <= 0) continue

    // Pick the right URL (skip CDN if known-failed)
    const cdn = cdnUrl(catalogId)
    const url = isUrlFailed(cdn) ? null : cdn
    if (!url || preloadedUrls.has(url)) continue

    preloadedUrls.add(url)

    const img = new Image()
    img.src = url
    if ("decode" in img) {
      img.decode()
        .then(() => markUrlDecoded(url))
        .catch(() => {
          markUrlFailed(url)
        })
    }
  }
}
