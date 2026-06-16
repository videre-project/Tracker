"use client"

import { useEffect, useState } from "react"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
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
import type {
  TradePartner,
  TradePost,
  TradePostFormatFilter,
} from "@/hooks/use-trades"
import { HighlightedText } from "@/utils/highlighted-text"
import { GameLogText } from "@/utils/parse-game-log"
import {
  ChevronLeft,
  ChevronRight,
  ChevronsLeft,
  ChevronsRight,
  Handshake,
  Loader2,
  Package,
  Search,
  Users,
  X,
} from "lucide-react"

function EmptyState({ children }: { children: string }) {
  return (
    <div className="flex min-h-24 items-center justify-center rounded-md border border-dashed border-sidebar-border/60 px-4 py-8 text-sm text-muted-foreground">
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
    <Card className="border-sidebar-border/60">
      <CardContent className="flex items-center justify-between p-4">
        <div>
          <p className="text-xs text-muted-foreground">{label}</p>
          <p className="text-2xl font-semibold">{value}</p>
        </div>
        <Icon className="h-5 w-5 text-muted-foreground" />
      </CardContent>
    </Card>
  )
}

function StatCardSkeleton({ icon: Icon }: { icon: typeof Package }) {
  return (
    <Card className="border-sidebar-border/60">
      <CardContent className="flex items-center justify-between p-4">
        <div className="space-y-2">
          <Skeleton className="h-3 w-24" />
          <Skeleton className="h-8 w-28" />
        </div>
        <Icon className="h-5 w-5 text-muted-foreground/60" />
      </CardContent>
    </Card>
  )
}

function TradePartnersSkeleton() {
  return (
    <Card className="border-sidebar-border/60">
      <CardHeader className="p-4">
        <CardTitle className="flex items-center gap-2 text-base">
          <Users className="h-4 w-4" />
          Trade Partners
        </CardTitle>
      </CardHeader>
      <CardContent className="p-4 pt-0">
        <div className="flex flex-wrap gap-2">
          <Skeleton className="h-6 w-24 rounded-full" />
          <Skeleton className="h-6 w-32 rounded-full" />
          <Skeleton className="h-6 w-20 rounded-full" />
          <Skeleton className="h-6 w-28 rounded-full" />
        </div>
      </CardContent>
    </Card>
  )
}

function MarketplaceTableSkeleton({ rows = 10 }: { rows?: number }) {
  const rowWidths = [
    ["w-24", "w-28", "w-11/12"],
    ["w-32", "w-20", "w-4/5"],
    ["w-20", "w-28", "w-10/12"],
    ["w-28", "w-20", "w-3/4"],
    ["w-24", "w-28", "w-5/6"],
  ]

  return (
    <div className="rounded-md border border-sidebar-border/60">
      <Table className="table-fixed" wrapperClassName="overflow-visible">
        <TableHeader>
          <TableRow className="bg-muted/50">
            <TableHead className="w-44">Poster</TableHead>
            <TableHead className="w-36">Format</TableHead>
            <TableHead>Message</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {Array.from({ length: rows }).map((_, index) => {
            const widths = rowWidths[index % rowWidths.length]
            return (
              <TableRow key={index}>
                <TableCell>
                  <Skeleton className={`h-4 ${widths[0]}`} />
                </TableCell>
                <TableCell>
                  <Skeleton className={`h-5 ${widths[1]} rounded-full`} />
                </TableCell>
                <TableCell>
                  <Skeleton className={`h-4 ${widths[2]}`} />
                </TableCell>
              </TableRow>
            )
          })}
        </TableBody>
      </Table>
    </div>
  )
}

function MarketplaceSkeleton() {
  return (
    <Card className="border-sidebar-border/60">
      <CardHeader className="p-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <CardTitle className="flex items-center gap-2 text-base">
            <Package className="h-4 w-4" />
            Marketplace
          </CardTitle>
        </div>
      </CardHeader>
      <CardContent className="p-4 pt-0">
        <div className="mb-4 grid gap-3 lg:grid-cols-[180px_160px_minmax(190px,1fr)_96px]">
          <Skeleton className="h-9" />
          <Skeleton className="h-9" />
          <Skeleton className="h-9" />
          <Skeleton className="h-9" />
        </div>
        <MarketplaceTableSkeleton />
        <div className="mt-4 flex items-center justify-end gap-6 px-2">
          <Skeleton className="h-4 w-[110px]" />
          <div className="flex items-center space-x-2">
            <Skeleton className="hidden h-8 w-8 lg:block" />
            <Skeleton className="h-8 w-8" />
            <Skeleton className="h-8 w-8" />
            <Skeleton className="hidden h-8 w-8 lg:block" />
          </div>
        </div>
      </CardContent>
    </Card>
  )
}

