import React, { useState, useEffect, useRef, useCallback } from "react"
import { Button } from "@/components/ui/button"
import {
  ChevronLeft,
  ChevronRight,
  SkipBack,
  SkipForward,
  Play,
  Pause,
} from "lucide-react"
import type { ReplaySnapshot } from "@/types/replay-types"

export interface ReplayTimelineProps {
  snapshots: ReplaySnapshot[]
  currentIndex: number
  onStepTo: (index: number) => void
}

export function ReplayTimeline({
  snapshots,
  currentIndex,
  onStepTo,
}: ReplayTimelineProps) {
  const [playing, setPlaying] = useState(false)
  const [speed, setSpeed] = useState(1000) // ms per step
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null)
  const trackRef = useRef<HTMLDivElement>(null)

  const maxIndex = snapshots.length - 1

  // Auto-play logic
  useEffect(() => {
    if (playing) {
      intervalRef.current = setInterval(() => {
        onStepTo(currentIndex + 1)
      }, speed)
    }
    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current)
    }
  }, [playing, currentIndex, speed, onStepTo])

  // Pause when reaching the end
  useEffect(() => {
    if (currentIndex >= maxIndex && playing) {
      setPlaying(false)
    }
  }, [currentIndex, maxIndex, playing])

  const togglePlay = useCallback(() => setPlaying((p) => !p), [])

  const cycleSpeed = useCallback(() => {
    setSpeed((s) => {
      if (s === 1000) return 500
      if (s === 500) return 250
      return 1000
    })
  }, [])

  // Click on timeline track to jump
  const handleTrackClick = useCallback(
    (e: React.MouseEvent<HTMLDivElement>) => {
      if (!trackRef.current || maxIndex <= 0) return
      const rect = trackRef.current.getBoundingClientRect()
      const x = e.clientX - rect.left
      const ratio = Math.max(0, Math.min(1, x / rect.width))
      const target = Math.round(ratio * maxIndex)
      onStepTo(target)
    },
    [maxIndex, onStepTo],
  )

  const progress = maxIndex > 0 ? ((currentIndex + 1) / (maxIndex + 1)) * 100 : 0

  return (
    <div className="flex flex-col gap-2 px-4 pt-3 bg-card/50 border-t border-sidebar-border/60">
      {/* Timeline track */}
      <div
        ref={trackRef}
        onClick={handleTrackClick}
        className="relative h-3 bg-muted/50 rounded-full cursor-pointer group"
      >
        {/* Progress fill */}
        <div
          className="absolute top-0 left-0 h-full bg-primary/60 rounded-full transition-all duration-150"
          style={{ width: `${progress}%` }}
        />
        {/* Turn markers */}
        {snapshots.map((snap, i) => {
          // Show a marker at the start of each new turn
          const prev = i > 0 ? snapshots[i - 1] : null
          if (prev && snap.turnNumber === prev.turnNumber) return null
          const pos = maxIndex > 0 ? (i / maxIndex) * 100 : 0
          return (
            <div
              key={i}
              className="absolute top-0 w-px h-full bg-muted-foreground/20"
              style={{ left: `${pos}%` }}
              title={`Turn ${snap.turnNumber}`}
            />
          )
        })}
        {/* Thumb */}
        <div
          className="absolute top-1/2 -translate-y-1/2 w-3.5 h-3.5 bg-primary rounded-full shadow border-2 border-background transition-all duration-150"
          style={{ left: `calc(${progress}% - 7px)` }}
        />
      </div>

      {/* Controls */}
      <div className="flex items-center justify-center gap-1">
        <Button
          variant="ghost"
          size="icon"
          className="h-7 w-7"
          onClick={() => onStepTo(-1)}
          disabled={currentIndex <= -1}
          title="Jump to start"
        >
          <SkipBack className="h-3.5 w-3.5" />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          className="h-7 w-7"
          onClick={() => onStepTo(currentIndex - 1)}
          disabled={currentIndex <= -1}
          title="Step backward"
        >
          <ChevronLeft className="h-4 w-4" />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          className="h-8 w-8"
          onClick={togglePlay}
          disabled={maxIndex < 0}
          title={playing ? "Pause" : "Play"}
        >
          {playing ? (
            <Pause className="h-4 w-4" />
          ) : (
            <Play className="h-4 w-4" />
          )}
        </Button>
        <Button
          variant="ghost"
          size="icon"
          className="h-7 w-7"
          onClick={() => onStepTo(currentIndex + 1)}
          disabled={currentIndex >= maxIndex}
          title="Step forward"
        >
          <ChevronRight className="h-4 w-4" />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          className="h-7 w-7"
          onClick={() => onStepTo(maxIndex)}
          disabled={currentIndex >= maxIndex}
          title="Jump to end"
        >
          <SkipForward className="h-3.5 w-3.5" />
        </Button>

        {/* Speed toggle */}
        <button
          onClick={cycleSpeed}
          className="ml-3 px-2 py-0.5 text-[10px] font-mono text-muted-foreground bg-muted/50 rounded hover:bg-muted/80 transition-colors"
          title="Cycle playback speed"
        >
          {speed === 1000 ? "1x" : speed === 500 ? "2x" : "4x"}
        </button>
      </div>

    </div>
  )
}
