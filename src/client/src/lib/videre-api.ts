/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import type { components } from "@/types/videre.g"

export type VidereCard = components["schemas"]["Card"]

export async function fetchVidere<T>(
  path: string, signal: AbortSignal, init?: Omit<RequestInit, "signal">,
): Promise<{ data: T; meta?: Partial<components["schemas"]["ListResponse"]["meta"]> }> {
  const response = await fetch(`https://api.videreproject.com${path}`, { ...init, signal })
  if (!response.ok) {
    const result = await response.json().catch(() => null)
    // The catalog uses HTTP 400 for an empty result set.
    if (response.status === 400 && result?.object === "error" && result.message === "No results found." &&
        result.body?.object === "list" && Array.isArray(result.body.data))
      return result.body
    throw new Error(`Videre API request failed (${response.status}): ${result?.message ?? response.statusText}`)
  }
  return response.json()
}

const cardMetadata = new Map<number, VidereCard>()

/** Cache successful reads only; retries request missing batches and unresolved faces. */
export async function getCardMetadata(catalogIds: number[], signal: AbortSignal): Promise<Map<number, VidereCard>> {
  const ids = [...new Set(catalogIds)]
  const missing = ids.filter(id => !cardMetadata.has(id))
  const batches = Array.from({ length: Math.ceil(missing.length / 500) }, (_, index) => missing.slice(index * 500, (index + 1) * 500))
  const pages = await Promise.allSettled(batches.map(ids => fetchVidere<VidereCard[]>(
    "/cards/search?limit=500&unique=prints", signal, {
      method: "QUERY", headers: { "content-type": "application/json" },
      body: JSON.stringify({ collection: { ids, mode: "only", match: "prints" } }),
    }).then(response => response.data)))
  if (signal.aborted) throw new DOMException("Aborted", "AbortError")
  for (const page of pages) if (page.status === "fulfilled") for (const card of page.value) cardMetadata.set(card.id, card)
  const failedPage = pages.find(page => page.status === "rejected")
  if (failedPage?.status === "rejected") throw failedPage.reason

  const faces = await Promise.allSettled(ids.filter(id => {
    const card = cardMetadata.get(id)
    return card?.is_multiface && !card.mana_cost && !card.faces
  }).map(id => fetchVidere<VidereCard[]>(`/cards/${id}`, signal).then(response => response.data)))
  if (signal.aborted) throw new DOMException("Aborted", "AbortError")
  for (const page of faces) if (page.status === "fulfilled") for (const card of page.value) cardMetadata.set(card.id, card)
  const failedFace = faces.find(page => page.status === "rejected")
  if (failedFace?.status === "rejected") throw failedFace.reason
  return new Map(ids.filter(id => cardMetadata.has(id)).map(id => [id, cardMetadata.get(id)!]))
}
