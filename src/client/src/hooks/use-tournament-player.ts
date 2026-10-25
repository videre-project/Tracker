/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useCallback, useEffect, useMemo, useState } from "react"
import { CARD_COLORS } from "@videreproject/constants"
import type { EventDecklistCard, StandingEntry, TournamentMatch, TournamentPlayerDetails } from "@videreproject/ui"

import type { ActiveGame } from "@/hooks/use-events"
import type { TournamentPlayerHistoryDTO } from "@/types/api"
import type { components } from "@/types/videre.g"
import { getApiUrl } from "@/utils/api-config"

import { fetchVidere, getCardMetadata, type VidereCard as PublishedCard } from "@/lib/videre-api"
import { toDeckCards } from "@/utils/deck-list"

type PublishedStanding = components["schemas"]["EventStanding"]
type PublishedMatch = components["schemas"]["EventMatch"]
type PublishedDeck = components["schemas"]["EventDeck"]

interface PublishedEventData {
  eventId: string
  rows: PublishedStanding[]
  decks: PublishedDeck[]
  cards: Map<number, PublishedCard>
  loading: boolean
  error: string | null
}

interface PlayerHistory {
  player: string
  matches: TournamentMatch[]
}

async function readJSON<T>(url: string, signal: AbortSignal): Promise<T> {
  const response = await fetch(url, { signal })
  if (!response.ok) throw new Error(`Request failed (${response.status})`)
  return response.json() as Promise<T>
}

async function publishedRows<T>(path: string, eventId: string, signal: AbortSignal, player?: string): Promise<T[]> {
  const rows: T[] = []
  let offset = 0
  while (true) {
    const query = new URLSearchParams({ event_id: eventId, limit: "500", offset: String(offset) })
    if (player) query.set("player", player)
    const response = await fetchVidere<T[]>(`/${path}?${query}`, signal)
    rows.push(...response.data)
    if (!(response.meta?.has_more ?? response.data.length === 500)) return rows
    const next = response.meta?.next_offset ?? offset + response.data.length
    if (next <= offset) return rows
    offset = next
  }
}

function deckCards(entries: PublishedDeck["mainboard"], metadata?: Map<number, PublishedCard>): EventDecklistCard[] {
  return toDeckCards(entries).map(card => {
    const data = metadata?.get(card.catalogId)
    return {
      id: card.catalogId, name: data?.display_name ?? data?.name ?? card.name, quantity: card.quantity,
      manaValue: data?.mana_value ?? data?.faces?.[0]?.mana_value ?? null,
      manaCost: data?.mana_cost || data?.faces?.map(face => face.mana_cost).filter(Boolean).join(" // ") || null,
      typeLine: data?.type_line ?? data?.faces?.[0]?.type_line ?? null,
      colors: data?.colors ?? undefined, imageUrl: data?.image_url ?? null,
    }
  })
}

function publishedResult(match: PublishedMatch): TournamentMatch["result"] {
  if (match.isbye) return "bye"
  return match.result === "win" || match.result === "loss" || match.result === "draw"
    ? match.result : "pending"
}

/** Prefer recorded event metadata after MTGO unloads a tournament. */
export function useArchivedTournament(eventId: string | null, enabled: boolean) {
  const [result, setResult] = useState<{
    id: string; event: ActiveGame | null; history: TournamentPlayerHistoryDTO | null; loading: boolean
  }>({ id: "", event: null, history: null, loading: false })
  useEffect(() => {
    if (!eventId || !enabled) return
    const controller = new AbortController()
    setResult({ id: eventId, event: null, history: null, loading: true })
    let retry: ReturnType<typeof setTimeout> | undefined
    // A public-archive retry must retain recordings already read from Tracker.
    let history: TournamentPlayerHistoryDTO | null = null
    const load = async () => {
      history = await readJSON<TournamentPlayerHistoryDTO>(
        getApiUrl(`/api/Events/GetPlayerMatches/${eventId}`), controller.signal,
      ).catch(error => { if (controller.signal.aborted) throw error; return history })
      const recorded: ActiveGame | null = history?.eventName ? {
        id: eventId, name: history.eventName, format: history.format ?? "",
        status: history.endTime ? "completed" : "active", state: history.endTime ? "Finished" : undefined,
        type: "swiss", url: `/events/${eventId}`, _rawStartTime: history.startTime ?? undefined,
        _rawEndTime: history.endTime ?? undefined,
      } : null
      if (controller.signal.aborted) return
      if (recorded) setResult({ id: eventId, event: recorded, history, loading: false })
      if (history?.endTime) return
      const rows = await publishedRows<components["schemas"]["Event"]>(
        "events", eventId, controller.signal,
      ).catch(error => { if (controller.signal.aborted) throw error; return [] })
      if (controller.signal.aborted) return
      const row = rows.find(row => String(row.id) === eventId)
      const event: ActiveGame | null = row ? {
        ...recorded, id: eventId, name: recorded?.name ?? row.name, format: recorded?.format || row.format,
        status: "completed", state: "Finished", type: "swiss", url: `/events/${eventId}`,
        totalPlayers: row.players, totalRounds: row.rounds, totalSwissRounds: row.rounds,
        _rawStartTime: recorded?._rawStartTime ?? row.date.slice(0, 10),
      } : recorded
      setResult({ id: eventId, event, history, loading: false })
      if (!row) retry = setTimeout(() => void load().catch(() => {}), 60_000)
    }
    void load().catch(() => {
      if (!controller.signal.aborted) setResult(current => ({ ...current, loading: false }))
    })
    return () => { controller.abort(); clearTimeout(retry) }
  }, [enabled, eventId])
  return enabled && result.id === eventId ? result : { event: null, history: null, loading: enabled }
}

