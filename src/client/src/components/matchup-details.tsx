/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { Link } from "react-router-dom"
import { ChevronRight, Layers } from "lucide-react"
import { Badge, CardImage, ScrollArea, Skeleton, useCardMedia, useCardTooltipHover } from "@videreproject/ui"
import type { SideboardGuideData, PaginatedMatchesDTO, MatchupData, DeckReferenceData } from "@/types/api"
import { useSessionQuery } from "@/hooks/use-session-query"
import { MatchupPairing, type DeckIdentity } from "./matchup-deck-label"
import { GamePerformance } from "./matchup-stats"
import { ActionLabel } from "./action-label"

type PlanCard = NonNullable<SideboardGuideData["bringIn"]>[number]

function GuideCard({ card }: { card: PlanCard }) {
  const hover = useCardTooltipHover({ catalogId: card.catalogId, name: card.name ?? undefined })
  const { getCardArtUrl } = useCardMedia()
  return (
    <li className="flex items-center gap-2 py-1 text-xs" {...hover}>
      <div className="grid h-7 w-7 shrink-0 place-items-center overflow-hidden rounded bg-muted" aria-hidden="true">
        <CardImage catalogId={card.catalogId ?? null} name={card.name ?? undefined}
          imageUrl={card.catalogId ? getCardArtUrl?.(card.catalogId) : undefined} alt=""
          className="h-full w-full object-cover object-[center_25%]"
          fallback={<Layers className="h-3.5 w-3.5 text-muted-foreground" />} />
      </div>
      <span className="min-w-0 flex-1 leading-4">{card.name}</span>
      <span className="shrink-0 tabular-nums text-muted-foreground">{card.quantity}</span>
    </li>
  )
}

function PlanningAction({ label, arrow = false }: { label: string; arrow?: boolean }) {
  return <button type="button" disabled aria-label={`${label} (planned)`}
    className="inline-flex cursor-not-allowed items-center gap-1 text-xs text-muted-foreground/50">
    {arrow ? <ActionLabel>{label}</ActionLabel> : label}
  </button>
}

function SideboardPlan({ guide }: { guide: SideboardGuideData }) {
  return <div className="space-y-3">
    <div className="grid grid-cols-2 gap-4">
      {([
        ["Bring in", guide.bringIn, "text-emerald-500"],
        ["Take out", guide.takeOut, "text-rose-500"]
      ] as const).map(([label, cards, color]) => <div key={label}>
        <h4 className={`mb-1 text-xs font-medium ${color}`}>{label}</h4>
        {cards?.length ? <ul>{cards.map(card => <GuideCard key={card.catalogId} card={card} />)}</ul>
          : <p className="text-xs text-muted-foreground">No cards planned</p>}
      </div>)}
    </div>
    <div>
      <h4 className="mb-1.5 text-sm font-medium">Matchup notes</h4>
      <p className="whitespace-pre-wrap text-xs leading-relaxed text-muted-foreground">{guide.notes || "No notes yet."}</p>
    </div>
    <p className="text-[11px] text-muted-foreground">Deck revision {guide.deck?.revisionId}</p>
  </div>
}

