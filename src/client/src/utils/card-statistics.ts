import type { DeckCardStatisticsData } from "@/types/api"

export const cardMetrics = [["inHand", "In hand"], ["openingHand", "Opening hand"], ["libraryToHand", "Library to hand"], ["cast", "Cast"]] as const
export type CardMetric = typeof cardMetrics[number][0]
export type CardSort = "winrate" | "games"
export type CardZone = "main" | "side" | "all"

export function readCardZone(value: string | null): CardZone {
  return value === "side" || value === "all" ? value : "main"
}

export function cardInZone(row: DeckCardStatisticsData, zone: CardZone): boolean {
  return zone === "all" || (zone === "main" ? row.mainboardCopies ?? 0 : row.sideboardCopies ?? 0) > 0
}
export const cardMetricDescriptions: Record<CardMetric, string> = {
  inHand: "Game winrate when observed in your hand during play.",
  openingHand: "Game winrate when captured in your kept opening hand, after bottoming.",
  libraryToHand: "Game winrate when observed moving from your library to your hand during play.",
  cast: "Game winrate when observed entering the stack as a spell."
}
export const sampleThresholds = [0, 5, 10, 20] as const

export function readCardMetric(value: string | null): CardMetric {
  return cardMetrics.find(([metric]) => metric === value)?.[0] ?? "inHand"
}

export function readSampleThreshold(value: string | null): number {
  return value != null && sampleThresholds.some(threshold => String(threshold) === value) ? Number(value) : 10
}

/** Empty observations always follow measured results, including when sorting ascending. */
export function compareCardObservations(a: DeckCardStatisticsData, b: DeckCardStatisticsData,
  metric: CardMetric, sort: CardSort, descending: boolean, cardOrder?: ReadonlyMap<string, number>) {
  const left = a[metric], right = b[metric]
  const leftMeasured = (left?.total ?? 0) > 0 && left?.winrate != null
  const rightMeasured = (right?.total ?? 0) > 0 && right?.winrate != null
  if (leftMeasured !== rightMeasured) return leftMeasured ? -1 : 1
  const difference = sort === "winrate"
    ? (right?.winrate ?? 0) - (left?.winrate ?? 0)
    : (right?.total ?? 0) - (left?.total ?? 0)
  if (difference) return difference * (descending ? 1 : -1)
  const tie = cardOrder
    ? (cardOrder.get(a.card?.name ?? "") ?? Number.MAX_SAFE_INTEGER) - (cardOrder.get(b.card?.name ?? "") ?? Number.MAX_SAFE_INTEGER)
    : (right?.total ?? 0) - (left?.total ?? 0)
  return tie || (a.card?.name ?? "").localeCompare(b.card?.name ?? "")
}
