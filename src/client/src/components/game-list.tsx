import * as React from "react"
import { Play, Pause, Clock, Square, Trophy, Target, Calendar } from "lucide-react"

import {
  SidebarContent,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  useSidebar,
} from "@/components/ui/sidebar"

// Tournament state type (stand-in due to ASP.NET OpenAPI generation issues).
type TournamentState =
  | "NotSet"
  | "Fired"
  | "WaitingToStart"
  | "Drafting"
  | "Deckbuilding"
  | "DeckbuildingDeckSubmitted"
  | "WaitingForFirstRoundToStart"
  | "RoundInProgress"
  | "BetweenRounds"
  | "Finished"

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
  // Live round info (from ITournamentStateUpdate)
  inPlayoffs?: boolean
  roundEndTime?: string
  state?: TournamentState
}

// Hook to calculate live countdown from a target time
function useCountdown(targetTime?: string) {
  const [timeLeft, setTimeLeft] = React.useState<string | null>(null)

  React.useEffect(() => {
    if (!targetTime) {
      setTimeLeft(null)
      return
    }

    const calculateTimeLeft = () => {
      const now = Date.now()
      const end = new Date(targetTime).getTime()
      const ms = Math.max(0, end - now)

      if (ms === 0) {
        setTimeLeft(null)
        return
      }

      const totalSeconds = Math.floor(ms / 1000)
      const hours = Math.floor(totalSeconds / 3600)
      const minutes = Math.floor((totalSeconds % 3600) / 60)
      const seconds = totalSeconds % 60

      if (hours > 0) {
        return `${hours}h ${minutes}m ${seconds}s`
      } else if (minutes > 0) {
        return `${minutes}m ${seconds}s`
      } else {
        return `${seconds}s`
      }
    }

    // Initial calculation
    setTimeLeft(calculateTimeLeft())

    // Update every second
    const interval = setInterval(() => {
      setTimeLeft(calculateTimeLeft())
    }, 1000)

    return () => clearInterval(interval)
  }, [targetTime])

  return timeLeft
}

// Get a concise state label for different tournament states
function getStateLabel(state?: TournamentState): string | null {
  switch (state) {
    case "Drafting":
      return "Drafting"
    case "Deckbuilding":
    case "DeckbuildingDeckSubmitted":
      return "Deckbuilding"
    case "WaitingForFirstRoundToStart":
    case "BetweenRounds":
      return "Starting new round"
    case "RoundInProgress":
      return "Round in progress"
    case "WaitingToStart":
      return "Waiting to start"
    default:
      return null
  }
}

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

const getProgressDisplay = (game: ActiveGame & { eventStructure?: any }, isUpcoming = false) => {
  // Always check for totalSwissRounds or totalRounds for all event types
  const rounds = game.totalSwissRounds || game.totalRounds;
  // Try to get eventStructure from the game object (if present)
  const hasPlayoffs = game.eventStructure && typeof game.eventStructure === 'object' && game.eventStructure.hasPlayoffs;
  const eventStructureName = game.eventStructure && typeof game.eventStructure === 'object' ? game.eventStructure.name : null;

  if (rounds && rounds > 0) {
    if (isUpcoming) {
      if (hasPlayoffs) {
        return <>{rounds} rounds <span style={{ fontSize: 'var(--caption-font-size)' }} className="text-sidebar-foreground/50 align-baseline">(with top 8)</span></>;
      } else {
        return `${rounds} rounds`;
      }
    } else if (game.status === "active") {
      // For active events, show round info and playoffs badge
      const roundInfo = game.currentRound ? `Round ${game.currentRound}/${rounds}` : null;
      const playoffs = game.inPlayoffs ? <span className="ml-1 px-2 py-0.5 rounded-full bg-purple-900/40 text-purple-300 text-xs font-semibold">Top 8</span> : null;

      return <>
        {roundInfo && <span>{roundInfo}</span>}
        {playoffs}
      </>;
    } else if (game.currentRound) {
      if (hasPlayoffs) {
        return <>
          Round {game.currentRound}/{rounds} <span style={{ fontSize: 'var(--caption-font-size)' }} className="text-sidebar-foreground/50 align-baseline">(with top 8)</span>
        </>;
      } else {
        return `Round ${game.currentRound}/${rounds}`;
      }
    }
  }

  // If rounds is 0 but we have eventStructure info for upcoming events
  if (isUpcoming && eventStructureName) {
    // Show the event structure name (e.g., "Premier (with top 8)")
    return eventStructureName;
  }

  // For leagues
  if (game.type === "league" && game.totalRounds) {
    if (isUpcoming) {
      return `${game.totalRounds} matches`;
    } else if (game.wins !== undefined && game.losses !== undefined) {
      const played = game.wins + game.losses;
      return `${played}/${game.totalRounds} matches`;
    }
  }
  // For draft pod info
  if (game.type === "draft" && game.pod) {
    return game.pod;
  }
  return null;
}