export function MatchupDetails({
  matchup,
  opponent,
  range,
  retainDataKey,
  refreshKey,
}: {
  matchup?: MatchupData
  opponent?: DeckIdentity
  range: URLSearchParams
  retainDataKey: string
  refreshKey?: number
}) {
  const revisionId = matchup?.deck?.revisionId
  const guideQuery = useSessionQuery<SideboardGuideData[]>(revisionId != null
    ? `/api/decks/${revisionId}/sideboard-guides` : null)
  const guides = guideQuery.data ?? []
  if (!matchup)
    return (
      <aside
        aria-label="Matchup details"
        className="matchup-details rounded-lg border border-sidebar-border/60 bg-card p-4 text-sm text-muted-foreground"
      >
        No matchup selected.
      </aside>
    )
  const reviewParams = new URLSearchParams(range)
  if (matchup.deck?.id != null) reviewParams.set("deckId", String(matchup.deck.id))
  else reviewParams.set("archetype", matchup.deck?.archetype ?? "")
  if (matchup.deck?.format) reviewParams.set("format", matchup.deck.format)
  reviewParams.set("opponent", "")
  reviewParams.set("deckName", matchup.deck?.name ?? "")
  reviewParams.set("review", "classify")
  const unclassified = !matchup.opponentArchetype?.trim()
  const plans = guides.filter(guide => guide.deck?.id === matchup?.deck?.id &&
    guide.opponentArchetype?.toLowerCase() === matchup?.opponentArchetype?.toLowerCase())
  const plan = plans.find(guide => guide.deck?.revisionId === matchup?.deck?.revisionId) ?? plans[0]
  return (
    <aside
      aria-label="Matchup details"
      className="matchup-details flex min-h-0 min-w-0 flex-col overflow-hidden rounded-lg border border-sidebar-border/60 bg-card"
    >
      <div className="shrink-0 border-b border-sidebar-border/60 px-4 py-3">
        <MatchupPairing deck={matchup.deck ?? {}} opponent={opponent ?? { name: matchup.opponentArchetype }} />
      </div>
      <ScrollArea className="min-h-0 flex-1">
        <div className="matchup-detail-content flex min-w-0 flex-col gap-5 p-4">
          <section aria-label="Sideboard plan" aria-busy={guideQuery.loading}
            className={`-m-2 rounded-md bg-black/5 p-2 dark:bg-black/20 ${plan ? "space-y-3" : "space-y-2"}`}>
            <div className="flex items-center justify-between gap-3 text-xs">
              <h3 className="text-sm font-medium">{plan ? "Sideboard plan" : "Sideboard plan and notes"}</h3>
              {plan && <PlanningAction label="Edit plan" />}
            </div>
            {guideQuery.error
              ? <p role="alert" className="text-xs text-muted-foreground">Could not load the saved plan and notes.</p>
              : plan ? <SideboardPlan guide={plan} /> : <>
              <p role={guideQuery.loading ? "status" : undefined} className="text-xs text-muted-foreground">{guideQuery.loading
                ? "Loading saved plan and notes…" : matchup.deck?.id == null
                  ? "Choose a deck to view its saved plan and notes." : "No saved plan for this matchup."}</p>
              <PlanningAction label="Review sideboarding" arrow />
            </>}
          </section>
          <div className="matchup-detail-results flex min-w-0 flex-col gap-5">
            <GamePerformance summary={matchup} />
            {matchup.deck && (
              <RecentMatches
                deck={matchup.deck}
                opponent={matchup.opponentArchetype ?? ""}
                range={range}
                retainDataKey={retainDataKey}
                refreshKey={refreshKey}
              />
            )}
          </div>
        </div>
      </ScrollArea>
      <div className="shrink-0 border-t border-sidebar-border/60 px-4 py-1">
        {unclassified ? (
          <Link to={`/history?${reviewParams}`} state={{ opponent }}
            className="flex items-center justify-between py-2 text-xs hover:text-primary">
            Review matches<ChevronRight className="h-3.5 w-3.5" />
          </Link>
        ) : (
          <button type="button" disabled className="flex w-full cursor-not-allowed items-center justify-between py-2 text-xs text-muted-foreground/50">
            Matchup analysis<ChevronRight className="h-3.5 w-3.5" />
          </button>
        )}
      </div>
    </aside>
  )
}

function RecentMatches({
  deck,
  opponent,
  range,
  retainDataKey,
  refreshKey,
}: {
  deck: DeckReferenceData
  opponent: string
  range: URLSearchParams
  retainDataKey: string
  refreshKey?: number
}) {
  const params = new URLSearchParams(range)
  if (deck.id != null) params.set("deckId", String(deck.id))
  else params.set("archetype", deck.archetype ?? "")
  if (deck.format) params.set("format", deck.format)
  params.set("opponent", opponent)
  params.set("pageSize", "3")
  params.set("completedOnly", "true")
  params.set("groupEvents", "false")
  const { data, loading, error } = useSessionQuery<PaginatedMatchesDTO>(
    `/api/games/history?${params}`, { retainDataKey, refreshKey }
  )
  return (
    <section aria-label="Recent matches" aria-busy={loading}>
      <h3 className="mb-2 text-sm font-medium">Recent matches</h3>
      {loading && !data ? (
        <Skeleton className="h-16" />
      ) : error ? (
        <p role="alert" className="text-xs text-muted-foreground">
          Could not load recent matches.
        </p>
      ) : data?.items?.length ? (
        <ul>
          {data.items.slice(0, 3).map((match) => (
            <li key={match.id}>
              <Link
                to={`/history/${match.id}`}
                aria-label={`Review match ${match.id}`}
                className="flex items-center gap-3 rounded py-1.5 text-xs outline-none hover:bg-muted/50 focus-visible:ring-1 focus-visible:ring-ring"
              >
                <Badge
                  variant={
                    match.result === "Win"
                      ? "default"
                      : match.result === "Loss"
                        ? "destructive"
                        : "secondary"
                  }
                  className="rounded-md capitalize"
                >
                  {match.result}
                </Badge>
                <span className="min-w-0 flex-1 truncate">{match.opponentName}</span>
                <span className="shrink-0 tabular-nums text-muted-foreground">
                  {match.record}
                </span>
                <span className="shrink-0 text-muted-foreground">
                  {match.startTime && new Date(match.startTime).toLocaleDateString()}
                </span>
              </Link>
            </li>
          ))}
        </ul>
      ) : (
        <p className="text-xs text-muted-foreground">No recorded matches in this range.</p>
      )}
    </section>
  )
}