function TradesPageSkeleton() {
  return (
    <>
      <div className="grid gap-4 md:grid-cols-3">
        <StatCardSkeleton icon={Users} />
        <StatCardSkeleton icon={Handshake} />
        <StatCardSkeleton icon={Package} />
      </div>
      <TradePartnersSkeleton />
      <MarketplaceSkeleton />
    </>
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
  const pageSize = 10
  const [postsPage, setPostsPage] = useState(1)
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
  const showPageSkeleton = !clientReady || (loading && !data)

  useEffect(() => {
    setPostsPage(1)
  }, [postFormat, debouncedUserSearch, debouncedMessageSearch])

  const clearPostFilters = () => {
    setPostFormat("all")
    setUserSearch("")
    setMessageSearch("")
  }

  return (
    <div className="flex min-h-full min-w-0 flex-col gap-4 p-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-bold">Trades</h1>
        </div>
      </div>

      {!showPageSkeleton && error && (
        <div className="rounded-md bg-destructive/15 px-4 py-3 text-sm font-medium text-destructive">
          Error loading trades: {error}
        </div>
      )}

      {!showPageSkeleton && postsError && (
        <div className="rounded-md bg-destructive/15 px-4 py-3 text-sm font-medium text-destructive">
          Error loading trade posts: {postsError}
        </div>
      )}

      {showPageSkeleton ? (
        <TradesPageSkeleton />
      ) : (
        <>
          <div className="grid gap-4 md:grid-cols-3">
            <StatCard label="Last Trade" value={lastTradePartner} icon={Users} />
            <StatCard
              label="Current Trade"
              value={currentTrade ? "Active" : "None"}
              icon={Handshake}
            />
            <StatCard label="Trade Post" value={formatTradePostStatus(data?.myPost)} icon={Package} />
          </div>

          <Card className="border-sidebar-border/60">
            <CardHeader className="p-4">
              <CardTitle className="flex items-center gap-2 text-base">
                <Users className="h-4 w-4" />
                Trade Partners
              </CardTitle>
            </CardHeader>
            <CardContent className="p-4 pt-0">
              {tradePartners.length === 0 ? (
                <EmptyState>No previous trade partners are currently available.</EmptyState>
              ) : (
                <div className="flex flex-wrap gap-2">
                  {tradePartners.map((partner, index) => (
                    <Badge
                      key={`${formatTradePartner(partner)}-${partner.lastTradeTime ?? index}`}
                      variant="outline"
                    >
                      {formatTradePartner(partner)}
                    </Badge>
                  ))}
                </div>
              )}
            </CardContent>
          </Card>

          <Card className="border-sidebar-border/60">
            <CardHeader className="p-4">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <CardTitle className="flex items-center gap-2 text-base">
                  <Package className="h-4 w-4" />
                  {postData
                    ? `Marketplace - ${postData.totalCount.toLocaleString()} Posts`
                    : "Marketplace"}
                </CardTitle>
                <div className="flex h-8 items-center">
                  {postsLoading && <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />}
                </div>
              </div>
            </CardHeader>
            <CardContent className="p-4 pt-0">
              <div className="mb-4 grid gap-3 lg:grid-cols-[180px_160px_minmax(190px,1fr)_96px]">
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
                <MarketplaceTableSkeleton />
              ) : allPosts.length === 0 ? (
                <EmptyState>
                  {filtersActive
                    ? "No marketplace posts match the current filters."
                    : "No marketplace posts are currently available."}
                </EmptyState>
              ) : (
                <div className="rounded-md border border-sidebar-border/60">
                  <Table className="table-fixed" wrapperClassName="overflow-visible">
                    <TableHeader>
                      <TableRow className="bg-muted/50">
                        <TableHead className="w-44">Poster</TableHead>
                        <TableHead className="w-36">Format</TableHead>
                        <TableHead>Message</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {allPosts.map((post, index) => (
                        <TableRow key={`${post.posterName}-${index}`}>
                          <TableCell className="font-medium">
                            <HighlightedText
                              text={post.posterName || "Unknown"}
                              highlight={debouncedUserSearch}
                            />
                          </TableCell>
                          <TableCell>
                            {post.format ? <Badge variant="outline">{post.format}</Badge> : "-"}
                          </TableCell>
                          <TableCell
                            className="max-w-0 truncate whitespace-nowrap align-middle text-muted-foreground"
                            title={post.message}
                          >
                            {post.message ? (
                              <TradeMessage
                                message={post.message}
                                className="block truncate whitespace-nowrap leading-6"
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
              )}
              <div className="mt-4 flex items-center justify-end gap-6 px-2">
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
            </CardContent>
          </Card>
        </>
      )}
    </div>
  )
}