// Component to display active game timing with live countdown
function ActiveGameTiming({ game }: { game: ActiveGame }) {
  const countdown = useCountdown(game.roundEndTime)
  const stateLabel = getStateLabel(game.state)

  if (!stateLabel && !countdown) {
    return null
  }

  return (
    <>
      {stateLabel && <span>{stateLabel}</span>}
      {stateLabel && countdown && <span className="text-sidebar-foreground/50 mx-1">•</span>}
      {countdown && <span className="text-sidebar-foreground/60">{countdown} left</span>}
    </>
  )
}

interface GameListProps {
  label: string
  games: ActiveGame[]
  className?: string
}

export function GameList({ label, games, className, placeholder, isUpcoming }: GameListProps & { placeholder?: React.ReactNode, isUpcoming?: boolean }) {
  const { state } = useSidebar()
  const isCollapsed = state === "collapsed"
  const [showGradient, setShowGradient] = React.useState(true)
  const scrollContainerRef = React.useRef<HTMLDivElement>(null)

  // Hijack scroll wheel and keyboard to snap to next/previous card, with a deadzone
  React.useEffect(() => {
    const container = scrollContainerRef.current;
    if (!container) return;

    // Helper: get all card elements
    const getCards = () => Array.from(container.querySelectorAll('[data-gamelist-card]')) as HTMLElement[];

    // Helper: find the index of the topmost visible card
    const getTopCardIndex = () => {
      const cards = getCards();
      const scrollTop = container.scrollTop;
      for (let i = 0; i < cards.length; i++) {
        if (cards[i].offsetTop + cards[i].offsetHeight/2 > scrollTop) {
          return i;
        }
      }
      return cards.length - 1;
    };

    // Deadzone logic: accumulate wheel delta, only trigger snap on threshold
    let wheelDeltaAccum = 0;
    const DEADZONE = 5; // px

    const snapToCard = (idx: number) => {
      const cards = getCards();
      if (cards.length === 0) return;
      idx = Math.max(0, Math.min(idx, cards.length - 1));
      const target = cards[idx];
      if (target) {
        container.scrollTo({ top: target.offsetTop, behavior: 'smooth' });
      }
    };

    const onWheel = (e: WheelEvent) => {
      // Only vertical scroll
      if (Math.abs(e.deltaY) < Math.abs(e.deltaX)) return;
      wheelDeltaAccum += e.deltaY;
      // Not enough scroll yet
      if (Math.abs(wheelDeltaAccum) < DEADZONE) return;
      e.preventDefault();
      const cards = getCards();
      if (cards.length === 0) return;
      let idx = getTopCardIndex();
      if (wheelDeltaAccum > 0) {
        // Scroll down
        idx = Math.min(idx + 1, cards.length - 1);
      } else if (wheelDeltaAccum < 0) {
        // Scroll up
        idx = Math.max(idx - 1, 0);
      }
      snapToCard(idx);
      // Reset after snapping
      wheelDeltaAccum = 0;
    };

    // Keyboard navigation
    const onKeyDown = (e: KeyboardEvent) => {
      // Only handle if container is focused or contains the active element
      if (document.activeElement !== container && !container.contains(document.activeElement)) return;
      const cards = getCards();
      if (cards.length === 0) return;
      let idx = getTopCardIndex();
      let handled = false;
      switch (e.key) {
        case 'ArrowDown':
          idx = Math.min(idx + 1, cards.length - 1);
          handled = true;
          break;
        case 'ArrowUp':
          idx = Math.max(idx - 1, 0);
          handled = true;
          break;
        case 'PageDown':
          idx = Math.min(idx + 3, cards.length - 1);
          handled = true;
          break;
        case 'PageUp':
          idx = Math.max(idx - 3, 0);
          handled = true;
          break;
        case 'Home':
          idx = 0;
          handled = true;
          break;
        case 'End':
          idx = cards.length - 1;
          handled = true;
          break;
      }
      if (handled) {
        e.preventDefault();
        snapToCard(idx);
      }
    };

    container.addEventListener('wheel', onWheel, { passive: false });
    window.addEventListener('keydown', onKeyDown, true);
    return () => {
      container.removeEventListener('wheel', onWheel);
      window.removeEventListener('keydown', onKeyDown, true);
    };
  }, []);

  const handleScroll = React.useCallback(() => {
    if (!scrollContainerRef.current) return;
    const { scrollTop, scrollHeight, clientHeight } = scrollContainerRef.current;
    const isAtBottom = scrollTop + clientHeight >= scrollHeight - 1;
    setShowGradient(!isAtBottom);
  }, []);

  React.useEffect(() => {
    const container = scrollContainerRef.current;
    if (!container) return;
    container.addEventListener('scroll', handleScroll);
    handleScroll();
    return () => container.removeEventListener('scroll', handleScroll);
  }, [handleScroll]);

  if (isCollapsed) return null;

  return (
    <SidebarGroup className={`flex-1 min-h-0 pr-0 pb-0 ${className || ''}`} style={{ contain: 'layout' }}>
      <SidebarGroupLabel
        className="group-data-[collapsible=icon]:hidden flex-shrink-0"
        style={{ width: 'calc(var(--sidebar-width, 236px) - 20px + 1px)' }}
      >
        {label}
      </SidebarGroupLabel>
      <SidebarContent className="flex flex-col relative" style={{ contain: 'layout' }}>
        <SidebarGroup className="flex-1 min-h-0 -p-2 overflow-hidden" style={{ contain: 'layout' }}>
          <SidebarGroupContent
            ref={scrollContainerRef}
            className="space-y-2 overflow-y-auto overflow-x-hidden flex-1 min-h-0 pb-2 group-data-[collapsible=icon]:hidden"
            style={{ height: '100%', contain: 'layout' }}
          >
            {games.length === 0 ? (
              placeholder || (
                <div
                  className="bg-sidebar-accent/10 p-4 rounded-md border border-sidebar-border/40 text-center text-xs text-sidebar-foreground/60 flex-shrink-0"
                  style={{ width: 'calc(var(--sidebar-width, 236px) - 20px + 1px)' }}
                >
                  No events to display.
                </div>
              )
            ) : (
              games.map((game) => {
                const statusConfig = getStatusConfig(game.status)
                const EventTypeIcon = getEventTypeIcon(game.type)
                const record = getRecordDisplay(game)
                const progress = getProgressDisplay(game, isUpcoming)

                return (
                  <div
                    key={game.id}
                    data-gamelist-card
                    className="bg-sidebar-accent/20 pl-3 pr-3 py-3 rounded-md border border-sidebar-border/60 hover:bg-sidebar-accent/40 transition-colors flex-shrink-0"
                    style={{ width: 'calc(var(--sidebar-width, 236px) - 20px + 1px)' }}
                  >
                    <div className="flex justify-between items-start mb-1">
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-1.5 mb-1">
                          <EventTypeIcon className="w-3 h-3 text-sidebar-foreground/60 shrink-0" />
                          <h3 className="font-semibold text-sm text-sidebar-foreground truncate">
                            <button
                              onClick={() => {
                                fetch(`/api/events/openevent/${game.id}`, { method: 'POST' })
                                  .catch(err => console.error('Failed to open event:', err));
                              }}
                              className="hover:text-sidebar-accent-foreground transition-colors cursor-pointer text-left w-full truncate"
                            >
                              {game.name}
                            </button>
                          </h3>
                        </div>
                        <p className="text-xs text-sidebar-foreground/70">
                          {/* For active events, show 'Format • Round X/Y' */}
                          {game.status === "active" ? (
                            <>
                              {game.format}
                              {game.currentRound && game.totalRounds && (
                                <>
                                  <span className="text-sidebar-foreground/50 mx-1">•</span>
                                  <span>{`Round ${game.currentRound}/${game.totalRounds}`}</span>
                                </>
                              )}
                              {game.inPlayoffs && (
                                <span style={{ fontSize: 'var(--caption-font-size)' }} className="text-sidebar-foreground/50 align-baseline"> (in top 8)</span>
                              )}
                            </>
                          ) : (
                            <>
                              {game.format}
                              {isUpcoming && progress && (
                                <>
                                  <span className="text-sidebar-foreground/50 mx-1">•</span>
                                  <span>{progress}</span>
                                </>
                              )}
                            </>
                          )}
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
                          <>
                            <span>
                              {"_rawStartTime" in game && game._rawStartTime ? (() => {
                                const start = new Date(String(game._rawStartTime));
                                if (isNaN(start.getTime())) return `Starts ${game.startTime}`;
                                const now = new Date();
                                const isToday = start.toDateString() === now.toDateString();
                                const isTomorrow = new Date(now.getTime() + 86400000).toDateString() === start.toDateString();

                                if (isToday) {
                                  return `Starts at ${game.startTime}`;
                                } else if (isTomorrow) {
                                  return `Tomorrow at ${game.startTime}`;
                                } else {
                                  // Show day, date and time (e.g., "Mon, Dec 8 at 2:00 PM")
                                  const dayStr = start.toLocaleDateString([], { weekday: 'short' });
                                  const dateStr = start.toLocaleDateString([], { month: 'short', day: 'numeric' });
                                  return `${dayStr}, ${dateStr} at ${game.startTime}`;
                                }
                              })() : `Starts ${game.startTime}`}
                            </span>
                            {"_rawStartTime" in game && "_rawEndTime" in game && game._rawStartTime && game._rawEndTime && (
                              <>
                                <span className="text-sidebar-foreground/50 mx-1">•</span>
                                <span className="text-sidebar-foreground/60">
                                  {(() => {
                                    const start = new Date(String(game._rawStartTime));
                                    const end = new Date(String(game._rawEndTime));
                                    if (isNaN(start.getTime()) || isNaN(end.getTime())) return null;
                                    let ms = end.getTime() - start.getTime();
                                    if (ms < 0) return null;
                                    const min = Math.floor(ms / 60000);
                                    const hr = Math.floor(min / 60);
                                    const remMin = min % 60;
                                    return hr > 0 ? `${hr}h${remMin > 0 ? ` ${remMin}m` : ''}` : `${remMin}m`;
                                  })()}
                                </span>
                              </>
                            )}
                          </>
                        ) : game.status === "active" ? (
                          <ActiveGameTiming game={game} />
                        ) : progress ? (
                          <span>{progress}</span>
                        ) : (
                          <span className="capitalize">{game.status}</span>
                        )}
                      </div>

                      <button
                        onClick={() => {
                          fetch(`/api/events/openevent/${game.id}`, { method: 'POST' })
                            .catch(err => console.error('Failed to open event:', err));
                        }}
                        className="text-blue-400 hover:underline shrink-0 cursor-pointer"
                      >
                        View
                      </button>
                    </div>
                  </div>
                )
              })
            )}
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

