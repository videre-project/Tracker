/**
 * Card image URL helpers.
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
 * Returns the direct CDN URL for immediate image rendering.
 */
export function getCardImageSync(
  catalogId: number | null,
): string | null {
  if (catalogId == null || catalogId <= 0) return null
  return cdnUrl(catalogId)
}
