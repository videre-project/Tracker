/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useEffect, useRef, useState } from "react"
import { Link } from "react-router-dom"
import { Swords, Trophy, Target, Calendar } from "lucide-react"
import { Badge, SidebarGroup, SidebarGroupLabel, useSidebar } from "@videreproject/ui"
import type { PersonalActivityDTO } from "@/types/api"
import { getApiUrl } from "@/utils/api-config"
import { MatchupDeckLabel } from "./matchup-deck-label"
import { normalizeFormatName } from "@/utils/event-format"
import { ActionLabel } from "./action-label"
import { activityClockProgress } from "@/utils/activity-clock"

const viewActionClassName = "inline-flex shrink-0 items-center gap-1 rounded text-xs leading-4 text-foreground outline-none transition-colors hover:text-primary focus-visible:ring-1 focus-visible:ring-ring disabled:opacity-50"
const viewAction = <ActionLabel>View</ActionLabel>

function ActivityProgress({ item }: { item: PersonalActivityDTO }) {
  const league = item.kind === "league"
  const total = (item.totalRounds ?? 0) + (item.totalRounds && !league && item.hasPlayoffs ? 3 : 0)
  const wins = Math.max(0, item.wins ?? 0)
  const losses = Math.max(0, item.losses ?? 0)
  const completed = Math.min(total, league ? wins + losses : item.completedRounds ?? 0)
  const current = item.status === "active" && completed < total && (league ? !!item.match : !!item.roundInProgress)
  const clock = league ? item.match?.clock : item.roundClock
  const [now, setNow] = useState(Date.now)
  const running = current && !!clock?.running
  useEffect(() => {
    if (!running) return
    const timer = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(timer)
  }, [running])
  if (total <= 0) return null
  const fraction = current ? activityClockProgress(clock, now) : null
  const filledTicks = Math.floor((fraction ?? 0) * 10)
  const unit = league ? "matches" : "rounds"
  const description = `${completed} of ${total} ${unit} completed${league ? `; ${wins} ${wins === 1 ? "win" : "wins"}, ${losses} ${losses === 1 ? "loss" : "losses"}` : ""}${current ? `; ${league ? "match" : "round"} in progress` : ""}${fraction != null ? `; ${Math.round(fraction * 100)}% of clock used` : ""}`
  return <div className="pointer-events-none absolute -inset-px overflow-hidden rounded-[inherit]">
    <div role="progressbar" aria-label={`${item.name} progress`}
      aria-valuemin={0} aria-valuemax={total} aria-valuenow={completed + (fraction ?? 0)} aria-valuetext={description}
      className="absolute inset-x-0 bottom-0 grid h-1 gap-px"
      style={{ gridTemplateColumns: `repeat(${total}, minmax(0, 1fr))` }}>
      {Array.from({ length: total }, (_, index) => current && index === completed
        ? <span key={index} data-activity-clock-fill aria-hidden="true" className="grid min-w-0 grid-cols-10 gap-px">
          {Array.from({ length: 10 }, (_, tick) => <span key={tick}
            className={tick < filledTicks ? "bg-foreground/90" : "bg-foreground/15"} />)}
        </span>
        : <span key={index} className={
          league && index < wins ? "bg-emerald-500"
            : league && index < completed ? "bg-rose-500"
            : index < completed ? "bg-foreground/60"
            : "bg-sidebar-accent/70"
        } />)}
    </div>
  </div>
}

function ActivityTiming({ item, showDuration = false, separator = false }: {
  item: PersonalActivityDTO; showDuration?: boolean; separator?: boolean
}) {
  const [now, setNow] = useState(Date.now)
  useEffect(() => {
    if (!item.roundEndTime) return
    const timer = setInterval(() => setNow(Date.now()), 1000)
    return () => clearInterval(timer)
  }, [item.roundEndTime])
  const end = item.roundEndTime ? new Date(item.roundEndTime).getTime() : NaN
  const remaining = Math.ceil((end - now) / 1000)
  if (remaining > 0) return <>
    {separator && <span aria-hidden="true" className="mx-1 text-sidebar-foreground/50">•</span>}
    <span className="tabular-nums">{Math.floor(remaining / 60)}m {remaining % 60}s left</span>
  </>
  if (item.status === "registered" && item.startTime) {
    const date = new Date(item.startTime)
    if (!Number.isNaN(date.getTime())) {
      const today = new Date(now)
      const tomorrow = new Date(today)
      tomorrow.setDate(tomorrow.getDate() + 1)
      const time = date.toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" })
      const label = date.toDateString() === today.toDateString() ? `Starts at ${time}`
        : date.toDateString() === tomorrow.toDateString() ? `Tomorrow at ${time}`
        : `${date.toLocaleDateString(undefined, { weekday: "short", month: "short", day: "numeric" })} at ${time}`
      const minutes = item.endTime ? Math.floor((new Date(item.endTime).getTime() - date.getTime()) / 60000) : 0
      const duration = minutes > 0 ? `${Math.floor(minutes / 60) ? `${Math.floor(minutes / 60)}h` : ""}${minutes % 60 ? ` ${minutes % 60}m` : ""}`.trim() : null
      return <>
        <time dateTime={item.startTime}>{label}</time>
        {showDuration && duration && <><span aria-hidden="true" className="mx-1 text-sidebar-foreground/50">•</span><span className="text-sidebar-foreground/60">{duration}</span></>}
      </>
    }
  }
  return null
}

