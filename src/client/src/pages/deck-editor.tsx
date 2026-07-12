/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import {
  type ComponentType,
  type ReactNode,
  useCallback,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
} from "react"
import { createPortal } from "react-dom"
import { useLocation, useNavigate, useParams } from "react-router-dom"
import {
  ArrowLeft,
  CalendarClock,
  Clipboard,
  ClipboardCheck,
  Download,
  Pencil,
  PanelRightClose,
  PanelRightOpen,
  Tags,
  Settings2,
  Upload,
} from "lucide-react"

import { DeckCollectionEditor } from "@/components/decks/deck-collection-editor"
import {
  DeckBuildSidePane,
  type SidePanelView,
} from "@/components/decks/deck-build-side-pane"
import type { SortMode } from "@/hooks/use-sortable-cards"
import { Button } from "@/components/ui/button"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Skeleton } from "@/components/ui/skeleton"
import { cn } from "@/lib/utils"
import { buildDeckListText, getDeckFileName } from "@/utils/deck-list"
import type {
  DeckDetail,
  DeckSummary,
} from "@/hooks/use-decks"
import {
  useDeckDetail,
  useDecks,
} from "@/hooks/use-decks"
type DeckRouteState = {
  deckName?: string
  deckFormat?: string
  deckColors?: string[]
}


function formatDate(value?: string) {
  if (!value) return "Unknown date"
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return "Unknown date"

  return date.toLocaleDateString(undefined, {
    month: "short",
    day: "numeric",
    year: "numeric",
  })
}

function getDeckCounts(summary?: DeckSummary, detail?: DeckDetail | null) {
  if (summary) {
    return {
      main: summary.mainboardCount,
      side: summary.sideboardCount,
    }
  }

  return {
    main: detail?.mainboard.reduce((total, card) => total + card.quantity, 0) ?? 0,
    side: detail?.sideboard.reduce((total, card) => total + card.quantity, 0) ?? 0,
  }
}

function BreadcrumbManaSymbols({ colors }: { colors: string[] }) {
  const visibleColors = colors.length > 0 ? colors : ["C"]

  return (
    <span className="inline-flex h-4 items-center gap-0.5 translate-y-[2px] leading-none">
      {visibleColors.map((color, index) => (
        <img
          key={`${color}-${index}`}
          src={`/mana-symbols/${color}.svg`}
          alt={color}
          className="block h-3.5 w-3.5 rounded-full bg-background shadow-sm ring-1 ring-background"
        />
      ))}
    </span>
  )
}

function HeaderMeta({
  icon: Icon,
  label,
  children,
  className,
}: {
  icon?: ComponentType<{ className?: string }>
  label: string
  children: ReactNode
  className?: string
}) {
  return (
    <span className={cn("inline-flex min-w-0 max-w-full items-center gap-2 whitespace-nowrap text-sm", className)}>
      {Icon ? <Icon className="h-3.5 w-3.5 shrink-0 translate-y-px text-muted-foreground" /> : null}
      <span className="shrink-0 text-muted-foreground">{label}</span>
      <span className="min-w-0 truncate font-medium text-foreground">{children}</span>
    </span>
  )
}

