/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useState, useMemo, useCallback } from "react"
import { useGamesHistory } from "@/hooks/use-games"
import { useGames } from "@/hooks/use-games"
import { useClientState } from "@/hooks/use-client-state"
import { useNDJSONStream } from "@/hooks/use-ndjson-stream"
import { getApiUrl } from "@/utils/api-config"
import { useNavigate } from "react-router-dom"
import {
  HistoryLayout,
  compareFormats,
  isLimitedFormat,
  type GameType,
  type MatchHistoryItem,
} from "@videreproject/ui"
import type { DateRange } from "react-day-picker"

function getHistoryRowHref(match: MatchHistoryItem): string | null {
  if (match.isEvent) return `/events/${match.eventId}`
  return `/history/${match.id}`
}

export default function History() {
  const navigate = useNavigate()
  const [dateRange, setDateRange] = useState<DateRange | undefined>(() => {
    const to = new Date()
    const from = new Date()
    from.setDate(to.getDate() - 30)
    return { from, to }
  })

  const [selectedFormat, setSelectedFormat] = useState<string>("")
  const [gameType, setGameType] = useState<GameType>("All")
  const [page, setPage] = useState(1)
  const pageSize = 50

  // The format list is shared with the dashboard and deck filters.
  const { formats } = useGames("ALL", "")

  const filteredFormats = useMemo(() => {
    let result = formats
    if (gameType === "Limited") {
      result = formats.filter(isLimitedFormat)
    } else if (gameType === "Constructed") {
      result = formats.filter(format => !isLimitedFormat(format))
    }

    return [...result].sort(compareFormats)
  }, [formats, gameType])

  const effectiveRange = dateRange || "ALL"
  const { data, loading, error } = useGamesHistory(page, pageSize, effectiveRange, selectedFormat)
  const [liveItems, setLiveItems] = useState<MatchHistoryItem[]>([])
  const { isReady: clientReady } = useClientState()

  // Subscribe to event/match creation/completion SSE stream
  const onSSEMessage = useCallback((dto: MatchHistoryItem) => {
    setLiveItems(prev => {
      if (dto.isEvent) {
        const idx = prev.findIndex(m => m.isEvent && m.eventId === dto.eventId)
        if (idx !== -1) {
          const updated = [...prev]
          updated[idx] = dto
          return updated
        }
        return [dto, ...prev]
      }

      const parentIdx = prev.findIndex(m => m.isEvent && m.eventId === dto.eventId)
      if (parentIdx !== -1) {
        const updated = [...prev]
        const parent = { ...updated[parentIdx] }
        const children = [...(parent.matches || [])]
        const childIdx = children.findIndex(c => c.id === dto.id)
        if (childIdx !== -1) {
          children[childIdx] = dto
        } else {
          children.push(dto)
        }
        parent.matches = children
        if (children.some(c => c.isActive)) {
          parent.isActive = true
          parent.result = "In Progress"
        }
        updated[parentIdx] = parent
        return updated
      }

      const idx = prev.findIndex(m => m.id === dto.id)
      if (idx !== -1) {
        const updated = [...prev]
        updated[idx] = dto
        return updated
      }
      return [dto, ...prev]
    })
  }, [])

  useNDJSONStream<MatchHistoryItem>({
    url: getApiUrl("/api/games/history/watch"),
    onMessage: onSSEMessage,
    enabled: clientReady,
    autoReconnect: true,
    reconnectDelay: 2000,
    maxReconnectAttempts: 0,
    useConstantRetry: true,
  })

  const mergedItems = useMemo(() => {
    const fetched = data?.items || []
    const fetchedEventIds = new Set(fetched.map((m: MatchHistoryItem) => m.eventId))
    const fetchedMatchIds = new Set(fetched.map((m: MatchHistoryItem) => m.id))

    const newLive = liveItems.filter(m => {
      if (m.isEvent) return !fetchedEventIds.has(m.eventId)
      return !fetchedMatchIds.has(m.id)
    })
    return [...newLive, ...fetched]
  }, [data, liveItems])

  const handleFormatSelect = (f: string) => {
    setSelectedFormat(f)
    setPage(1)
    setLiveItems([])
  }

  const handleGameTypeSelect = (value: GameType) => {
    setGameType(value)
    if (
      selectedFormat &&
      value !== "All" &&
      (value === "Limited") !== isLimitedFormat(selectedFormat)
    ) {
      handleFormatSelect("")
    } else {
      setPage(1)
      setLiveItems([])
    }
  }

  const handleDateChange = (range: DateRange | undefined) => {
    setDateRange(range)
    setPage(1)
    setLiveItems([])
  }

  const handleRowClick = useCallback(
    (match: MatchHistoryItem) => {
      const href = getHistoryRowHref(match)
      if (href) navigate(href)
    },
    [navigate],
  )

  return (
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
      pagination={
        data
          ? {
              page: data.page,
              totalPages: data.totalPages,
              totalCount: data.totalCount,
            }
          : null
      }
      onPreviousPage={() => setPage(p => Math.max(1, p - 1))}
      onNextPage={() => setPage(p => Math.min(data?.totalPages ?? p, p + 1))}
    />
  )
}
