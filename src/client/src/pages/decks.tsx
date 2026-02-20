"use client"

import { useState, useEffect } from "react"
import {
  useDecks,
  useDeckDetail,
  useDeckArchetype,
  DeckSummary
} from "@/hooks/use-decks"
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { ChevronDown, ChevronRight, Loader2 } from "lucide-react"

function formatDate(dateString?: string) {
  if (!dateString) return "-"
  const date = new Date(dateString)
  return date.toLocaleString(undefined, {
    year: 'numeric',
    month: 'numeric',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

interface DeckRowProps {
  deck: DeckSummary
  isExpanded: boolean
  onToggle: () => void
}

function DeckRow({ deck, isExpanded, onToggle }: DeckRowProps) {
  const { detail, loading: detailLoading } = useDeckDetail(isExpanded ? deck.hash : null)
  const { archetype, loading: archetypeLoading, fetchArchetype } = useDeckArchetype(deck.hash)

  // Fetch archetype when expanded
  useEffect(() => {
    if (isExpanded && !archetype && !archetypeLoading) {
      fetchArchetype()
    }
  }, [isExpanded, archetype, archetypeLoading, fetchArchetype])

  return (
    <>
      <TableRow
        className="cursor-pointer hover:bg-muted/50"
        onClick={onToggle}
      >
        <TableCell className="w-8">
          {isExpanded ? (
            <ChevronDown className="h-4 w-4" />
          ) : (
            <ChevronRight className="h-4 w-4" />
          )}
        </TableCell>
        <TableCell className="font-medium">{deck.name}</TableCell>
        <TableCell>
          <div className="flex items-center gap-2">
            {deck.archetype ? (
              <span className="text-sm">{deck.archetype}</span>
            ) : (
              <span className="text-muted-foreground text-sm italic">Unknown</span>
            )}
            {deck.colors.length > 0 && (
              <div className="flex gap-0.5">
                {deck.colors.map(color => (
                  <span
                    key={color}
                    className={`w-4 h-4 rounded-full text-xs font-bold flex items-center justify-center ${color === 'W' ? 'bg-amber-100 text-amber-800' :
                        color === 'U' ? 'bg-blue-500 text-white' :
                          color === 'B' ? 'bg-gray-800 text-white' :
                            color === 'R' ? 'bg-red-500 text-white' :
                              color === 'G' ? 'bg-green-600 text-white' : 'bg-gray-400'
                      }`}
                  >
                    {color}
                  </span>
                ))}
              </div>
            )}
          </div>
        </TableCell>
        <TableCell>
          <Badge variant="outline">{deck.format}</Badge>
        </TableCell>
        <TableCell>{deck.mainboardCount}</TableCell>
        <TableCell>{deck.sideboardCount}</TableCell>
        <TableCell>{formatDate(deck.timestamp)}</TableCell>
      </TableRow>

      {isExpanded && (
        <TableRow>
          <TableCell colSpan={7} className="bg-muted/30 p-4">
            <div className="space-y-4">
              {/* Archetype Section */}
              <div>
                <h4 className="text-sm font-semibold mb-2">Archetype (NBAC API Response)</h4>
                {archetypeLoading ? (
                  <div className="flex items-center gap-2 text-muted-foreground">
                    <Loader2 className="h-4 w-4 animate-spin" />
                    Loading archetype...
                  </div>
                ) : archetype ? (
                  <pre className="bg-black/20 rounded-md p-3 text-xs overflow-x-auto max-h-48 overflow-y-auto">
                    {JSON.stringify(archetype, null, 2)}
                  </pre>
                ) : (
                  <p className="text-muted-foreground text-sm">No archetype data</p>
                )}
              </div>

              {/* Decklist Section */}
              <div>
                <h4 className="text-sm font-semibold mb-2">Decklist</h4>
                {detailLoading ? (
                  <div className="flex items-center gap-2 text-muted-foreground">
                    <Loader2 className="h-4 w-4 animate-spin" />
                    Loading decklist...
                  </div>
                ) : detail ? (
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                    <div>
                      <h5 className="text-xs font-medium text-muted-foreground mb-1">
                        Mainboard ({detail.mainboard.reduce((acc, c) => acc + c.quantity, 0)} cards)
                      </h5>
                      <pre className="bg-black/20 rounded-md p-3 text-xs overflow-x-auto max-h-64 overflow-y-auto">
                        {detail.mainboard.map(c => `${c.quantity} ${c.name}`).join('\n')}
                      </pre>
                    </div>
                    <div>
                      <h5 className="text-xs font-medium text-muted-foreground mb-1">
                        Sideboard ({detail.sideboard.reduce((acc, c) => acc + c.quantity, 0)} cards)
                      </h5>
                      <pre className="bg-black/20 rounded-md p-3 text-xs overflow-x-auto max-h-64 overflow-y-auto">
                        {detail.sideboard.length > 0
                          ? detail.sideboard.map(c => `${c.quantity} ${c.name}`).join('\n')
                          : '(empty)'}
                      </pre>
                    </div>
                  </div>
                ) : (
                  <p className="text-muted-foreground text-sm">No decklist data</p>
                )}
              </div>
            </div>
          </TableCell>
        </TableRow>
      )}
    </>
  )
}

export default function Decks() {
  const { decks, loading, error } = useDecks()
  const [expandedHash, setExpandedHash] = useState<string | null>(null)

  const formats = Object.keys(decks).sort()
  const allDecks = formats.flatMap(format => decks[format] || [])

  const handleToggle = (hash: string) => {
    setExpandedHash(prev => prev === hash ? null : hash)
  }

  return (
    <div className="container mx-auto py-4 px-4 space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Decks</h1>
        <div className="flex items-center gap-2 text-sm text-muted-foreground">
          {formats.length > 0 && (
            <span>{allDecks.length} deck{allDecks.length !== 1 ? 's' : ''} across {formats.length} format{formats.length !== 1 ? 's' : ''}</span>
          )}
        </div>
      </div>

      {error && (
        <div className="bg-destructive/15 text-destructive px-4 py-3 rounded-md text-sm font-medium">
          Error loading decks: {error}
        </div>
      )}

      {loading ? (
        <div className="flex items-center justify-center py-12">
          <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
        </div>
      ) : allDecks.length === 0 ? (
        <div className="text-center py-12 text-muted-foreground">
          No decks found in the database.
        </div>
      ) : (
        <div className="rounded-md border">
          <Table>
            <TableHeader>
              <TableRow className="bg-muted/50">
                <TableHead className="w-8"></TableHead>
                <TableHead>Name</TableHead>
                <TableHead>Archetype</TableHead>
                <TableHead>Format</TableHead>
                <TableHead>Mainboard</TableHead>
                <TableHead>Sideboard</TableHead>
                <TableHead>Date</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {allDecks.map(deck => (
                <DeckRow
                  key={deck.hash}
                  deck={deck}
                  isExpanded={expandedHash === deck.hash}
                  onToggle={() => handleToggle(deck.hash)}
                />
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </div>
  )
}
