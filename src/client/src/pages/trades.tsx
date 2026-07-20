"use client"

import { useEffect, useRef, useState } from "react"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Skeleton } from "@/components/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { useTradePosts, useTrades } from "@/hooks/use-trades"
import { TradeHistoryView } from "@/components/trades/trade-history"
import type {
  TradePartner,
  TradePost,
  TradePostFormatFilter,
} from "@/hooks/use-trades"
import { cn } from "@/lib/utils"
import { HighlightedText } from "@/utils/highlighted-text"
import { GameLogText } from "@/utils/parse-game-log"
import {
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
  Handshake,
  History,
  Loader2,
  Package,
  Search,
  Users,
  X,
} from "lucide-react"

const MARKETPLACE_PAGE_SIZE = 20

function EmptyState({ children }: { children: string }) {
  return (
    <div className="flex min-h-24 items-center justify-center rounded-md bg-muted/10 px-4 py-8 text-sm text-muted-foreground">
      {children}
    </div>
  )
}

function TradeMessage({
  message,
  className,
  highlightText,
}: {
  message: string
  className?: string
  highlightText?: string
}) {
  return (
    <GameLogText
      text={message}
      className={className}
      manaSymbolClassName="inline h-3.5 w-3.5 align-text-bottom mx-px"
      highlightText={highlightText}
    />
  )
}

function formatTradePostStatus(post: TradePost | null | undefined) {
  if (!post) return "None"
  return post.format || "Active"
}

function formatTradePartner(partner: TradePartner | null | undefined) {
  if (!partner) return "None"
  if (partner.posterName) return partner.posterName

  return "Unknown partner"
}

function StatCard({
  label,
  value,
  icon: Icon,
}: {
  label: string
  value: number | string
  icon: typeof Package
}) {
  return (
    <Card className="border-sidebar-border/60 bg-card/80">
      <CardContent className="flex min-h-12 items-center gap-3 px-3 py-2">
        <span className="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-md bg-muted/35 text-muted-foreground">
          <Icon className="h-3.5 w-3.5" />
        </span>
        <div className="min-w-0">
          <p className="text-xs leading-4 text-muted-foreground">{label}</p>
          <p className="truncate text-sm font-semibold leading-5" title={String(value)}>
            {value}
          </p>
        </div>
      </CardContent>
    </Card>
  )
}

function StatCardSkeleton({ icon: Icon }: { icon: typeof Package }) {
  return (
    <Card className="border-sidebar-border/60 bg-card/80">
      <CardContent className="flex min-h-12 items-center gap-3 px-3 py-2">
        <span className="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-md bg-muted/35">
          <Icon className="h-3.5 w-3.5 text-muted-foreground/60" />
        </span>
        <div className="min-w-0 flex-1 space-y-1">
          <Skeleton className="h-3 w-20" />
          <Skeleton className="h-4 w-24 max-w-full" />
        </div>
      </CardContent>
    </Card>
  )
}

function TradePartnersSkeleton() {
  return (
    <section className="space-y-3">

      <div className="grid gap-2 md:grid-cols-3">
        <StatCardSkeleton icon={Users} />
        <StatCardSkeleton icon={Handshake} />
        <StatCardSkeleton icon={Package} />
      </div>
      <div className="flex flex-wrap gap-2">
        <Skeleton className="h-6 w-24 rounded-full" />
        <Skeleton className="h-6 w-32 rounded-full" />
        <Skeleton className="h-6 w-20 rounded-full" />
        <Skeleton className="h-6 w-28 rounded-full" />
      </div>
    </section>
  )
}

function MarketplaceTableHeader() {
  return (
    <Table className="table-fixed" wrapperClassName="overflow-hidden">
      <TableHeader>
        <TableRow>
          <TableHead className="w-44">Poster</TableHead>
          <TableHead className="w-36">Format</TableHead>
          <TableHead>Message</TableHead>
        </TableRow>
      </TableHeader>
    </Table>
  )
}

