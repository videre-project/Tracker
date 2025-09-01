import * as React from "react"
import { Play, Pause, Clock, Square, Trophy, Target, Calendar } from "lucide-react"
import { NavLink } from "react-router-dom"

import {
  SidebarContent,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  useSidebar,
} from "@/components/ui/sidebar"

type EventType = "league" | "swiss" | "elimination" | "draft" | "unknown"
type GameStatus = "active" | "paused" | "scheduled" | "completed"

export interface ActiveGame {
  id: string
  name: string
  type: EventType
  status: GameStatus
  format: string
  url: string
  deck?: string
  // League-specific
  wins?: number
  losses?: number
  totalRounds?: number
  // Prelim-specific
  currentRound?: number
  totalSwissRounds?: number
  // Draft-specific
  pod?: string
  // Timing
  startTime?: string
  endTime?: string
  timeRemaining?: string
}


import { useActiveGames, useUpcomingGames } from '@/hooks/use-events'

const getStatusConfig = (status: GameStatus) => {
  switch (status) {
    case "active":
      return {
        icon: Play,
        badgeClass: "text-green-400 bg-green-900/50",
      }
    case "paused":
      return {
        icon: Pause,
        badgeClass: "text-yellow-400 bg-yellow-900/50",
      }
    case "scheduled":
      return {
        icon: Clock,
        badgeClass: "text-blue-400 bg-blue-900/50",
      }
    case "completed":
      return {
        icon: Square,
        badgeClass: "text-red-400 bg-red-900/50",
      }
  }
}

const getEventTypeIcon = (type: EventType) => {
  switch (type) {
    case "league":
      return Target
    case "swiss":
      return Trophy
    case "elimination":
      return Trophy
    case "draft":
      return Calendar
  }
}

const getRecordDisplay = (game: ActiveGame) => {
  if (game.wins !== undefined && game.losses !== undefined) {
    if (game.status === "completed") {
      return game.wins === 0 ? `${game.wins}-${game.losses} Drop` : `${game.wins}-${game.losses}`
    }
    return `${game.wins}-${game.losses}`
  }
  return null
}

const getProgressDisplay = (game: ActiveGame) => {
  switch (game.type) {
    case "league":
      if (game.totalRounds && game.wins !== undefined && game.losses !== undefined) {
        const played = game.wins + game.losses
        return `${played}/${game.totalRounds} rounds`
      }
      break
    case "swiss":
      if (game.currentRound && game.totalSwissRounds) {
        return `Round ${game.currentRound}/${game.totalSwissRounds}`
      }
      break
    case "draft":
      if (game.pod) {
        return game.pod
      }
      break
  }
  return null
}

interface GameListProps {
  label: string
  games: ActiveGame[]
  className?: string
}

export function GameList({ label, games, className }: GameListProps) {
  const { state } = useSidebar()
  const isCollapsed = state === "collapsed"
  const [showGradient, setShowGradient] = React.useState(true)
  const scrollContainerRef = React.useRef<HTMLDivElement>(null)

  const handleScroll = React.useCallback(() => {
    if (!scrollContainerRef.current) return

    const { scrollTop, scrollHeight, clientHeight } = scrollContainerRef.current
    const isAtBottom = scrollTop + clientHeight >= scrollHeight - 1

    setShowGradient(!isAtBottom)
  }, [])

  React.useEffect(() => {
    const container = scrollContainerRef.current
    if (!container) return

    container.addEventListener('scroll', handleScroll)

    // Check initial state
    handleScroll()

    return () => container.removeEventListener('scroll', handleScroll);
  }, [handleScroll])

  if (isCollapsed) return null;

  return (
    <SidebarGroup className={`flex-1 min-h-0 pr-0 pb-0 ${className || ''}`} style={{ contain: 'layout' }}>
      <SidebarGroupLabel className="group-data-[collapsible=icon]:hidden w-[236px] flex-shrink-0">{label}</SidebarGroupLabel>
      <SidebarContent className="flex flex-col relative" style={{ contain: 'layout' }}>
        <SidebarGroup className="flex-1 min-h-0 -p-2 overflow-hidden" style={{ contain: 'layout' }}>
          <SidebarGroupContent
            ref={scrollContainerRef}
            className="space-y-2 overflow-y-auto overflow-x-hidden flex-1 min-h-0 pb-2 group-data-[collapsible=icon]:hidden"
            style={{ height: '100%', contain: 'layout' }}
          >
            {games.map((game) => {
              const statusConfig = getStatusConfig(game.status)
              const EventTypeIcon = getEventTypeIcon(game.type)
              const record = getRecordDisplay(game)
              const progress = getProgressDisplay(game)

              return (
                <div key={game.id} className="bg-sidebar-accent/20 p-3 rounded-md border border-sidebar-border/60 hover:bg-sidebar-accent/40 transition-colors w-[236px] flex-shrink-0">
                  <div className="flex justify-between items-start mb-1">
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-1.5 mb-1">
                        <EventTypeIcon className="w-3 h-3 text-sidebar-foreground/60 shrink-0" />
                        <h3 className="font-semibold text-sm text-sidebar-foreground truncate">
                          <NavLink
                            to={game.url}
                            className="hover:text-sidebar-accent-foreground transition-colors"
                          >
                            {game.name}
                          </NavLink>
                        </h3>
                      </div>
                      <p className="text-xs text-sidebar-foreground/70">
                        {game.format}
                        {game.deck && (
                          <>
                            <span className="text-sidebar-foreground/50 mx-1">•</span>
                            <span className="italic">{game.deck}</span>
                          </>
                        )}
                      </p>
                    </div>

                    {record && (
                      <span className={`text-xs font-medium px-2 py-0.5 rounded-full shrink-0 ml-2 ${statusConfig.badgeClass}`}>
                        {record}
                      </span>
                    )}
                  </div>

                  <div className="flex items-center justify-between text-xs">
                    <div className="flex items-center text-sidebar-foreground/80">
                      {game.status === "scheduled" && game.startTime ? (
                        <span>Starts {game.startTime}</span>
                      ) : progress ? (
                        <>
                          <span>{progress}</span>
                          {game.timeRemaining && game.status === "active" && (game.type === "swiss" || game.type === "elimination") && (
                            <>
                              <span className="text-sidebar-foreground/50 mx-1">•</span>
                              <span className="text-sidebar-foreground/60">{game.timeRemaining} left</span>
                            </>
                          )}
                        </>
                      ) : (
                        <span className="capitalize">{game.status}</span>
                      )}
                    </div>

                    <NavLink
                      to={game.url}
                      className="text-blue-400 hover:underline shrink-0"
                    >
                      View
                    </NavLink>
                  </div>
                </div>
              )
            })}
          </SidebarGroupContent>
        </SidebarGroup>
        {/* Gradient fade at bottom to indicate scrollable content - exclude scrollbar area */}
        {showGradient && (
          <div className="absolute bottom-0 left-0 right-2 h-8 pointer-events-none z-10"
               style={{
                 background: 'linear-gradient(to top, hsl(var(--sidebar-background)), transparent)'
               }} />
        )}
      </SidebarContent>
    </SidebarGroup>
  )
}

export function ActiveGames({ className }: { className?: string }) {
  const { games, loading, error } = useActiveGames();
  return <GameList label={`Active Events – ${games.length}`}
                   games={games}
                   className={className} />;
}

export function UpcomingGames({ className }: { className?: string }) {
  const { games, loading, error } = useUpcomingGames();
  return <GameList label={`Upcoming Events – ${games.length}`}
                   games={games}
                   className={className} />;
}
