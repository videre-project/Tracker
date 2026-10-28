/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { Fragment, useCallback, useEffect, useRef, useState } from "react"
import { Link, useNavigate, useParams, useSearchParams } from "react-router-dom"
import { ArrowLeft, ChevronRight, FileText, Layers3, Pencil, Play, Radio } from "lucide-react"
import {
  Badge, Button, DeckManaSymbols, GameReviewPanel, MatchDeckCard, OpponentArchetypePicker, Skeleton,
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from "@videreproject/ui"
import { ActionLabel } from "@/components/action-label"
import { OpponentClassificationNotice } from "@/components/opponent-classification-notice"
import { loadOpponentArchetypeContext, updateOpponentArchetypes, useMatchDetails } from "@/hooks/use-match-details"
import { useSessionQuery } from "@/hooks/use-session-query"
import type { EventMatchDTO, EventMatchResult, EventPlayerHistoryDTO, GameDetailsDTO, OpponentArchetypeUpdate } from "@/types/api"
import { useClientState } from "@/hooks/use-client-state"
import { useNDJSONStream } from "@/hooks/use-ndjson-stream"
import { getApiUrl } from "@/utils/api-config"
import { activityClockProgress } from "@/utils/activity-clock"
import { getCatalogIdByCardId, getOpeningHandCards, type ReplayCardCatalog } from "@/utils/opening-hand"
import { getSideboardingDiff } from "@/utils/sideboarding"

function resultVariant(result?: string | null) {
  return result === "Win" || result === "Bye" ? "default" : result === "Loss" ? "destructive" : "secondary"
}

const resultLabels: Record<EventMatchResult, string> = {
  win: "Win", loss: "Loss", draw: "Draw", bye: "Bye",
  "in-progress": "In Progress", pending: "Awaiting Result", nullified: "Nullified",
}

export default function EventSummary() {
  const { eventId } = useParams()
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const [expansion, setExpansion] = useState<{ eventId?: string; matchId: number | null } | null>(null)
  const [selection, setSelection] = useState<{ matchId: number; gameId: number | null } | null>(() => {
    const matchId = Number(searchParams.get("matchId"))
    const gameId = Number(searchParams.get("gameId"))
    return Number.isSafeInteger(matchId) && matchId > 0
      ? { matchId, gameId: Number.isSafeInteger(gameId) && gameId > 0 ? gameId : null } : null
  })
  const doubleClickSelection = useRef<typeof selection>(null)
  const { data, loading, error, refresh } = useSessionQuery<EventPlayerHistoryDTO>(
    eventId ? `/api/events/getplayermatches/${eventId}` : null,
  )
  const { isReady } = useClientState()
  const onHistoryUpdate = useCallback((id: number) => {
    if (String(id) === eventId) refresh()
  }, [eventId, refresh])
  useNDJSONStream<number>({
    url: getApiUrl("/api/games/history/watch"),
    onMessage: onHistoryUpdate,
    onOpen: refresh,
    enabled: isReady,
    autoReconnect: true,
    useConstantRetry: true,
    maxReconnectAttempts: 0,
  })
  const clock = data?.activity?.kind === "league" ? data.activity.match?.clock : data?.activity?.roundClock
  const [now, setNow] = useState(Date.now)
  useEffect(() => {
    if (!clock?.running) return
    const timer = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(timer)
  }, [clock?.running])
  const [classificationPending, setClassificationPending] = useState(false)
  const [classificationUndo, setClassificationUndo] = useState<OpponentArchetypeUpdate[] | null>(null)
  const [undoError, setUndoError] = useState<string | null>(null)
  const currentEventId = useRef(eventId)
  useEffect(() => {
    currentEventId.current = eventId
    setClassificationPending(false); setClassificationUndo(null); setUndoError(null)
    return () => { currentEventId.current = undefined }
  }, [eventId])

  const classify = async (match: EventMatchDTO, archetype: string, colors: string[] | null) => {
    if (match.id == null) return
    setClassificationPending(true); setUndoError(null)
    try {
      const previous = await updateOpponentArchetypes([{ matchId: match.id, archetype,
        colors: colors ?? match.opponentDeckColors ?? null }])
      if (currentEventId.current !== eventId) return
      setClassificationUndo(previous); refresh()
    } finally { if (currentEventId.current === eventId) setClassificationPending(false) }
  }
  const undoClassification = async () => {
    if (!classificationUndo) return
    setClassificationPending(true); setUndoError(null)
    try {
      await updateOpponentArchetypes(classificationUndo)
      if (currentEventId.current !== eventId) return
      setClassificationUndo(null); refresh()
    } catch (error) {
      if (currentEventId.current === eventId) setUndoError(error instanceof Error ? error.message : "Could not undo classification.")
    } finally { if (currentEventId.current === eventId) setClassificationPending(false) }
  }
  const back = <Button variant="ghost" size="icon" className="mt-0.5 h-8 w-8 shrink-0" aria-label="Back to history" onClick={() => navigate("/history")}>
    <ArrowLeft className="h-5 w-5" />
  </Button>

  if (loading && !data) return <div className="flex min-h-0 flex-1 flex-col gap-4 px-4 pb-4 pt-1" aria-label="Loading event summary">
    <Skeleton className="h-12 w-64 shrink-0" /><Skeleton className="min-h-0 w-full flex-1" />
  </div>
  if (!data?.activity) return <div className="space-y-4 p-4">
    {back}
    <h1 className="text-xl font-semibold">Event summary unavailable</h1>
    <p role={error ? "alert" : undefined} className="text-sm text-muted-foreground">
      {error ?? "No results are available for this event."}
    </p>
    {error && <Button variant="outline" onClick={refresh}>Try again</Button>}
  </div>

  const { activity, matches = [] } = data
  const total = Math.max(data.totalMatches ?? 0, ...matches.map(match => match.round ?? 0))
  const hasRoundNumbers = matches.every(match => match.round != null)
  const matchesRemaining = Math.max(0, total - matches.filter(match => match.result !== "nullified").length)
  const matchRows: { match: EventMatchDTO | null; number: number | null }[] = []
  const progressMatches: (EventMatchDTO | undefined)[] = []
  if (hasRoundNumbers) {
    for (let number = 1; number <= total; number++) {
      const attempts = matches.filter(match => match.round === number)
      const currentMatch = attempts.find(match => match.result !== "nullified")
      progressMatches.push(currentMatch)
      for (const match of attempts) matchRows.push({ match, number })
      // Keep nullified attempts visible without consuming their replacement slot.
      if (!currentMatch) matchRows.push({ match: null, number })
    }
  } else {
    for (const match of matches) matchRows.push({ match, number: match.round ?? null })
    for (let remaining = 0; remaining < matchesRemaining; remaining++)
      matchRows.push({ match: null, number: null })
  }
  const wins = activity.wins ?? 0
  const losses = activity.losses ?? 0
  const draws = matches.filter(match => match.result === "draw").length
  const deck = activity.deck
  const currentProgress = activityClockProgress(clock, now)
  const currentTicks = Math.floor((currentProgress ?? 0) * 10)
  const defaultMatch = matches.find(match => match.result === "in-progress")
    ?? [...matches].reverse().find(match => match.id != null) ?? matches.at(-1)
  const selectedMatch = matches.find(match => match.id === selection?.matchId) ?? defaultMatch
  const expandedMatchId = expansion && expansion.eventId === eventId ? expansion.matchId : selectedMatch?.id
  const selectedGame = selectedMatch?.games?.find(game => game.id === selection?.gameId)
    ?? selectedMatch?.games?.find(game => game.result === "In Progress")
    ?? selectedMatch?.games?.[0]

  return <div data-ui-layout="event-summary" className="flex min-h-0 w-full flex-1 flex-col gap-4 overflow-hidden px-4 pb-4 pt-1">
    <header className="flex shrink-0 items-start gap-3">
      {back}
      <div className="min-w-0 flex-1 pt-0.5">
        <div className="flex min-w-0 flex-wrap items-center gap-2">
          <h1 className="min-w-0 truncate text-xl font-semibold leading-7 tracking-tight">{activity.name}</h1>
          <div role="group" aria-label="Run record and progress" className="inline-flex items-center gap-3">
            <Badge variant="secondary" className="rounded-md tabular-nums">{wins}–{losses}{draws ? `–${draws}` : ""}</Badge>
            {total > 0 && hasRoundNumbers && <div className="grid h-1.5 w-36 gap-1" role="img"
              aria-label={`${wins} wins, ${losses} losses, ${matchesRemaining} matches remaining`}
              style={{ gridTemplateColumns: `repeat(${total}, minmax(0, 1fr))` }}>
              {progressMatches.map((match, index) => {
                if (match?.result === "in-progress") return <span key={index} className="grid grid-cols-10 gap-px overflow-hidden rounded-sm">
                  {Array.from({ length: 10 }, (_, tick) => <span key={tick} className={tick < currentTicks ? "bg-foreground/90" : "bg-foreground/15"} />)}
                </span>
                let color = "bg-muted"
                if (match?.result === "win" || match?.result === "bye") color = "bg-emerald-500"
                else if (match?.result === "loss") color = "bg-rose-500"
                else if (match?.result === "draw") color = "bg-muted-foreground/60"
                return <span key={index} className={`rounded-sm ${color}`} />
              })}
            </div>}
          </div>
        </div>
      </div>
    </header>

    {error && <div role="alert" className="flex shrink-0 items-center justify-between gap-3 rounded-md border border-sidebar-border/60 bg-card px-3 py-2 text-sm">
      <span>Could not refresh this event. Showing the last loaded results.</span>
      <Button variant="outline" size="sm" onClick={refresh}>Try again</Button>
    </div>}

    <dl className="flex shrink-0 flex-wrap items-center gap-x-5 gap-y-1.5 rounded-md border border-sidebar-border/60 bg-card px-3 py-2 text-xs">
      <div className="flex h-6 items-center gap-1.5">
        <Layers3 aria-hidden="true" className="h-3.5 w-3.5 shrink-0 translate-y-px text-muted-foreground" />
        <dt className="font-medium text-muted-foreground">Format</dt><dd className="font-semibold">{activity.format}</dd>
      </div>
      <div className="flex h-6 items-center gap-1.5">
        <dt className="font-medium text-muted-foreground">{activity.courseId ? "Course" : "Event"}</dt>
        <dd className="font-semibold tabular-nums">#{activity.courseId ?? activity.eventId}</dd>
      </div>
      <div className="flex h-6 items-center gap-1.5">
        {activity.match && <Radio aria-hidden="true" className="h-3 w-3 text-emerald-500" />}
        <dt className="sr-only">Status</dt>
        <dd>{activity.kind === "tournament" ? <Link to={`/events/${activity.eventId}`}
          aria-label={`${activity.stateLabel}, view standings`}
          className="inline-flex rounded outline-none hover:text-primary focus-visible:ring-1 focus-visible:ring-ring">
          <ActionLabel>{activity.stateLabel ?? "Standings"}</ActionLabel>
        </Link> : activity.stateLabel}</dd>
      </div>
    </dl>

    <div className="grid min-h-0 flex-1 auto-rows-max content-start gap-3 overflow-y-auto xl:grid-cols-[28rem_minmax(0,1fr)] xl:grid-rows-[minmax(0,1fr)_auto] xl:content-normal xl:overflow-hidden">
      <section aria-labelledby="event-matches-heading" className="flex h-96 min-h-0 min-w-0 flex-col xl:h-auto"
        onClickCapture={event => {
          if (event.detail < 2) doubleClickSelection.current = null
        }}
        onDoubleClick={() => {
          // Expanding the first click can move another row under the pointer.
          const target = doubleClickSelection.current
          const match = matches.find(match => match.id === target?.matchId)
          if (!target || match?.id == null || !match.isRecorded) return
          doubleClickSelection.current = null
          navigate(`/history/${match.id}?eventSummary=${eventId}${target.gameId != null ? `&gameId=${target.gameId}` : ""}`, { state: { matchNumber: match.round } })
        }}>
        <h2 id="event-matches-heading" className="sr-only">Your matches</h2>
        <Table aria-label="Your matches" scrollLabel="Scroll matches" className="min-w-[28rem] table-fixed"
          wrapperClassName="min-h-0 flex-1 overflow-auto">
          <colgroup>
            <col className="w-[4.5rem]" /><col /><col className="w-28" /><col className="w-20" />
          </colgroup>
          <TableHeader className="sticky top-0 z-10 bg-background">
            <TableRow className="hover:bg-transparent">
              <TableHead aria-label="Match number" className="pl-[2.625rem] text-xs">#</TableHead>
              <TableHead className="text-xs">Opponent</TableHead>
              <TableHead className="text-xs">Result</TableHead>
              <TableHead className="pr-3 text-right text-xs">Games</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody className="border-b border-sidebar-border/60">
            {matchRows.map(({ match, number: matchNumber }, index) => {
              if (!match) return <TableRow key={`empty-${index}`} aria-label={`Match ${matchNumber ?? ""}`} className="hover:bg-transparent">
                <TableCell className="pl-[2.625rem] text-xs leading-5 tabular-nums text-muted-foreground/50"><span className="block h-10">{matchNumber ?? "—"}</span></TableCell>
                <TableCell colSpan={3} />
              </TableRow>
              const active = match.result === "in-progress"
              const matchKey = match.id ?? index
              const games = match.games ?? []
              const expanded = expandedMatchId === matchKey
              const result = resultLabels[match.result ?? "pending"]
              return <Fragment key={matchKey}>
                <TableRow className={`cursor-pointer ${active ? "bg-muted/25" : ""}`}
                  onClick={event => {
                    if (match.id == null) return
                    const game = (match === selectedMatch ? selectedGame : null)
                      ?? games.find(game => game.result === "In Progress") ?? games[0]
                    const nextSelection = { matchId: matchKey, gameId: game?.id ?? game?.gameNumber ?? null }
                    if (event.detail === 1) doubleClickSelection.current = nextSelection
                    setSelection(nextSelection)
                    setExpansion({ eventId, matchId: matchKey })
                  }}>
                  <TableCell className={`relative text-xs leading-5 tabular-nums text-muted-foreground ${games.length ? "pl-0.5" : "pl-[2.625rem]"}`}>
                    {expanded && games.length > 0 && <span aria-hidden="true" className="pointer-events-none absolute -bottom-px left-0.5 top-9 flex w-8 justify-center">
                      <span className="data-table-group-guide w-0.5" />
                    </span>}
                    {/* Align the caret with the first line of the two-line opponent label. */}
                    <span className="flex h-10 items-start gap-2">
                      {games.length > 0 && <Button type="button" variant="ghost" size="icon"
                        className="-my-1.5 h-8 w-8 shrink-0 text-muted-foreground"
                        aria-label={`${expanded ? "Collapse" : "Expand"} match ${matchNumber ?? "—"}`}
                        aria-expanded={expanded}
                        onClick={event => {
                          event.stopPropagation()
                          setExpansion({ eventId, matchId: expanded ? null : matchKey })
                        }}>
                        <ChevronRight className={`h-4 w-4 transition-transform ${expanded ? "rotate-90" : ""}`} />
                      </Button>}
                      {matchNumber ?? "—"}
                    </span>
                  </TableCell>
                  <TableCell>
                    <button type="button" aria-label={`Review match ${matchNumber ?? "—"}`} aria-pressed={match === selectedMatch}
                      className="block w-full rounded text-left outline-none focus-visible:ring-1 focus-visible:ring-ring">
                      <span className="block truncate font-medium">{match.opponent ?? (match.result === "bye" ? "Bye" : "—")}</span>
                    </button>
                    {match.id != null && match.canEditOpponent ? <OpponentArchetypePicker value={match.opponentDeckArchetype ?? null} colors={match.opponentDeckColors}
                      loadContext={signal => loadOpponentArchetypeContext([matchKey], [activity.format ?? ""], signal)}
                      onChange={(archetype, colors) => classify(match, archetype, colors)}>
                      <button type="button" aria-label={`Edit opponent archetype in match ${matchNumber ?? "—"}`} disabled={classificationPending}
                        onClick={event => event.stopPropagation()} onDoubleClick={event => event.stopPropagation()}
                        className="group/classify mt-0.5 inline-flex max-w-full items-center gap-1.5 rounded text-left text-xs text-muted-foreground outline-none hover:text-foreground focus-visible:ring-1 focus-visible:ring-ring">
                        <span className="truncate">{match.opponentDeckArchetype || "Unclassified deck"}</span>
                        <DeckManaSymbols colors={match.opponentDeckColors} className="shrink-0" />
                        <Pencil className="h-3 w-3 shrink-0 opacity-0 group-hover/classify:opacity-100 group-focus-visible/classify:opacity-100" />
                      </button>
                    </OpponentArchetypePicker> : <span className="mt-0.5 inline-flex max-w-full items-center gap-1.5 text-xs text-muted-foreground">
                      <span className="truncate">{match.opponentDeckArchetype || "Unclassified deck"}</span>
                      <DeckManaSymbols colors={match.opponentDeckColors} className="shrink-0" />
                    </span>}
                  </TableCell>
                  <TableCell>
                    <Badge variant={resultVariant(result)} className="whitespace-nowrap rounded-md">{result}</Badge>
                  </TableCell>
                  <TableCell className="pr-3 text-right text-xs tabular-nums">{match.record}</TableCell>
                </TableRow>
                {expanded && games.map((game, gameIndex) => {
                  const selected = match === selectedMatch && game === selectedGame
                  const gameNumber = game.gameNumber ?? gameIndex + 1
                  return <TableRow key={game.id ?? gameIndex} data-state={selected ? "selected" : undefined}
                    className="cursor-pointer bg-muted/30"
                    onClick={() => setSelection({ matchId: matchKey, gameId: game.id ?? gameNumber })}>
                    <TableCell className="relative">
                      <span aria-hidden="true" className={`pointer-events-none absolute -top-px left-0.5 flex w-8 justify-center ${gameIndex === games.length - 1 ? "bottom-2" : "-bottom-px"}`}>
                        <span className="data-table-group-guide w-0.5" />
                      </span>
                    </TableCell>
                    <TableCell>
                      <button type="button" aria-label={`Review match ${matchNumber ?? "—"}, game ${gameNumber}`} aria-pressed={selected}
                        className="block w-full rounded text-left outline-none focus-visible:ring-1 focus-visible:ring-ring">
                        <span className="block font-medium">Game {gameNumber}</span>
                        <span className="mt-0.5 inline-flex text-xs text-muted-foreground">
                          {game.playDraw === "Play" || game.playDraw === "Draw" ? `On the ${game.playDraw.toLowerCase()}` : "—"}
                        </span>
                      </button>
                    </TableCell>
                    <TableCell><Badge variant={resultVariant(game.result)} className="whitespace-nowrap rounded-md">{game.result || "Unknown"}</Badge></TableCell>
                    <TableCell className="pr-3 text-right text-xs tabular-nums text-muted-foreground" aria-label="Game duration">{game.duration || "—"}</TableCell>
                  </TableRow>
                })}
              </Fragment>
            })}
          </TableBody>
        </Table>
      </section>

      <section aria-label="Selected game review" className="flex h-[36rem] min-h-0 min-w-0 flex-col overflow-hidden rounded-md border border-sidebar-border/60 bg-card xl:col-start-2 xl:row-span-2 xl:row-start-1 xl:h-auto">
        {selectedMatch ? <EventGameReview key={selectedMatch.id} match={selectedMatch} game={selectedGame} eventId={eventId!} />
          : <div className="flex flex-1 items-center justify-center p-4 text-sm text-muted-foreground">No matches recorded for this run.</div>}
      </section>

      {deck && <section aria-label="Deck" className="min-w-0 shrink-0 xl:col-start-1 xl:row-start-2">
        <MatchDeckCard deckName={deck.name} deckArchetype={deck.archetype} deckColors={deck.colors}
          previewCards={(deck.featuredCards ?? []).map(card => ({ catalogId: card.catalogId ?? 0, name: card.name ?? "" }))}
          onOpen={deck.revisionId ? () => navigate(`/decks/${deck.revisionId}`) : undefined} />
      </section>}
    </div>
    {classificationUndo && <OpponentClassificationNotice count={classificationUndo.length} pending={classificationPending} error={undoError}
      onUndo={() => void undoClassification()} onDismiss={() => setClassificationUndo(null)} />}
  </div>
}

function EventGameReview({ match, game, eventId }: { match: EventMatchDTO; game?: GameDetailsDTO; eventId: string }) {
  const { data: recording, loading: recordingLoading, error: recordingError, refresh: retryRecording } = useMatchDetails(
    match.isRecorded ? match.id ?? null : null, { refreshKey: match },
  )
  const recordedGame = recording?.games?.find(item => item.id === game?.id)
  const handKey = getOpeningHandCards(recordedGame?.logs ?? [], new Map()).map(card => card.key).join(",")
  const { data: replay, loading, error, refresh } = useSessionQuery<ReplayCardCatalog>(
    recordedGame?.id && recordedGame.logs?.length ? `/api/games/game/${recordedGame.id}/replay` : null,
    { refreshKey: handKey },
  )
  const openingHand = getOpeningHandCards(recordedGame?.logs ?? [], getCatalogIdByCardId(replay))
  const sideboarding = getSideboardingDiff(recordedGame?.sideboardChanges)

  if (recordingError && !recording) return <div role="alert" className="flex flex-1 flex-col items-center justify-center gap-3 p-4 text-sm text-muted-foreground">
    <span>Could not load this recording.</span><Button variant="outline" size="sm" onClick={retryRecording}>Try again</Button>
  </div>
  if (match.isRecorded && recordingLoading && !recordedGame) return <Skeleton className="m-4 min-h-0 flex-1" aria-label="Loading recording" />
  if (!recordedGame) return <div className="flex flex-1 items-center justify-center p-4 text-sm text-muted-foreground">No recording for this game.</div>

  return <>
    <h2 className="sr-only">Match {match.round}{game && ` — Game ${game.gameNumber}`}</h2>
    {game ? <div aria-busy={loading || recordingLoading} className="min-h-0 flex-1 overflow-hidden">
      <GameReviewPanel openingHandCards={openingHand} sideboardingDiff={{ ...sideboarding, emptyMessage: "No sideboard changes recorded." }}
        endContent={game.id ? <div role="group" aria-label="Game review actions" className="flex items-center gap-2">
          <Button asChild variant="outline" size="sm" className="h-7 shrink-0 gap-1.5 border-sidebar-border/60 px-2.5 text-xs">
            <Link to={`/history/${match.id}/watch?gameId=${game.id}&eventSummary=${eventId}`}><FileText className="h-3.5 w-3.5" />Game Log</Link>
          </Button>
          <Button asChild variant="outline" size="sm" className="h-7 shrink-0 gap-1.5 border-sidebar-border/60 px-2.5 text-xs">
            <Link to={`/history/${match.id}/game/${game.id}/replay?eventSummary=${eventId}`} state={{ eventId: Number(eventId), gameNumber: game.gameNumber }}><Play className="h-3.5 w-3.5 translate-y-px" />Replay</Link>
          </Button>
        </div> : null} />
    </div> : <div className="flex flex-1 items-center justify-center p-4 text-sm text-muted-foreground">No games recorded for this match.</div>}
    {(recordingError || error) && <div role="alert" className="flex shrink-0 items-center justify-between gap-2 border-t border-sidebar-border/60 px-3 py-2 text-xs text-muted-foreground">
      <span>{recordingError ? "Recording could not be refreshed." : "Replay data could not be loaded."}</span>
      <Button variant="ghost" size="sm" onClick={recordingError ? retryRecording : refresh}>Retry</Button>
    </div>}
  </>
}
