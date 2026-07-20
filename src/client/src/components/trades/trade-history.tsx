import { useEffect, useState } from "react"

import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
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
import {
  useTradeHistory,
  useTradeHistoryDetail,
  type TradeHistoryItem,
  type TradeHistorySummary,
} from "@/hooks/use-trade-history"
import { cn } from "@/lib/utils"
import {
  AlertTriangle,
  ArrowDownToLine,
  ArrowUpFromLine,
  Loader2,
  MessageSquare,
  ReceiptText,
  Search,
  X,
} from "lucide-react"

function formatTimestamp(value?: string) {
  if (!value) return "\u2014"
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(new Date(value))
}

function titleForTrade(trade: TradeHistorySummary) {
  if (trade.kind === "Player") return trade.partnerName || "Player trade"
  return "Product escrow"
}

function attributionLabel(status: TradeHistorySummary["attributionStatus"]) {
  switch (status) {
    case "Inferred": return "Outputs inferred"
    case "InferredAmbiguous": return "Outputs may include other changes"
    case "Unavailable": return "Outputs unavailable"
    case "Pending": return "Waiting for collection"
    default: return null
  }
}

function formatResult(result: TradeHistorySummary["result"]) {
  return result.replace(/([a-z])([A-Z])/g, "$1 $2")
}

function ResultBadge({ result }: { result: TradeHistorySummary["result"] }) {
  const variant = result === "Completed"
    ? "success"
    : result === "Failed"
      ? "destructive"
      : "secondary"

  return (
    <Badge
      variant={variant}
      className={cn(
        "rounded-md capitalize",
        result === "InProgress" &&
          "border-yellow-500/30 bg-yellow-500/15 text-yellow-700 dark:text-yellow-400"
      )}
    >
      {formatResult(result)}
    </Badge>
  )
}

function EffectSummary({
  quantity,
  catalogCount,
  direction,
}: {
  quantity: number
  catalogCount: number
  direction: "out" | "in"
}) {
  if (quantity <= 0 || catalogCount <= 0) {
    return <span className="text-muted-foreground">{"\u2014"}</span>
  }

  const Icon = direction === "out" ? ArrowUpFromLine : ArrowDownToLine
  const sign = direction === "out" ? "-" : "+"
  return (
    <span
      className="inline-flex items-center gap-1.5 whitespace-nowrap"
      title={`${quantity.toLocaleString()} items across ${catalogCount.toLocaleString()} catalog entries`}
    >
      <Icon className="h-3.5 w-3.5 text-muted-foreground" />
      <span className="font-medium tabular-nums">
        {sign}{quantity.toLocaleString()}
      </span>
      <span className="text-xs text-muted-foreground">
        {"\u00b7"} {catalogCount.toLocaleString()} {catalogCount === 1 ? "entry" : "entries"}
      </span>
    </span>
  )
}

function ItemList({
  title,
  items,
  direction,
}: {
  title: string
  items: TradeHistoryItem[]
  direction: "out" | "in"
}) {
  const Icon = direction === "out" ? ArrowUpFromLine : ArrowDownToLine
  return (
    <section className="space-y-2">
      <div className="flex items-center gap-2 text-sm font-medium">
        <Icon className="h-4 w-4 text-muted-foreground" />
        {title}
      </div>
      {items.length === 0 ? (
        <p className="rounded-md bg-muted/15 px-3 py-2 text-sm text-muted-foreground">
          No items recorded.
        </p>
      ) : (
        <div className="rounded-md border border-sidebar-border/60">
          {items.map(item => (
            <div
              key={`${item.role}-${item.catalogId}`}
              className="flex items-center justify-between border-b px-3 py-2 text-sm last:border-b-0"
            >
              <span>Catalog {item.catalogId}</span>
              <span className="font-medium">{item.quantity.toLocaleString()}</span>
            </div>
          ))}
        </div>
      )}
    </section>
  )
}

function HistoryListSkeleton() {
  return (
    <div className="space-y-1 p-2">
      {Array.from({ length: 8 }).map((_, index) => (
        <Skeleton key={index} className="h-12 w-full" />
      ))}
    </div>
  )
}

function useDebouncedValue<T>(value: T, delayMs: number) {
  const [debouncedValue, setDebouncedValue] = useState(value)

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedValue(value), delayMs)
    return () => window.clearTimeout(timer)
  }, [delayMs, value])

  return debouncedValue
}