export default function DeckEditor() {
  const { deckHash = "" } = useParams()
  const navigate = useNavigate()
  const location = useLocation()
  const routeState = location.state as DeckRouteState | null
  const [copiedDeckList, setCopiedDeckList] = useState(false)
  const [sidePanelView, setSidePanelView] = useState<SidePanelView>("cards")
  const [sortMode, setSortMode] = useState<SortMode>("cmc")
  const [isSideboardCollapsed, setIsSideboardCollapsed] = useState(true)
  const [isDeckToolsCollapsed, setIsDeckToolsCollapsed] = useState(false)
  const [splitEditorHeaderControls, setSplitEditorHeaderControls] = useState(false)
  const editorHeaderRef = useRef<HTMLDivElement>(null)
  const editorHeaderSingleRowProbeRef = useRef<HTMLDivElement>(null)

  const { decks, loading: summariesLoading } = useDecks()
  const { detail, loading: detailLoading } = useDeckDetail(deckHash)

  const allDecks = useMemo(
    () => Object.values(decks).flat(),
    [decks]
  )
  const summary = useMemo(
    () => allDecks.find(deck => deck.hash === deckHash),
    [allDecks, deckHash]
  )

  const deckName = summary?.name ?? detail?.name ?? routeState?.deckName ?? "Deck"
  const archetype = summary?.archetype || "Unclassified deck"
  const colors = summary?.colors?.length
    ? summary.colors
    : routeState?.deckColors?.length
      ? routeState.deckColors
      : ["C"]
  const timestamp = summary?.timestamp ?? detail?.timestamp
  const counts = getDeckCounts(summary, detail)
  const deckListText = useMemo(() => buildDeckListText(detail), [detail])
  const canExportDeckList = Boolean(detail)
  const hasHeaderData = Boolean(summary || detail || routeState?.deckName)
  const loadingHeader = (summariesLoading || detailLoading) && !hasHeaderData
  const canNavigateBack = location.key !== "default"

  const navigateBack = useCallback(() => {
    if (canNavigateBack) {
      navigate(-1)
      return
    }

    navigate("/decks")
  }, [canNavigateBack, navigate])

  useLayoutEffect(() => {
    const header = editorHeaderRef.current
    const probe = editorHeaderSingleRowProbeRef.current
    if (!header || !probe) return

    const measureHeaderFit = () => {
      const nextSplit = probe.offsetWidth > header.clientWidth
      setSplitEditorHeaderControls(current => current === nextSplit ? current : nextSplit)
    }

    measureHeaderFit()

    if (typeof ResizeObserver === "undefined") return

    const observer = new ResizeObserver(measureHeaderFit)
    observer.observe(header)
    observer.observe(probe)

    return () => observer.disconnect()
  }, [
    archetype,
    copiedDeckList,
    counts.main,
    counts.side,
    loadingHeader,
    timestamp,
  ])

  const copyDeckList = useCallback(async () => {
    if (!canExportDeckList) return

    try {
      await navigator.clipboard.writeText(deckListText)
    } catch {
      const textarea = document.createElement("textarea")
      textarea.value = deckListText
      textarea.setAttribute("readonly", "")
      textarea.style.position = "fixed"
      textarea.style.opacity = "0"
      document.body.append(textarea)
      textarea.select()
      document.execCommand("copy")
      textarea.remove()
    }

    setCopiedDeckList(true)
    window.setTimeout(() => setCopiedDeckList(false), 1600)
  }, [canExportDeckList, deckListText])

  const downloadDeckList = useCallback(() => {
    if (!canExportDeckList) return

    const blob = new Blob([deckListText], { type: "text/plain;charset=utf-8" })
    const url = URL.createObjectURL(blob)
    const anchor = document.createElement("a")
    anchor.href = url
    anchor.download = getDeckFileName(deckName)
    document.body.append(anchor)
    anchor.click()
    anchor.remove()
    URL.revokeObjectURL(url)
  }, [canExportDeckList, deckListText, deckName])
  const renderEditorControls = (className?: string) => (
    <div className={cn("flex shrink-0 flex-wrap items-center justify-end gap-2", className)}>
      <div className="flex items-center gap-2">
        <span className="text-xs text-muted-foreground">Sort</span>
        <Select
          value={sortMode}
          onValueChange={value => setSortMode(value as SortMode)}
        >
          <SelectTrigger className="h-8 w-[108px] border-sidebar-border/70 bg-background/70 text-sm">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="cmc">CMC</SelectItem>
            <SelectItem value="colors">Colors</SelectItem>
            <SelectItem value="types">Types</SelectItem>
            <SelectItem value="rarity">Rarity</SelectItem>
          </SelectContent>
        </Select>
      </div>
      {counts.side > 0 ? (
        <Button
          variant="outline"
          size="sm"
          onClick={() => setIsSideboardCollapsed(current => !current)}
          className={cn(
            "h-8 border-sidebar-border/70 bg-background/70",
            !isSideboardCollapsed && "bg-secondary/70 text-secondary-foreground hover:bg-secondary/80"
          )}
        >
          {isSideboardCollapsed ? (
            <PanelRightOpen className="h-4 w-4" />
          ) : (
            <PanelRightClose className="h-4 w-4" />
          )}
          Sideboard
        </Button>
      ) : null}
    </div>
  )

  const renderDeckActions = (className?: string) => (
    <div className={cn(
      "grid w-80 max-w-full shrink-0 grid-cols-[minmax(0,1fr)_minmax(0,1fr)_minmax(0,1fr)_2rem] items-center gap-2",
      className
    )}>
      <Button
        variant="outline"
        size="sm"
        onClick={copyDeckList}
        disabled={!canExportDeckList}
        className="h-8 w-full justify-center border-sidebar-border/70 bg-background/70 px-2"
      >
        {copiedDeckList ? (
          <ClipboardCheck className="h-4 w-4" />
        ) : (
          <Clipboard className="h-4 w-4" />
        )}
        {copiedDeckList ? "Copied" : "Copy list"}
      </Button>
      <Button
        variant="outline"
        size="sm"
        onClick={downloadDeckList}
        disabled={!canExportDeckList}
        className="h-8 w-full justify-center border-sidebar-border/70 bg-background/70 px-2"
      >
        <Download className="h-4 w-4" />
        Export
      </Button>
      <Button
        variant="outline"
        size="sm"
        disabled
        title="Deck import is not available yet"
        className="h-8 w-full justify-center border-sidebar-border/70 bg-background/70 px-2"
      >
        <Upload className="h-4 w-4" />
        Import
      </Button>
      <Button
        variant="outline"
        size="icon"
        onClick={() => setIsDeckToolsCollapsed(current => !current)}
        className={cn(
          "h-8 w-8 shrink-0 border-sidebar-border/70 bg-background/70",
          !isDeckToolsCollapsed && "bg-secondary/70 text-secondary-foreground hover:bg-secondary/80"
        )}
        aria-label={isDeckToolsCollapsed ? "Show deck tools" : "Hide deck tools"}
        title={isDeckToolsCollapsed ? "Show deck tools" : "Hide deck tools"}
        aria-pressed={!isDeckToolsCollapsed}
      >
        <Settings2 className="h-4 w-4" />
      </Button>
    </div>
  )
  const breadcrumbContextHost = typeof document === "undefined"
    ? null
    : document.getElementById("page-header-context")

  const renderBreadcrumbDeckContext = () => {
    if (loadingHeader) return null

    return (
      <div className="group inline-flex items-center gap-1.5">
        <BreadcrumbManaSymbols colors={colors} />
        <Button
          variant="ghost"
          size="icon"
          type="button"
          className="h-4 w-4 p-0 text-muted-foreground/40 transition-colors group-hover:text-muted-foreground"
          title="Edit deck name and colors"
          aria-label="Edit deck name and colors"
        >
          <Pencil className="h-3.5 w-3.5 shrink-0 translate-y-px" />
        </Button>
      </div>
    )
  }

  return (
    <div className="flex h-[calc(100vh-2.5rem)] min-h-0 flex-col gap-2 overflow-hidden px-4 pb-4 pt-1">
      {breadcrumbContextHost ? createPortal(
        renderBreadcrumbDeckContext(),
        breadcrumbContextHost
      ) : null}
      <div className="flex flex-col gap-2 pt-0">
        <div
          ref={editorHeaderRef}
          className={cn(
            "relative grid min-w-0 items-start gap-x-4 gap-y-2",
            splitEditorHeaderControls
              ? "grid-cols-[minmax(0,1fr)_20rem]"
              : "grid-cols-[minmax(0,1fr)_auto]"
          )}
        >
          <div
            ref={editorHeaderSingleRowProbeRef}
            aria-hidden="true"
            {...({ inert: "" } as Record<string, string>)}
            className="pointer-events-none absolute left-0 top-0 -z-10 flex w-max max-w-none items-start gap-4 opacity-0"
          >
            <div className="flex min-w-max items-start gap-3">
              <div className="mt-0.5 h-8 w-8 shrink-0" />
              {!loadingHeader ? (
                <div className="flex min-w-max flex-nowrap items-center gap-x-5 pt-1.5">
                  <HeaderMeta
                    icon={Tags}
                    label="Archetype"
                    className="max-w-[20rem]"
                  >
                    {archetype}
                  </HeaderMeta>
                  <HeaderMeta icon={CalendarClock} label="Updated">
                    {formatDate(timestamp)}
                  </HeaderMeta>
                  <HeaderMeta label="Cards">
                    {counts.main} main / {counts.side} side
                  </HeaderMeta>
                </div>
              ) : null}
            </div>
            <div className="flex shrink-0 items-center gap-2">
              {loadingHeader ? null : renderEditorControls("w-auto")}
              {renderDeckActions("w-80")}
            </div>
          </div>

          <div className="flex min-w-0 items-start gap-3 overflow-hidden">
            <Button
              variant="ghost"
              size="icon"
              onClick={navigateBack}
              className="mt-0.5 h-8 w-8 shrink-0"
              aria-label="Back"
            >
              <ArrowLeft className="h-5 w-5" />
            </Button>

            <div className="min-w-0 flex-1">
              {loadingHeader ? (
                <div className="flex items-center gap-3">
                  <Skeleton className="h-7 w-[34rem] max-w-full" />
                  <Skeleton className="h-8 w-64" />
                </div>
              ) : (
                <div className="flex min-w-0 flex-nowrap items-center gap-x-5 gap-y-2 overflow-hidden pt-1.5">
                  <HeaderMeta
                    icon={Tags}
                    label="Archetype"
                    className="max-w-[20rem]"
                  >
                    {archetype}
                  </HeaderMeta>
                  <HeaderMeta icon={CalendarClock} label="Updated">
                    {formatDate(timestamp)}
                  </HeaderMeta>
                  <HeaderMeta label="Cards">
                    {counts.main} main / {counts.side} side
                  </HeaderMeta>
                </div>
              )}
            </div>
          </div>

          <div className={cn(
            "flex max-w-full shrink-0 items-end gap-2",
            splitEditorHeaderControls
              ? "flex-col"
              : "flex-row items-center gap-4"
          )}>
            {loadingHeader ? null : renderEditorControls(
              splitEditorHeaderControls ? "order-2 w-auto" : "order-1 w-auto"
            )}
            {renderDeckActions(
              splitEditorHeaderControls ? "order-1 w-80" : "order-2 w-80"
            )}
          </div>
        </div>
      </div>

      <div className="flex min-h-0 flex-1 items-stretch gap-4">
        <DeckCollectionEditor
          deckHash={deckHash}
          className="h-full flex-1 gap-0 p-0"
          editorTitle="Editor"
          hideDeckSelector
          showDeckStats={false}
          showFixedDeckLabel={false}
          hideEditorHeader
          sortMode={sortMode}
          onSortModeChange={setSortMode}
          sideboardCollapsed={isSideboardCollapsed}
          onSideboardCollapsedChange={setIsSideboardCollapsed}
        />
        <DeckBuildSidePane
          view={sidePanelView}
          onViewChange={setSidePanelView}
          isCollapsed={isDeckToolsCollapsed}
        />
      </div>
    </div>
  )
}