function ActivityDeck({ item }: { item: PersonalActivityDTO }) {
  if (!item.deck && !item.deckName) return null
  return <div role="group" aria-label="Registered deck" className="-mx-2 my-2 min-w-0 rounded-md bg-black/5 p-2 dark:bg-black/20">
    <MatchupDeckLabel deck={item.deck ?? { name: item.deckName ?? undefined }} />
  </div>
}

function ActivityFooter({ item, href, opening, onOpen }: {
  item: PersonalActivityDTO; href: string | null; opening: boolean; onOpen: () => void
}) {
  const match = item.match
  const matchActionLabel = match?.status === "Sideboarding" ? match.status
    : match?.gameNumber != null ? `Game ${match.gameNumber}` : "View match"
  const actionLabel = `View ${item.name}${match && (item.kind === "league" || item.kind === "match") ? " match" : ""}`
  const action = href
    ? <Link to={href} aria-label={actionLabel} className={viewActionClassName}>{viewAction}</Link>
    : <button type="button" onClick={onOpen} disabled={opening} aria-label={`View ${item.name} in MTGO`} className={viewActionClassName}>{viewAction}</button>
  if (match) return <Link to={`/history/${match.id}`} aria-label={`Open match against ${match.opponent ?? "unknown opponent"}, ${matchActionLabel}`}
    className="group/match-action flex min-w-0 items-center justify-between gap-3 rounded text-xs outline-none focus-visible:ring-1 focus-visible:ring-ring">
    <span className="truncate text-sidebar-foreground/70 group-hover/match-action:text-sidebar-accent-foreground">
      {match.opponent ? `vs ${match.opponent}` : "Current match"}
    </span>
    <span className="shrink-0 text-foreground transition-colors group-hover/match-action:text-primary">
      <ActionLabel>{matchActionLabel}</ActionLabel>
    </span>
  </Link>
  return <div className="flex items-center justify-between gap-2 text-xs">
    <div className="flex min-w-0 items-center text-sidebar-foreground/80">
      {item.status === "registered" && item.startTime
        ? <span className="truncate"><ActivityTiming item={item} showDuration /></span>
        : <>
          <span className="truncate">{item.stateLabel !== "Registered" ? item.stateLabel : null}</span>
          <span className="shrink-0 text-sidebar-foreground/60 empty:hidden"><ActivityTiming item={item} separator /></span>
        </>}
    </div>
    {action}
  </div>
}

function RegularEventCard({ item, opening, error, onOpen }: {
  item: PersonalActivityDTO
  opening: boolean
  error: string | null
  onOpen: () => void
}) {
  const href = item.kind === "tournament"
    ? `/events/${item.eventId}`
    : null
  const Icon = item.isDraft ? Calendar : Trophy
  const progress = item.status === "active" && item.roundNumber
    ? `Round ${item.roundNumber}${item.totalRounds ? `/${item.totalRounds}` : ""}`
    : item.totalRounds ? `${item.totalRounds} rounds` : null
  return <article aria-label={item.name ?? undefined} data-activity-id={item.id}
    className="relative min-w-0 shrink-0 rounded-md border border-sidebar-border/60 bg-sidebar-accent/20 p-3 transition-colors hover:bg-sidebar-accent/40 focus-within:bg-sidebar-accent/40">
    <div className="mb-1">
      <div className="mb-1 flex items-center gap-1.5">
        <Icon className="h-3 w-3 shrink-0 text-sidebar-foreground/60" />
        <h3 className="min-w-0 truncate text-sm font-semibold text-sidebar-foreground">
          {href ? <Link to={href} className="block truncate rounded outline-none hover:text-sidebar-accent-foreground focus-visible:ring-1 focus-visible:ring-ring">{item.name}</Link>
            : <button type="button" onClick={onOpen} disabled={opening} className="block w-full truncate rounded text-left outline-none hover:text-sidebar-accent-foreground focus-visible:ring-1 focus-visible:ring-ring disabled:opacity-50">{item.name}</button>}
        </h3>
      </div>
      <p className="text-xs text-sidebar-foreground/70">
        {normalizeFormatName(item.format)}
        {progress && <><span aria-hidden="true" className="mx-1 text-sidebar-foreground/50">•</span><span>{progress}</span></>}
        {(item.inPlayoffs || item.status === "registered" && item.hasPlayoffs) && <span className="text-sidebar-foreground/50" style={{ fontSize: "var(--caption-font-size)" }}> {item.inPlayoffs ? "(in top 8)" : "(with top 8)"}</span>}
      </p>
    </div>
    <ActivityDeck item={item} />
    <ActivityFooter item={item} href={href} opening={opening} onOpen={onOpen} />
    {error && <p role="alert" className="mt-2 text-xs text-destructive">{error}</p>}
    <ActivityProgress item={item} />
  </article>
}

