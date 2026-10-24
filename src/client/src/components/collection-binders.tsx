/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useMemo, useState } from "react"
import { Search } from "lucide-react"
import { Button, Input, VirtualCollectionGrid, type CollectionCardEntry, type CollectionSelection } from "@videreproject/ui"
import { useBinders } from "@/hooks/use-binders"

export function CollectionBinders({ cards, onSelectItem }: {
  cards: CollectionCardEntry[]
  onSelectItem: (selection: CollectionSelection) => void
}) {
  const { binders, selected, selectBinder, items, loading, error } = useBinders()
  const [search, setSearch] = useState("")
  const visibleBinders = binders.filter(binder => (binder.name ?? "").toLocaleLowerCase().includes(search.trim().toLocaleLowerCase()))
  const binderCards = useMemo(() => {
    const metadata = new Map(cards.map(card => [card.catalogId, card]))
    return items.map(item => ({ ...metadata.get(item.catalogId), ...item }))
  }, [cards, items])

  return <div className="flex h-full min-h-0">
    <nav aria-label="Binders" className="flex w-56 min-w-0 shrink-0 flex-col border-r border-sidebar-border/60">
      <div className="relative m-2">
        <Search className="pointer-events-none absolute left-2 top-2.5 h-3.5 w-3.5 text-muted-foreground" />
        <Input aria-label="Find a binder" placeholder="Find a binder…" value={search}
          onChange={event => setSearch(event.target.value)} className="h-8 pl-7 text-xs" />
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto px-2 pb-2">
        {visibleBinders.map(binder => <Button key={binder.id} variant="ghost" aria-pressed={selected?.id === binder.id}
          className={`h-8 w-full justify-start px-2 text-xs ${selected?.id === binder.id ? "bg-accent" : ""}`}
          title={binder.name ?? undefined} onClick={() => selectBinder(binder.id ?? null)}>
          <span className="truncate">{binder.name}</span>
        </Button>)}
        {!visibleBinders.length && !loading && !error && <p className="px-2 py-3 text-xs text-muted-foreground">
          {search ? "No binders match your search." : "No binders available."}
        </p>}
      </div>
    </nav>
    <div className="flex min-h-0 min-w-0 flex-1 flex-col">
      {error ? <p role="alert" className="px-3 py-2 text-xs text-destructive">{error}</p> : null}
      {selected && <div className="flex h-9 shrink-0 items-center justify-between gap-3 border-b border-sidebar-border/60 px-3 text-xs">
        <span className="truncate font-medium">{selected.name}</span>
        {!loading && !error && <span className="shrink-0 tabular-nums text-muted-foreground">
          {items.reduce((sum, item) => sum + item.quantity, 0).toLocaleString()} items
        </span>}
      </div>}
      <div className="flex min-h-0 flex-1 flex-col">
        {!loading && !error && binderCards.length === 0
          ? <p className="flex flex-1 items-center justify-center text-sm text-muted-foreground">
              {selected ? "This binder is empty." : "Select a binder to view its contents."}
            </p>
          : <VirtualCollectionGrid items={binderCards} viewMode="cards" selectedCatalogId={null}
              onSelectItem={onSelectItem} loading={loading} />}
      </div>
    </div>
  </div>
}