export function TradeHistoryView() {
  const [search, setSearch] = useState("")
  const [kind, setKind] = useState<"all" | TradeHistorySummary["kind"]>("all")
  const [result, setResult] = useState<"all" | TradeHistorySummary["result"]>("all")
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const debouncedSearch = useDebouncedValue(search, 350)
  const history = useTradeHistory({
    search: debouncedSearch,
    kind: kind === "all" ? undefined : kind,
    result: result === "all" ? undefined : result,
  })
  const detail = useTradeHistoryDetail(selectedId)
  const selected = detail.data?.summary.id === selectedId ? detail.data : null
  const outgoing = selected?.items.filter(item => item.role === "LocalOffer") ?? []
  const incoming = selected?.items.filter(item =>
    item.role === (selected.summary.kind === "Player" ? "RemoteOffer" : "InferredOutput")) ?? []

  useEffect(() => {
    if (history.items.length === 0) {
      setSelectedId(null)
    } else if (selectedId == null ||
               !history.items.some(item => item.id === selectedId)) {
      setSelectedId(history.items[0].id)
    }
  }, [history.items, selectedId])

  const filtersActive =
    search.trim().length > 0 || kind !== "all" || result !== "all"
  const clearFilters = () => {
    setSearch("")
    setKind("all")
    setResult("all")
  }

  return (
    <section className="flex min-h-0 flex-1 flex-col gap-3">

      {history.error && (
        <div className="rounded-md bg-destructive/15 px-4 py-3 text-sm text-destructive">
          Error loading trade history: {history.error}
        </div>
      )}

      <div className="grid min-h-0 flex-1 gap-3 overflow-y-auto lg:grid-cols-[minmax(0,3fr)_minmax(22rem,2fr)] lg:overflow-visible">
        <div className="flex min-h-[22rem] min-w-0 flex-col gap-3 lg:min-h-0">
          <div className="relative z-10 flex flex-wrap gap-2">
            <div className="relative min-w-[12rem] flex-1">
              <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
              <Input
                value={search}
                onChange={event => setSearch(event.target.value)}
                placeholder="Search partner or escrow"
                className="h-9 pl-9"
              />
            </div>
            <Select
              value={kind}
              onValueChange={value => setKind(value as "all" | TradeHistorySummary["kind"])}
            >
              <SelectTrigger className="h-9 w-36">
                <SelectValue placeholder="Trade type" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All trade types</SelectItem>
                <SelectItem value="Player">Player trades</SelectItem>
                <SelectItem value="NonPlayer">Product escrows</SelectItem>
              </SelectContent>
            </Select>
            <Select
              value={result}
              onValueChange={value => setResult(value as "all" | TradeHistorySummary["result"])}
            >
              <SelectTrigger className="h-9 w-40">
                <SelectValue placeholder="Result" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="all">All results</SelectItem>
                <SelectItem value="Completed">Completed</SelectItem>
                <SelectItem value="InProgress">In progress</SelectItem>
                <SelectItem value="Cancelled">Cancelled</SelectItem>
                <SelectItem value="Failed">Failed</SelectItem>
                <SelectItem value="ClosedUnknown">Closed unknown</SelectItem>
                <SelectItem value="Interrupted">Interrupted</SelectItem>
              </SelectContent>
            </Select>
            <Button
              variant="outline"
              size="sm"
              onClick={clearFilters}
              disabled={!filtersActive || history.loading}
              aria-label="Clear trade history filters"
              title="Clear filters"
              className="h-9 w-9 shrink-0 gap-2 px-0 2xl:w-auto 2xl:px-3"
            >
              <X className="h-4 w-4" />
              <span className="hidden 2xl:inline">Clear</span>
            </Button>
          </div>
          <div className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-md bg-muted/10">
          <div className="min-h-0 flex-1 overflow-auto">
            {history.loading && history.items.length === 0 ? (
              <HistoryListSkeleton />
            ) : history.items.length === 0 ? (
              <div className="flex h-full min-h-40 items-center justify-center px-4 text-sm text-muted-foreground">
                No trade escrows have been recorded yet.
              </div>
            ) : (
              <Table>
                <TableHeader className="sticky top-0 z-10 bg-background">
                  <TableRow>
                    <TableHead>Trade</TableHead>
                    <TableHead>Started</TableHead>
                    <TableHead>Given</TableHead>
                    <TableHead>Received</TableHead>
                    <TableHead>Result</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {history.items.map(trade => {
                    const isSelected = trade.id === selectedId
                    return (
                      <TableRow
                        aria-selected={isSelected}
                        className={cn(
                          "cursor-pointer outline-none focus-visible:ring-1 focus-visible:ring-inset focus-visible:ring-ring",
                          isSelected && "bg-muted/45 hover:bg-muted/55"
                        )}
                        key={trade.id}
                        onClick={() => setSelectedId(trade.id)}
                        onKeyDown={event => {
                          if (event.key === "Enter" || event.key === " ") {
                            event.preventDefault()
                            setSelectedId(trade.id)
                          }
                        }}
                        tabIndex={0}
                      >
                        <TableCell>
                          <div className="font-medium">{titleForTrade(trade)}</div>
                          <div className="text-xs text-muted-foreground">
                            {trade.kind === "Player" ? "Player" : "Non-player"}
                            {trade.escrowId ? ` \u00b7 Escrow ${trade.escrowId}` : ""}
                          </div>
                        </TableCell>
                        <TableCell className="whitespace-nowrap">
                          {formatTimestamp(trade.startedAt)}
                        </TableCell>
                        <TableCell>
                          <EffectSummary
                            quantity={trade.outgoingQuantity}
                            catalogCount={trade.outgoingCatalogCount}
                            direction="out"
                          />
                        </TableCell>
                        <TableCell>
                          <EffectSummary
                            quantity={trade.incomingQuantity}
                            catalogCount={trade.incomingCatalogCount}
                            direction="in"
                          />
                        </TableCell>
                        <TableCell>
                          <ResultBadge result={trade.result} />
                        </TableCell>
                      </TableRow>
                    )
                  })}
                </TableBody>
              </Table>
            )}
          </div>
          {history.hasMore && (
            <div className="shrink-0 border-t border-sidebar-border/60 p-3 text-center">
              <Button
                variant="outline"
                onClick={() => void history.loadMore()}
                disabled={history.loadingMore}
                size="sm"
              >
                {history.loadingMore && <Loader2 className="h-4 w-4 animate-spin" />}
                Load older trades
              </Button>
            </div>
          )}
          </div>
        </div>

        <Card className="flex min-h-[22rem] min-w-0 flex-col overflow-hidden border-sidebar-border/60 bg-card/80 lg:min-h-0">
          {detail.loading && !selected ? (
            <div className="space-y-4 p-4">
              <Skeleton className="h-6 w-2/3" />
              <Skeleton className="h-4 w-1/2" />
              <Skeleton className="h-24 w-full" />
              <Skeleton className="h-32 w-full" />
            </div>
          ) : detail.error ? (
            <div className="p-4 text-sm text-destructive">{detail.error}</div>
          ) : selected ? (
            <>
              <CardHeader className="shrink-0 border-b border-sidebar-border/60 px-4 py-3">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <CardTitle className="truncate text-base">
                      {titleForTrade(selected.summary)}
                    </CardTitle>
                    <CardDescription>
                      {formatTimestamp(selected.summary.startedAt)} {"\u00b7"} {formatResult(selected.summary.result)}
                    </CardDescription>
                  </div>
                  <Badge variant="secondary" className="shrink-0 rounded-md">
                    {selected.summary.kind === "Player" ? "Player trade" : "Product"}
                  </Badge>
                </div>
              </CardHeader>
              <CardContent className="min-h-0 flex-1 space-y-5 overflow-y-auto p-4">
                <dl className="grid grid-cols-2 gap-x-4 gap-y-3 rounded-md bg-muted/15 p-3 text-sm">
                  <div>
                    <dt className="text-xs text-muted-foreground">State</dt>
                    <dd className="font-medium">{selected.summary.stateName || selected.summary.state}</dd>
                  </div>
                  <div>
                    <dt className="text-xs text-muted-foreground">Escrow</dt>
                    <dd className="font-medium">{selected.summary.escrowId || "Not assigned"}</dd>
                  </div>
                  <div>
                    <dt className="text-xs text-muted-foreground">Started</dt>
                    <dd>{formatTimestamp(selected.summary.startedAt)}</dd>
                  </div>
                  <div>
                    <dt className="text-xs text-muted-foreground">Closed</dt>
                    <dd>{formatTimestamp(selected.summary.closedAt)}</dd>
                  </div>
                </dl>

                {attributionLabel(selected.summary.attributionStatus) && (
                  <div className="flex gap-2 rounded-md bg-muted/30 px-3 py-2 text-sm text-muted-foreground">
                    <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
                    {attributionLabel(selected.summary.attributionStatus)}
                  </div>
                )}

                <ItemList
                  title={selected.summary.kind === "Player" ? "Given" : "Consumed"}
                  items={outgoing}
                  direction="out"
                />
                <ItemList
                  title={selected.summary.kind === "Player" ? "Received" : "Inferred outputs"}
                  items={incoming}
                  direction="in"
                />

                {selected.errors.length > 0 && (
                  <section className="space-y-2">
                    <h3 className="flex items-center gap-2 text-sm font-medium">
                      <AlertTriangle className="h-4 w-4 text-destructive" />
                      Errors
                    </h3>
                    {selected.errors.map(error => (
                      <div key={error.id} className="rounded-md bg-destructive/10 px-3 py-2 text-sm">
                        {error.errorName || `Trade error ${error.errorCode}`}
                      </div>
                    ))}
                  </section>
                )}

                {selected.messages.length > 0 && (
                  <section className="space-y-2">
                    <h3 className="flex items-center gap-2 text-sm font-medium">
                      <MessageSquare className="h-4 w-4 text-muted-foreground" />
                      Chat
                    </h3>
                    <div className="space-y-2 rounded-md border border-sidebar-border/60 p-3">
                      {selected.messages.map(message => (
                        <div key={message.id} className="text-sm">
                          <span className="font-medium">{message.senderName || "MTGO"}: </span>
                          <span className="text-muted-foreground">{message.text}</span>
                        </div>
                      ))}
                    </div>
                  </section>
                )}
              </CardContent>
            </>
          ) : (
            <div className="flex h-full min-h-40 flex-col items-center justify-center gap-2 px-6 text-center text-sm text-muted-foreground">
              <ReceiptText className="h-8 w-8 opacity-50" />
              <p>Select a recorded escrow to inspect its items, chat, and errors.</p>
            </div>
          )}
        </Card>
      </div>
    </section>
  )
}
