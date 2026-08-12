/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useState, useEffect, useMemo } from "react"
import { Link } from "react-router-dom"
import type { DateRange } from "react-day-picker"

import {
  DashboardLayout,
  compareFormats,
  isLimitedFormat,
  type DashboardGameType,
} from "@videreproject/ui"
import { useGames } from "@/hooks/use-games"
import { useAggregatedArchetypes } from "@/hooks/use-decks"
import { useCardArtContext } from "@/hooks/use-card-art"

export default function Home() {
  const [dateRange, setDateRange] = useState<DateRange | undefined>(() => {
    const to = new Date()
    const from = new Date()
    from.setDate(to.getDate() - 30)
    return { from, to }
  })

  const effectiveRange = dateRange || "ALL"

  const [selectedFormat, setSelectedFormat] = useState<string>("")
  const [gameType, setGameType] = useState<DashboardGameType>("Constructed")

  const { formats, stats, trend: trendData, loading } = useGames(effectiveRange, selectedFormat)

  const filteredFormats = useMemo(() => {
    let result = formats
    if (gameType === "Limited") {
      result = formats.filter(isLimitedFormat)
    } else if (gameType === "Constructed") {
      result = formats.filter(format => !isLimitedFormat(format))
    }

    return [...result].sort(compareFormats)
  }, [formats, gameType])

  useEffect(() => {
    if (selectedFormat && !filteredFormats.includes(selectedFormat)) {
      setSelectedFormat("")
    }
  }, [gameType, filteredFormats, selectedFormat])

  const { archetypes, loading: archetypesLoading } = useAggregatedArchetypes(
    effectiveRange,
    selectedFormat || undefined,
  )
  const { getArtUrl, prefetchCards, isReady: clientReady } = useCardArtContext()

  useEffect(() => {
    if (archetypesLoading || archetypes.length === 0 || !clientReady) return
    const topCards = archetypes.slice(0, 10).map(a => a.topCard).filter(Boolean)
    prefetchCards(topCards)
  }, [clientReady, archetypesLoading, archetypes, prefetchCards])

  return (
    <DashboardLayout
      gameType={gameType}
      onGameTypeChange={setGameType}
      selectedFormat={selectedFormat}
      formats={filteredFormats}
      onFormatChange={setSelectedFormat}
      dateRange={dateRange}
      onDateRangeChange={setDateRange}
      stats={stats}
      loading={loading}
      trend={trendData}
      archetypes={archetypes}
      archetypesLoading={archetypesLoading}
      getArtUrl={getArtUrl}
      renderViewMoreDecks={({ children }) => <Link to="/decks">{children}</Link>}
    />
  )
}