function ActivityCard({ item }: { item: PersonalActivityDTO }) {
  const [opening, setOpening] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const Icon = item.kind === "match" ? Swords : item.kind === "league" ? Target : item.kind === "tournament" ? Trophy : Calendar
  const regularEvent = item.kind === "tournament" || item.kind === "queue"
  const href = item.kind === "tournament" ? `/events/${item.eventId}`
    : item.kind === "match" && item.match ? `/history/${item.match.id}` : null
  const viewHref = item.match ? `/history/${item.match.id}` : href
  async function openInMTGO() {
    setOpening(true)
    setError(null)
    try {
      const response = await fetch(getApiUrl(`/api/events/openevent/${item.eventId}`), { method: "POST" })
      if (!response.ok) throw new Error("Could not open this event in MTGO.")
    } catch (error) {
      setError(error instanceof Error ? error.message : "Could not open this event in MTGO.")
    } finally { setOpening(false) }
  }
  if (regularEvent) return <RegularEventCard item={item} opening={opening} error={error} onOpen={openInMTGO} />
  return <article aria-label={item.name ?? undefined} data-activity-id={item.id}
    className="relative min-w-0 shrink-0 rounded-md border border-sidebar-border/60 bg-sidebar-accent/20 px-3 py-3 text-xs transition-colors hover:bg-sidebar-accent/40 focus-within:bg-sidebar-accent/40">
    <div className="mb-1 flex items-start justify-between gap-2">
      <div className="min-w-0 flex-1">
        <div className="mb-1 flex min-w-0 items-center gap-1.5">
          <Icon className="h-3 w-3 shrink-0 text-sidebar-foreground/60" />
          <h3 className="min-w-0 truncate text-sm font-semibold text-sidebar-foreground">
            {href ? <Link to={href} className="block truncate rounded outline-none transition-colors hover:text-sidebar-accent-foreground focus-visible:ring-1 focus-visible:ring-ring">{item.name}</Link>
              : <button type="button" onClick={openInMTGO} disabled={opening} aria-label={`Open ${item.name} in MTGO`}
                  className="block w-full truncate rounded text-left outline-none transition-colors hover:text-sidebar-accent-foreground focus-visible:ring-1 focus-visible:ring-ring disabled:opacity-50">
                  {item.name}
                </button>}
          </h3>
        </div>
      </div>
      {item.wins != null && item.losses != null && <Badge variant="secondary" className="shrink-0 rounded-md tabular-nums">{item.wins}–{item.losses}</Badge>}
    </div>
    <ActivityDeck item={item} />
    <ActivityFooter item={item} href={viewHref} opening={opening} onOpen={openInMTGO} />
    {error && <p role="alert" className="mt-2 text-destructive">{error}</p>}
    <ActivityProgress item={item} />
  </article>
}

export function ActivityList({ label, items, loading, error, connected, emptyMessage }: {
  label: string
  items: PersonalActivityDTO[]
  loading: boolean
  error: string | null
  connected: boolean
  emptyMessage: string
}) {
  const { state } = useSidebar()
  const scrollRef = useRef<HTMLDivElement>(null)
  const [faded, setFaded] = useState(false)
  useEffect(() => {
    const element = scrollRef.current
    if (!element) return
    const update = () => setFaded(element.scrollHeight - element.scrollTop - element.clientHeight > 2)
    const observer = new ResizeObserver(update)
    observer.observe(element)
    if (element.firstElementChild) observer.observe(element.firstElementChild)
    element.addEventListener("scroll", update, { passive: true })
    update()
    return () => { observer.disconnect(); element.removeEventListener("scroll", update) }
  }, [items, state])
  if (state === "collapsed") return null
  return <SidebarGroup className={`min-h-0 ${items.length ? "basis-0" : "shrink-0"}`}
    style={items.length ? { flexGrow: items.length } : undefined}>
    <SidebarGroupLabel className="shrink-0">{label} Events – {items.length}</SidebarGroupLabel>
    <div className="relative min-h-0 flex-1">
      <div ref={scrollRef} className="h-full overflow-x-hidden overflow-y-auto overscroll-contain pr-1">
        <div role="list" aria-label={label} className="space-y-2 pb-2"
          style={{ width: "calc(var(--sidebar-width, 236px) - 20px + 1px)" }}>
          {items.map(item => <div role="listitem" key={item.id}><ActivityCard item={item} /></div>)}
          {!items.length && <p role={error ? "status" : undefined} className="shrink-0 rounded-md border border-sidebar-border/40 bg-sidebar-accent/10 p-4 text-center text-xs text-sidebar-foreground/60">
            {loading ? "Loading your activity…" : !connected ? "Connect MTGO to view your activity." : error ?? emptyMessage}
          </p>}
        </div>
      </div>
      {faded && <div className="pointer-events-none absolute inset-x-0 bottom-0 h-8 bg-gradient-to-t from-sidebar to-transparent" />}
    </div>
  </SidebarGroup>
}