function MarketplaceTableSkeleton({
  rows = MARKETPLACE_PAGE_SIZE,
  className,
}: {
  rows?: number
  className?: string
}) {
  const rowWidths = [
    ["w-24", "w-28", "w-11/12"],
    ["w-32", "w-20", "w-4/5"],
    ["w-20", "w-28", "w-10/12"],
    ["w-28", "w-20", "w-3/4"],
    ["w-24", "w-28", "w-5/6"],
  ]

  return (
    <div className={cn("flex min-h-0 flex-col overflow-hidden rounded-md bg-muted/10", className)}>
      <MarketplaceTableHeader />
      <div className="min-h-0 flex-1 overflow-y-auto overflow-x-hidden">
        <Table className="table-fixed" wrapperClassName="overflow-visible">
          <TableBody>
            {Array.from({ length: rows }).map((_, index) => {
              const widths = rowWidths[index % rowWidths.length]
              return (
                <TableRow key={index}>
                  <TableCell className="w-44">
                    <Skeleton className={`h-5 ${widths[0]}`} />
                  </TableCell>
                  <TableCell className="w-36">
                    <Skeleton className={`h-5 ${widths[1]}`} />
                  </TableCell>
                  <TableCell>
                    <Skeleton className={`h-5 ${widths[2]}`} />
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </Table>
      </div>
    </div>
  )
}

function MarketplaceSkeleton() {
  return (
    <section className="flex min-h-0 flex-1 flex-col gap-3">
      <div className="grid gap-2 lg:grid-cols-[180px_160px_minmax(190px,1fr)_96px]">
        <Skeleton className="h-9" />
        <Skeleton className="h-9" />
        <Skeleton className="h-9" />
        <Skeleton className="h-9" />
      </div>
      <MarketplaceTableSkeleton className="min-h-0 flex-1" />
      <div className="flex shrink-0 items-center justify-end gap-6 px-2">
        <Skeleton className="h-4 w-[110px]" />
        <div className="flex items-center space-x-2">
          <Skeleton className="hidden h-8 w-8 lg:block" />
          <Skeleton className="h-8 w-8" />
          <Skeleton className="h-8 w-8" />
          <Skeleton className="hidden h-8 w-8 lg:block" />
        </div>
      </div>
    </section>
  )
}

function TradesPageSkeleton() {
  return <MarketplaceSkeleton />
}

type TradeView = "marketplace" | "history" | "partners"

function TradeViewTabs({
  value,
  onValueChange,
}: {
  value: TradeView
  onValueChange: (value: TradeView) => void
}) {
  const tabs = [
    { value: "marketplace", label: "Marketplace", icon: Package },
    { value: "partners", label: "Trade Partners", icon: Users },
    { value: "history", label: "Trade History", icon: History },
  ] as const

  return (
    <div
      aria-label="Trade view"
      className="inline-flex h-10 shrink-0 items-stretch gap-1"
      role="tablist"
    >
      {tabs.map(({ value: tabValue, label, icon: Icon }) => (
        <button
          aria-selected={value === tabValue}
          className={cn(
            "-mb-px inline-flex h-10 items-center gap-2 border-b-2 px-4 text-sm font-medium transition-colors",
            value === tabValue
              ? "border-primary text-foreground"
              : "border-transparent text-muted-foreground hover:bg-muted/30 hover:text-foreground"
          )}
          key={tabValue}
          onClick={() => onValueChange(tabValue)}
          role="tab"
          type="button"
        >
          <Icon className="h-4 w-4" />
          {label}
        </button>
      ))}
    </div>
  )
}
function useDebouncedValue<T>(value: T, delayMs: number) {
  const [debouncedValue, setDebouncedValue] = useState(value)

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedValue(value), delayMs)
    return () => window.clearTimeout(timer)
  }, [value, delayMs])

  return debouncedValue
}

export default function Trades() {
  const pageSize = MARKETPLACE_PAGE_SIZE
  const marketplaceRowsRef = useRef<HTMLDivElement>(null)
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

  const allPosts = postData?.posts ?? []
  const tradePartners = data?.tradePartners ?? []
  const currentTrade = data?.currentTrade ?? null
  const lastTradePartner = formatTradePartner(tradePartners[0])
  const filtersActive =
    postFormat !== "all" ||
    userSearch.trim().length > 0 ||
    messageSearch.trim().length > 0
  const showPageSkeleton = activeView === "marketplace" && (!clientReady || (loading && !data))

  useEffect(() => {
    setPostsPage(1)
  }, [postFormat, debouncedUserSearch, debouncedMessageSearch])

  useEffect(() => {
    if (postData && marketplaceRowsRef.current) {
      marketplaceRowsRef.current.scrollTop = 0
    }
  }, [postData])

  const clearPostFilters = () => {
    setPostFormat("all")
    setUserSearch("")
    setMessageSearch("")
  }

  return (
    <div className="flex h-[calc(100vh-2.5rem)] min-h-0 min-w-0 flex-col gap-3 overflow-hidden px-4 pb-4 pt-1">
      <div className="flex h-10 shrink-0 items-center justify-between gap-3 border-b border-sidebar-border/70">
        <TradeViewTabs value={activeView} onValueChange={setActiveView} />
        {activeView === "marketplace" && postsLoading && (
          <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />
        )}
      </div>
      {activeView !== "history" && !showPageSkeleton && error && (
        <div className="rounded-md bg-destructive/15 px-4 py-3 text-sm font-medium text-destructive">
          Error loading trades: {error}
        </div>
      )}

      {activeView === "marketplace" && !showPageSkeleton && postsError && (
        <div className="rounded-md bg-destructive/15 px-4 py-3 text-sm font-medium text-destructive">
          Error loading trade posts: {postsError}
        </div>
      )}

      <div className={cn(
        "flex min-h-0 min-w-0 flex-1 flex-col",
        activeView !== "history" && "hidden"
      )}>
        <TradeHistoryView />
      </div>
      {activeView !== "history" && (
        activeView === "partners" ? (
        loading && !data ? (
          <TradePartnersSkeleton />
        ) : (
          <section className="min-h-0 flex-1 space-y-3 overflow-y-auto">
            <div className="grid gap-2 md:grid-cols-3">
              <StatCard label="Last Trade" value={lastTradePartner} icon={Users} />
              <StatCard
                label="Current Trade"
                value={currentTrade ? "Active" : "None"}
                icon={Handshake}
              />
              <StatCard
                label="Trade Post"
                value={formatTradePostStatus(data?.myPost)}
                icon={Package}
              />
            </div>
            {tradePartners.length === 0 ? (
              <EmptyState>No previous trade partners are currently available.</EmptyState>
            ) : (
              <div className="flex flex-wrap gap-2">
                {tradePartners.map((partner, index) => (
                  <Badge
                    className="rounded-md"
                    key={`${formatTradePartner(partner)}-${partner.lastTradeTime ?? index}`}
                    variant="outline"
                  >
                    {formatTradePartner(partner)}
                  </Badge>
                ))}
              </div>
            )}
          </section>
        )
      ) : showPageSkeleton ? (
        <TradesPageSkeleton />
      ) : (
        <>
          <section className="flex min-h-0 flex-1 flex-col gap-3">
            <div className="grid gap-2 lg:grid-cols-[180px_160px_minmax(190px,1fr)_96px]">
              <div className="relative">
                <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                <Input
                  value={userSearch}
                  onChange={event => setUserSearch(event.target.value)}
                  disabled={!clientReady}
                  placeholder="Search users"
                  className="h-9 pl-9"
                />
              </div>
              <Select
                value={postFormat}
                onValueChange={value => setPostFormat(value as TradePostFormatFilter)}
                disabled={!clientReady}
              >
                <SelectTrigger className="h-9">
                  <SelectValue placeholder="Post type" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="all">All post types</SelectItem>
                  <SelectItem value="message">Message posts</SelectItem>
                  <SelectItem value="offeredWantedList">Wanted/offered lists</SelectItem>
                </SelectContent>
              </Select>
              <div className="relative">
                <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
                <Input
                  value={messageSearch}
                  onChange={event => setMessageSearch(event.target.value)}
                  disabled={!clientReady}
                  placeholder="Search messages"
                  className="h-9 pl-9"
                />
              </div>
              <Button
                variant="outline"
                size="sm"
                onClick={clearPostFilters}
                disabled={!filtersActive || postsLoading}
                className="h-9 gap-2"
              >
                <X className="h-4 w-4" />
                Clear
              </Button>
            </div>
            {postsLoading && !postData ? (
              <MarketplaceTableSkeleton className="min-h-0 flex-1" />
            ) : allPosts.length === 0 ? (
              <div className="flex min-h-0 flex-1 items-center justify-center rounded-md bg-muted/10 px-4 py-8 text-sm text-muted-foreground">
                <span>
                  {filtersActive
                    ? "No marketplace posts match the current filters."
                    : "No marketplace posts are currently available."}
                </span>
              </div>
            ) : (
              <div className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-md bg-muted/10">
                <MarketplaceTableHeader />
                <div
                  ref={marketplaceRowsRef}
                  className="min-h-0 flex-1 overflow-y-auto overflow-x-hidden"
                >
                  <Table className="table-fixed" wrapperClassName="overflow-visible">
                    <TableBody>
                      {allPosts.map((post, index) => (
                        <TableRow key={`${post.posterName}-${index}`}>
                          <TableCell className="w-44 font-medium">
                            <HighlightedText
                              text={post.posterName || "Unknown"}
                              highlight={debouncedUserSearch}
                            />
                          </TableCell>
                          <TableCell className="w-36 font-medium text-foreground/85">
                            {post.format || "-"}
                          </TableCell>
                          <TableCell
                            className="max-w-0 truncate whitespace-nowrap align-middle text-muted-foreground"
                            title={post.message}
                          >
                            {post.message ? (
                              <TradeMessage
                                message={post.message}
                                className="block truncate whitespace-nowrap leading-5"
                                highlightText={debouncedMessageSearch}
                              />
                            ) : (
                              "-"
                            )}
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
              </div>
            )}
            <div className="flex shrink-0 items-center justify-end gap-6 px-2">
              <div className="flex min-w-[110px] items-center justify-center text-sm font-medium">
                Page {postData?.page ?? postsPage} of {postData?.totalPages ?? 1}
              </div>
              <div className="flex items-center space-x-2">
                <Button
                  variant="outline"
                  className="hidden h-8 w-8 p-0 lg:flex"
                  onClick={() => setPostsPage(1)}
                  disabled={!postData?.hasPreviousPage || postsLoading}
                >
                  <span className="sr-only">Go to first page</span>
                  <ChevronsLeft />
                </Button>
                <Button
                  variant="outline"
                  className="h-8 w-8 p-0"
                  onClick={() => setPostsPage(page => Math.max(1, page - 1))}
                  disabled={!postData?.hasPreviousPage || postsLoading}
                >
                  <span className="sr-only">Go to previous page</span>
                  <ChevronLeft />
                </Button>
                <Button
                  variant="outline"
                  className="h-8 w-8 p-0"
                  onClick={() => setPostsPage(page => page + 1)}
                  disabled={!postData?.hasNextPage || postsLoading}
                >
                  <span className="sr-only">Go to next page</span>
                  <ChevronRight />
                </Button>
                <Button
                  variant="outline"
                  className="hidden h-8 w-8 p-0 lg:flex"
                  onClick={() => setPostsPage(postData?.totalPages ?? 1)}
                  disabled={!postData?.hasNextPage || postsLoading}
                >
                  <span className="sr-only">Go to last page</span>
                  <ChevronsRight />
                </Button>
              </div>
            </div>
          </section>
        </>
        )
      )}
    </div>
  )
}