export function ActiveGames({ className, otherListEmpty, games }: { className?: string, otherListEmpty?: boolean, games: ActiveGame[] }) {
  // If the other list is empty, allocate more space
  const flexGrow = games.length === 0 && !otherListEmpty ? 'flex-1' : games.length > 0 && otherListEmpty ? 'flex-[2]' : 'flex-1';
  return <GameList label={`Active Events – ${games.length}`}
                   games={games}
                   className={`${className || ''} ${flexGrow}`.trim()}
                   placeholder={<div className="bg-sidebar-accent/10 p-4 rounded-md border border-sidebar-border/40 text-center text-xs text-sidebar-foreground/60 w-[245px] flex-shrink-0">No active events.</div>}
                   isUpcoming={false} />;
}

export function UpcomingGames({ className, otherListEmpty, games }: { className?: string, otherListEmpty?: boolean, games: ActiveGame[] }) {
  // If the other list is empty, allocate more space
  const flexGrow = games.length === 0 && !otherListEmpty ? 'flex-1' : games.length > 0 && otherListEmpty ? 'flex-[2]' : 'flex-1';
  return <GameList label={`Upcoming Events – ${games.length}`}
                   games={games}
                   className={`${className || ''} ${flexGrow}`.trim()}
                   placeholder={<div className="bg-sidebar-accent/10 p-4 rounded-md border border-sidebar-border/40 text-center text-xs text-sidebar-foreground/60 w-[245px] flex-shrink-0">No upcoming events.</div>}
                   isUpcoming={true} />;
}
