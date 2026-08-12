/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

"use client"

import { useEffect, useState } from "react"

import {
  TradeHistoryLayout,
  TradesLayout,
  type TradeEscrowKind,
  type TradeEscrowResult,
  type TradePostFormatFilter,
  type TradeView,
} from "@videreproject/ui"
import { useTradePosts, useTrades } from "@/hooks/use-trades"
import {
  useTradeHistory,
  useTradeHistoryDetail,
} from "@/hooks/use-trade-history"

function useDebouncedValue<T>(value: T, delayMs: number) {
  const [debouncedValue, setDebouncedValue] = useState(value)

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedValue(value), delayMs)
    return () => window.clearTimeout(timer)
  }, [value, delayMs])

  return debouncedValue
}

function TradeHistoryPanel() {
  const [search, setSearch] = useState("")
  const [kind, setKind] = useState<"all" | TradeEscrowKind>("all")
  const [result, setResult] = useState<"all" | TradeEscrowResult>("all")
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const debouncedSearch = useDebouncedValue(search, 350)

  const history = useTradeHistory({
    search: debouncedSearch,
    kind: kind === "all" ? undefined : kind,
    result: result === "all" ? undefined : result,
  })
  const detail = useTradeHistoryDetail(selectedId)

  useEffect(() => {
    if (history.items.length === 0) {
      setSelectedId(null)
    } else if (selectedId == null || !history.items.some(item => item.id === selectedId)) {
      setSelectedId(history.items[0].id)
    }
  }, [history.items, selectedId])

  return (
    <TradeHistoryLayout
      trades={history.items}
      selectedId={selectedId}
      detail={detail.data}
      onSelectedIdChange={setSelectedId}
      search={search}
      kind={kind}
      result={result}
      onSearchChange={setSearch}
      onKindChange={setKind}
      onResultChange={setResult}
      onClearFilters={() => {
        setSearch("")
        setKind("all")
        setResult("all")
      }}
      loading={history.loading}
      loadingMore={history.loadingMore}
      detailLoading={detail.loading}
      error={history.error}
      detailError={detail.error}
      hasMore={history.hasMore}
      onLoadMore={() => {
        void history.loadMore()
      }}
    />
  )
}

export default function Trades() {
  const pageSize = 20
  const [postsPage, setPostsPage] = useState(1)
  const [activeView, setActiveView] = useState<TradeView>("marketplace")
  const [postFormat, setPostFormat] = useState<TradePostFormatFilter>("all")
  const [userSearch, setUserSearch] = useState("")
  const [messageSearch, setMessageSearch] = useState("")

  const debouncedUserSearch = useDebouncedValue(userSearch, 350)
  const debouncedMessageSearch = useDebouncedValue(messageSearch, 350)
  const { data, loading, error, clientReady } = useTrades()
  const {
    data: postData,
    loading: postsLoading,
    error: postsError,
  } = useTradePosts(postsPage, pageSize, {
    format: postFormat,
    user: debouncedUserSearch,
    message: debouncedMessageSearch,
  })

  useEffect(() => {
    setPostsPage(1)
  }, [postFormat, debouncedUserSearch, debouncedMessageSearch])

  const clearPostFilters = () => {
    setPostFormat("all")
    setUserSearch("")
    setMessageSearch("")
  }

  return (
    <TradesLayout
      activeView={activeView}
      onActiveViewChange={setActiveView}
      historyContent={<TradeHistoryPanel />}
      posts={postData?.posts ?? []}
      postsLoading={postsLoading}
      postsError={postsError}
      postsPagination={
        postData
          ? {
              page: postData.page,
              totalPages: postData.totalPages,
              hasPreviousPage: postData.hasPreviousPage,
              hasNextPage: postData.hasNextPage,
            }
          : null
      }
      postsPage={postsPage}
      onPostsPageChange={setPostsPage}
      postFormat={postFormat}
      onPostFormatChange={setPostFormat}
      userSearch={userSearch}
      onUserSearchChange={setUserSearch}
      messageSearch={messageSearch}
      onMessageSearchChange={setMessageSearch}
      debouncedUserSearch={debouncedUserSearch}
      debouncedMessageSearch={debouncedMessageSearch}
      onClearPostFilters={clearPostFilters}
      tradePartners={data?.tradePartners ?? []}
      currentTrade={data?.currentTrade ?? null}
      myPost={data?.myPost ?? null}
      tradesLoading={loading}
      tradesError={error}
      hasTradesSnapshot={!!data}
      clientReady={clientReady}
    />
  )
}
