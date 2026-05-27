/**
 * Persistent card image cache using the Cache API.
 *
 * Resolution order:
 *   1. In-memory blob URL (instant, survives within session)
 *   2. Cache API (fast, survives across reloads / app restarts)
 *   3. CDN fetch (r2.videreproject.com)
 *   4. MTGO fallback (/api/collection/cards/:id/image)
 *
 * Once resolved, the image is stored in both layers so subsequent
 * renders never hit the network.
 */

const CACHE_NAME = "videre-card-images-v1"

/** catalogId → blob URL (session-scoped, instant access) */
const memoryCache = new Map<number, string>()

/** Deduplicates concurrent fetches for the same catalogId. */
const inflight = new Map<number, Promise<string | null>>()

function cdnUrl(catalogId: number): string {
  return `https://r2.videreproject.com/${catalogId}-300px.png`
}

function fallbackUrl(catalogId: number): string {
  return `/api/collection/cards/${catalogId}/image`
}

async function resolveAndCache(catalogId: number): Promise<string | null> {
  // 1. Cache API
  try {
    const cache = await caches.open(CACHE_NAME)
    const hit = await cache.match(cdnUrl(catalogId))
    if (hit) {
      const blob = await hit.blob()
      const url = URL.createObjectURL(blob)
      memoryCache.set(catalogId, url)
      return url
    }
  } catch {
    // Cache API unavailable (e.g. opaque origin) — fall through to network
  }

  // 2. CDN
  try {
    const res = await fetch(cdnUrl(catalogId))
    if (res.ok) {
      return await storeResponse(catalogId, res)
    }
  } catch {}

  // 3. MTGO fallback
  try {
    const res = await fetch(fallbackUrl(catalogId))
    if (res.ok) {
      return await storeResponse(catalogId, res)
    }
  } catch {}

  return null
}

async function storeResponse(
  catalogId: number,
  res: Response,
): Promise<string> {
  const clone = res.clone()
  const blob = await res.blob()
  const url = URL.createObjectURL(blob)
  memoryCache.set(catalogId, url)

  // Persist in Cache API
  try {
    const cache = await caches.open(CACHE_NAME)
    await cache.put(cdnUrl(catalogId), clone)
  } catch {}

  return url
}

/**
 * Returns a blob URL for the card image, resolving from the fastest
 * available layer. Deduplicates concurrent requests.
 */
export function getCardImage(
  catalogId: number | null,
): Promise<string | null> {
  if (catalogId == null || catalogId <= 0) return Promise.resolve(null)

  const cached = memoryCache.get(catalogId)
  if (cached) return Promise.resolve(cached)

  let p = inflight.get(catalogId)
  if (!p) {
    p = resolveAndCache(catalogId).finally(() => inflight.delete(catalogId))
    inflight.set(catalogId, p)
  }
  return p
}

/**
 * Synchronous check — returns the blob URL only if it's already in
 * the session memory cache. Used for initial render to avoid flicker.
 */
export function getCardImageSync(
  catalogId: number | null,
): string | null {
  if (catalogId == null || catalogId <= 0) return null
  return memoryCache.get(catalogId) ?? null
}