function playerHistory(response: TournamentPlayerHistoryDTO): PlayerHistory {
  if (!response.player) throw new Error("Missing selected player")
  return {
    player: response.player,
    matches: (response.matches ?? []).map(match => ({
      id: match.id ?? null, round: match.round ?? null, opponent: match.opponent ?? null,
      record: match.record ?? null, result: match.result ?? "pending", isRecorded: match.isRecorded ?? false,
    })),
  }
}

/** Live history stays local; published archetypes and decks are requested only after completion. */
export function useTournamentPlayer({
  eventId, completed, enabled, clientReady, revision, recordedHistory,
}: {
  eventId: string | null
  completed: boolean
  enabled: boolean
  clientReady: boolean
  revision: string
  recordedHistory?: TournamentPlayerHistoryDTO | null
}) {
  const [selection, setSelection] = useState<{ eventId: string; player: string } | null>(null)
  const player = selection?.eventId === eventId ? selection.player : recordedHistory?.player ?? null
  const key = `${eventId}|${player ?? ""}`
  const [local, setLocal] = useState<{ key: string; revision: string; history: PlayerHistory | null; error: string | null }>({ key: "", revision: "", history: null, error: null })
  const [published, setPublished] = useState<PublishedEventData | null>(null)
  const [publishedPlayer, setPublishedPlayer] = useState<{
    key: string
    matches: PublishedMatch[]
    error: string | null
  } | null>(null)

  const selectPlayer = useCallback((name: string) => {
    if (eventId) setSelection({ eventId, player: name })
  }, [eventId])

  useEffect(() => {
    if (!eventId || !enabled || recordedHistory?.player === player || local.key === key && local.revision === revision) return
    const controller = new AbortController()
    const query = player ? `?${new URLSearchParams({ player })}` : ""
    void readJSON<TournamentPlayerHistoryDTO>(getApiUrl(`/api/Events/GetPlayerMatches/${eventId}${query}`), controller.signal)
      .then(response => {
        if (controller.signal.aborted) return
        const history = playerHistory(response)
        setLocal({ key: `${eventId}|${history.player}`, revision, history, error: null })
        if (!player) setSelection({ eventId, player: history.player })
      }).catch(() => {
        if (!controller.signal.aborted) setLocal({ key, revision, history: null, error: "Could not load this player's live match history." })
      })
    return () => controller.abort()
  }, [enabled, eventId, key, local.key, local.revision, player, revision, recordedHistory])

  useEffect(() => {
    if (!eventId || !completed) return
    const controller = new AbortController()
    let retry: ReturnType<typeof setTimeout> | undefined
    let snapshot: PublishedEventData = { eventId, rows: [], decks: [], cards: new Map(), loading: true, error: null }
    const load = async () => {
      const [standings, decks] = await Promise.allSettled([
        publishedRows<PublishedStanding>("standings", eventId, controller.signal),
        publishedRows<PublishedDeck>("decks", eventId, controller.signal),
      ])
      if (controller.signal.aborted) return
      snapshot = { ...snapshot, loading: true,
        rows: standings.status === "fulfilled" ? standings.value : snapshot.rows,
        decks: decks.status === "fulfilled" ? decks.value : snapshot.decks,
        error: decks.status === "rejected" ? "Published decklists are unavailable. Retrying…"
          : standings.status === "rejected" ? "Published standings are unavailable. Retrying…" : null,
      }
      setPublished(snapshot)
      try {
        const ids = snapshot.decks.flatMap(deck => [...toDeckCards(deck.mainboard), ...toDeckCards(deck.sideboard)].map(card => card.catalogId))
        const cards = await getCardMetadata(ids, controller.signal)
        if (controller.signal.aborted) return
        snapshot = { ...snapshot, cards }
      } catch {
        if (controller.signal.aborted) return
        snapshot = { ...snapshot, error: "Card metadata is unavailable. Retrying…" }
      }
      snapshot = { ...snapshot, loading: false }
      setPublished(snapshot)
      if (snapshot.error || !snapshot.rows.length || !snapshot.decks.length) retry = setTimeout(load, 60_000)
    }
    void load()
    return () => { controller.abort(); clearTimeout(retry) }
  }, [completed, eventId])

  const publishedStandings = completed && published?.eventId === eventId ? published : null
  useEffect(() => {
    if (eventId && !player && (!clientReady || local.key === key && local.error) && publishedStandings?.rows[0]) {
      setSelection({ eventId, player: publishedStandings.rows[0].player })
    }
  }, [clientReady, eventId, key, local.error, local.key, player, publishedStandings])

  useEffect(() => {
    if (!eventId || !completed || !player) return
    const controller = new AbortController()
    let retry: ReturnType<typeof setTimeout> | undefined
    const load = async () => {
      try {
        const matches = await publishedRows<PublishedMatch>("matches", eventId, controller.signal, player)
        if (controller.signal.aborted) return
        setPublishedPlayer({ key, matches: matches.filter(row => row.player === player), error: null })
        if (!matches.length) retry = setTimeout(load, 60_000)
      } catch {
        if (controller.signal.aborted) return
        setPublishedPlayer(current => ({ key, matches: current?.key === key ? current.matches : [], error: "Published player details are unavailable." }))
        retry = setTimeout(load, 60_000)
      }
    }
    void load()
    return () => { controller.abort(); clearTimeout(retry) }
  }, [completed, eventId, key, player])

  const publishedDecks = useMemo(() => {
    const cards = publishedStandings?.cards
    return new Map(publishedStandings?.decks.map(deck => [deck.player, {
      name: deck.deck_name,
      archetype: deck.archetype,
      mainboard: deckCards(deck.mainboard, cards),
      sideboard: deckCards(deck.sideboard, cards),
    }]) ?? [])
  }, [publishedStandings])
  const publishedColors = useMemo(() => new Map([...publishedDecks].map(([player, deck]) => [player,
    deck.mainboard.some(card => card.colors == null) ? undefined
      : CARD_COLORS.map(color => color.symbol).filter(color => deck.mainboard.some(card => card.colors?.includes(color))),
  ])), [publishedDecks])

  const details = useMemo<TournamentPlayerDetails>(() => {
    const history = local.key === key ? local
      : recordedHistory?.player === player ? { history: playerHistory(recordedHistory), error: null } : null
    const remote = completed && publishedPlayer?.key === key ? publishedPlayer : null
    const localMatches = history?.history?.matches ?? []
    const matches = new Map(localMatches.map(match => [match.id ?? `bye-${match.round}`, match]))
    for (const match of remote?.matches ?? []) {
      const matchKey = match.isbye ? `bye-${match.round}` : match.id
      const existing = matches.get(matchKey)
      matches.set(matchKey, {
        id: match.isbye ? null : match.id,
        round: match.round,
        opponent: match.opponent,
        record: match.isbye ? null : match.record,
        result: publishedResult(match),
        isRecorded: existing?.isRecorded ?? false,
        opponentArchetype: match.opponent_archetype,
        opponentColors: match.opponent ? publishedColors.get(match.opponent) : undefined,
      })
    }
    return {
      player,
      matches: [...matches.values()].sort((a, b) => (a.round ?? Number.MAX_SAFE_INTEGER) - (b.round ?? Number.MAX_SAFE_INTEGER)),
      loading: enabled && !remote && !history,
      error: !matches.size ? history?.error ?? remote?.error ?? (!clientReady && !completed ? "Connect MTGO to view this player's matches." : null) : null,
      deck: player && publishedDecks.get(player)?.mainboard.length ? publishedDecks.get(player) : null,
      deckLoading: completed && !!player && (!publishedStandings || publishedStandings.loading),
      deckError: publishedStandings?.error,
    }
  }, [clientReady, completed, enabled, key, local, player, publishedColors, publishedDecks, publishedPlayer, publishedStandings, recordedHistory])

  const enrichStandings = (standings: StandingEntry[]): StandingEntry[] => {
    const remote = publishedStandings?.rows ?? []
    const byPlayer = new Map(remote.map(row => [row.player, row]))
    if (standings.length) return standings.map(row => ({ ...row, archetype: byPlayer.get(row.player)?.archetype, colors: publishedColors.get(row.player) }))
    return remote.map(row => ({
      rank: row.rank, player: row.player, record: row.record, points: row.points, archetype: row.archetype, colors: publishedColors.get(row.player),
      opponentMatchWinPercentage: row.omwp == null ? "—" : `${row.omwp}%`,
      gameWinPercentage: row.gwp == null ? "—" : `${row.gwp}%`,
      opponentGameWinPercentage: row.owp == null ? "—" : `${row.owp}%`,
    }))
  }

  return { details, selectPlayer, enrichStandings, hasPublishedStandings: !!publishedStandings?.rows.length }
}
