import { useCallback, useEffect, useRef, useState } from "react"
import { useNavigate } from "react-router-dom"
import { X, ArrowRight } from "lucide-react"
import { cn } from "@/lib/utils"
import { Button } from "@/components/ui/button"
import { getApiUrl } from "@/utils/api-config"
import { getFormatDotColor } from "@/utils/formats"
import type { ActiveGame } from "@/hooks/use-events"

function useEventDetails(eventId: string | null, enabled: boolean) {
  const [entryFee, setEntryFee] = useState<string | null>(null)
  const [prizes, setPrizes] = useState<Record<string, string> | null>(null)
  const [loading, setLoading] = useState(false)

  useEffect(() => {
    if (!eventId || !enabled) {
      setEntryFee(null)
      setPrizes(null)
      setLoading(false)
      return
    }

    setLoading(true)
    let cancelled = false

    Promise.all([
      fetch(getApiUrl(`/api/Events/GetEntryFee/${eventId}`))
        .then(r => r.ok ? r.text() : "—")
        .catch(() => "—"),
      fetch(getApiUrl(`/api/Events/GetPrizes/${eventId}`))
        .then(r => r.ok ? r.json() : null)
        .catch(() => null),
    ]).then(([fee, prz]) => {
      if (cancelled) return
      setEntryFee(fee)
      setPrizes(prz)
      setLoading(false)
    })

    return () => {
      cancelled = true
    }
  }, [eventId, enabled])

  return { entryFee, prizes, loading }
}

interface EventDetailPanelProps {
  event: ActiveGame | null
  loadDetails?: boolean
  onClose: () => void
}

export function EventDetailPanel({ event, loadDetails = true, onClose }: EventDetailPanelProps) {
  const navigate = useNavigate()
  const { entryFee, prizes, loading } = useEventDetails(event?.id ?? null, loadDetails)
  const detailsPending = Boolean(event && !loadDetails)

  if (!event) return null

  return (
    <div className="w-80 shrink-0 border-l border-border bg-background flex flex-col">
      {/* Header — fixed */}
      <div className="flex items-start justify-between gap-2 p-4 border-b border-border shrink-0">
        <div className="min-w-0">
          <h3 className="text-sm font-semibold truncate">{event.name}</h3>
          <div className="flex items-center gap-1.5 mt-1">
            <span className={cn("w-2 h-2 rounded-full shrink-0", getFormatDotColor(event.format))} />
            <span className="text-xs text-muted-foreground">{event.format}</span>
          </div>
        </div>
        <Button
          variant="ghost"
          size="sm"
          className="h-6 w-6 p-0 shrink-0"
          onClick={onClose}
        >
          <X className="h-3.5 w-3.5" />
        </Button>
      </div>

      {/* Event info — fixed */}
      <div className="shrink-0 p-4 space-y-4">
        <div className="grid grid-cols-2 gap-3 text-[13px]">
          <div>
            <div className="text-xs text-muted-foreground mb-0.5">Schedule</div>
            <div>{event.startTime ?? "—"} – {event.endTime ?? "—"}</div>
          </div>
          <div>
            <div className="text-xs text-muted-foreground mb-0.5">Players</div>
            <div>{event.totalPlayers ?? 0} / {event.minimumPlayers ?? 0}</div>
          </div>
          <div>
            <div className="text-xs text-muted-foreground mb-0.5">Rounds</div>
            <div>
              {event.totalSwissRounds || event.totalRounds || "—"}
              {(event.hasPlayoffs ||
                (event.eventStructure && typeof event.eventStructure === 'object' && event.eventStructure.hasPlayoffs) ||
                (event.totalSwissRounds && event.totalRounds && event.totalSwissRounds !== event.totalRounds)) ? (
                <span className="ml-1 text-xs text-muted-foreground font-normal">(with top 8)</span>
              ) : null}
            </div>
          </div>
          <div>
            <div className="text-xs text-muted-foreground mb-0.5">Entry Fee</div>
            <div>{loading ? "..." : entryFee ?? "—"}</div>
          </div>
        </div>
      </div>

      {/* Prizes — scrollable with bottom fade */}
      <PrizesScroll>
          {detailsPending || loading ? (
            <div className="text-xs text-muted-foreground">Loading...</div>
          ) : prizes && Object.keys(prizes).length > 0 ? (
            <div className="space-y-2">
              {Object.entries(prizes)
                .sort(([a], [b]) => {
                  // Sort by best record first: "4-0" > "3-1" > "2-2", "1st" < "2nd" < "3rd-4th"
                  const parseRank = (s: string) => {
                    const wl = s.match(/^(\d+)-(\d+)$/)
                    if (wl) return -parseInt(wl[1]) // negative wins = best first
                    const nth = s.match(/^(\d+)/)
                    if (nth) return parseInt(nth[1])
                    return 999
                  }
                  return parseRank(a) - parseRank(b)
                })
                .map(([bracket, prize]) => (
                <div key={bracket} className="flex gap-3 text-[13px]">
                  <span className="text-muted-foreground shrink-0 w-16">{bracket}</span>
                  <div className="space-y-0.5">
                    {prize.split(" / ").map((item, j) => (
                      <div key={j}>{item}</div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          ) : (
            <div className="text-xs text-muted-foreground">No prize data available</div>
          )}
      </PrizesScroll>

      {/* View tournament CTA */}
      <div className="shrink-0 px-4 py-4 border-t border-border">
        <Button
          variant="outline"
          size="sm"
          className="w-full gap-1.5 border-sidebar-border/60"
          onClick={() => navigate(`/events/${event.id}`)}
        >
          View Tournament
          <ArrowRight className="h-3.5 w-3.5" />
        </Button>
      </div>
    </div>
  )
}

function PrizesScroll({ children }: { children: React.ReactNode }) {
  const scrollRef = useRef<HTMLDivElement>(null)
  const [showFade, setShowFade] = useState(false)

  const checkFade = useCallback(() => {
    const el = scrollRef.current
    if (!el) return
    const hasMore = el.scrollHeight - el.scrollTop - el.clientHeight > 4
    setShowFade(hasMore)
  }, [])

  useEffect(() => {
    const el = scrollRef.current
    if (!el) return
    checkFade()
    el.addEventListener("scroll", checkFade, { passive: true })
    const ro = new ResizeObserver(checkFade)
    ro.observe(el)
    return () => { el.removeEventListener("scroll", checkFade); ro.disconnect() }
  }, [checkFade])

  return (
    <div className="relative flex-1 min-h-0">
      <div ref={scrollRef} className="h-full overflow-y-auto px-4 pb-4">
        {children}
      </div>
      {showFade && (
        <div
          className="absolute bottom-0 left-0 right-0 pointer-events-none"
          style={{
            height: 32,
            background: 'linear-gradient(to top, hsl(var(--background)), transparent)',
          }}
        />
      )}
    </div>
  )
}
