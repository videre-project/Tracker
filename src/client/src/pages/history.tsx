/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useApplicationSettings } from "@/hooks/use-application-settings"
import { initialDateRange } from "@/lib/application-preferences"
import { useState, useMemo, useCallback, useEffect, type ReactNode } from "react"
import { useGamesHistory } from "@/hooks/use-games"
import { useGameFormats } from "@/hooks/use-games"
import { useClientState } from "@/hooks/use-client-state"
import { useNDJSONStream } from "@/hooks/use-ndjson-stream"
import { dateRangeParams, getApiUrl } from "@/utils/api-config"
import { useNavigate, useSearchParams } from "react-router-dom"
import { loadOpponentArchetypeContext, updateOpponentArchetypes } from "@/hooks/use-match-details"
import { OpponentClassificationNotice } from "@/components/opponent-classification-notice"
import type { OpponentArchetypeUpdate } from "@/types/api"
import {
  HistoryLayout,
  OpponentArchetypePicker,
  compareFormats,
  type GameType,
  type MatchHistoryItem,
} from "@videreproject/ui"
import type { DateRange } from "react-day-picker"

function getHistoryRowHref(match: MatchHistoryItem): string | null {
  if (match.isEvent) return `/events/${match.eventId}`
  return `/history/${match.id}`
}

export default function History() {
  const { preferences } = useApplicationSettings()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const defaultRange = useMemo(() => initialDateRange(preferences.historyRangeDays), [preferences.historyRangeDays])
  const minDate = params.get("minDate"), maxDate = params.get("maxDate"), allTime = params.get("allTime") === "true"
  const dateRange = useMemo<DateRange | undefined>(() => allTime ? undefined
    : minDate && Number.isFinite(Date.parse(minDate)) ? { from: new Date(minDate), to: maxDate && Number.isFinite(Date.parse(maxDate)) ? new Date(maxDate) : undefined }
      : defaultRange, [allTime, minDate, maxDate, defaultRange])
  const selectedFormat = params.get("format") ?? ""
  const [gameType, setGameType] = useState<GameType>("All")
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(preferences.historyPageSize)
  const [pending, setPending] = useState(false)
  const [undo, setUndo] = useState<OpponentArchetypeUpdate[] | null>(null)
  const [undoError, setUndoError] = useState<string | null>(null)
  const review = params.get("review") === "classify"
  const filters = new URLSearchParams()
  filters.set("gameType", gameType)
  for (const key of ["deckId", "deckRevisionId", "archetype", "opponent"]) if (params.has(key)) filters.set(key, params.get(key)!)
  if (review) { filters.set("groupEvents", "false"); filters.set("includeOpponentCards", "true"); filters.set("completedOnly", "true") }
  const scope = filters.toString()

  // The format list is shared with the dashboard and deck filters.
  const { formats, loading: formatsLoading, error: formatsError } = useGameFormats(gameType)

  const filteredFormats = useMemo(() => {
    return [...formats].sort(compareFormats)
  }, [formats])

  useEffect(() => {
    if (selectedFormat && !formatsLoading && !formatsError && !filteredFormats.includes(selectedFormat)) {
      setParams(current => { const next = new URLSearchParams(current); next.delete("format"); return next })
      setPage(1)
    }
  }, [selectedFormat, formatsLoading, formatsError, filteredFormats, setParams])

  const effectiveRange = dateRange || "ALL"
  const { data, loading, error, refresh } = useGamesHistory(page, pageSize, effectiveRange, selectedFormat, undefined, scope)
  const { isReady: clientReady } = useClientState()

  // Subscribe to event/match creation/completion SSE stream
  const onSSEMessage = useCallback(() => refresh(), [refresh])

  useNDJSONStream<MatchHistoryItem>({
    url: getApiUrl("/api/games/history/watch"),
    onMessage: onSSEMessage,
    enabled: clientReady,
    autoReconnect: true,
    reconnectDelay: 2000,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })

  const mergedItems = data?.items ?? []
  const classify = useCallback(async (match: MatchHistoryItem, archetype: string, colors: string[] | null) => {
    setPending(true); setUndoError(null)
    try {
      const previous = await updateOpponentArchetypes([{ matchId: match.id, archetype,
        colors: colors ?? match.opponentDeckColors ?? null }])
      setUndo(previous); if (review) setPage(1); refresh()
    } finally { setPending(false) }
  }, [refresh, review])
  const renderOpponentArchetype = useCallback((match: MatchHistoryItem, trigger: ReactNode) => (
    <OpponentArchetypePicker value={match.opponentDeckArchetype ?? null} colors={match.opponentDeckColors}
      loadContext={signal => loadOpponentArchetypeContext([match.id], [match.format], signal)}
      onChange={(archetype, colors) => classify(match, archetype, colors)}>{trigger}</OpponentArchetypePicker>
  ), [classify])
  const undoClassification = async () => {
    if (!undo) return
    setPending(true); setUndoError(null)
    try { await updateOpponentArchetypes(undo); setUndo(null); refresh() }
    catch (error) { setUndoError(error instanceof Error ? error.message : "Could not undo classification.") }
    finally { setPending(false) }
  }

  const handleFormatSelect = (f: string) => {
    setParams(current => { const next = new URLSearchParams(current); if (f) next.set("format", f); else next.delete("format"); return next })
    setPage(1)
  }

  const handleGameTypeSelect = (value: GameType) => {
    setGameType(value)
    setPage(1)
  }

  const handleDateChange = (range: DateRange | undefined) => {
    setParams(current => {
      const next = new URLSearchParams(current)
      next.delete("minDate"); next.delete("maxDate"); next.delete("allTime")
      if (range) for (const [key, value] of dateRangeParams(range)) next.set(key, value)
      else next.set("allTime", "true")
      return next
    })
    setPage(1)
  }

  const handleRowClick = useCallback(
    (match: MatchHistoryItem) => {
      const href = getHistoryRowHref(match)
      if (href) navigate(href)
    },
    [navigate],
  )

  return (
    <>
    <HistoryLayout
      items={mergedItems}
      loading={loading}
      error={error}
      gameType={gameType}
      onGameTypeChange={handleGameTypeSelect}
      selectedFormat={selectedFormat}
      formats={filteredFormats}
      onFormatChange={handleFormatSelect}
      dateRange={dateRange}
      onDateRangeChange={handleDateChange}
      onRowClick={handleRowClick}
      renderOpponentArchetype={renderOpponentArchetype}
      reviewLabel={review ? `${params.get("deckName") ? `${params.get("deckName")}: ` : ""}${params.get("opponent") || "Unclassified opponents"}` : undefined}
      onClearReview={() => {
        setParams(current => { const next = new URLSearchParams(current); for (const key of ["deckId", "deckRevisionId", "deckName", "archetype", "opponent", "review"]) next.delete(key); return next })
        setPage(1)
      }}
      pagination={
        data
          ? {
              pageIndex: data.page - 1,
              pageSize: data.pageSize,
              pageCount: Math.max(1, data.totalPages),
              totalCount: data.totalCount,
              hasPreviousPage: data.page > 1,
              hasNextPage: data.page < data.totalPages,
              onPageIndexChange: index => setPage(index + 1),
              onPageSizeChange: size => { setPageSize(size); setPage(1) },
            }
          : null
      }
    />
    {undo && <OpponentClassificationNotice count={undo.length} pending={pending} error={undoError}
      onUndo={() => void undoClassification()} onDismiss={() => setUndo(null)} />}
    </>
  )
}
