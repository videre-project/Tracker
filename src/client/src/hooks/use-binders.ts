/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useEffect, useState } from "react"
import { useSessionQuery } from "./use-session-query"
import type { CollectionBinderDTO, CollectionCardDTO } from "@/types/api"

export function useBinders() {
  const list = useSessionQuery<CollectionBinderDTO[]>("/api/collection/binders")
  const binders = list.data ?? []
  const [selectedId, setSelectedId] = useState<number | null>(null)
  const selected = binders.find(binder => binder.id === selectedId) ?? binders[0] ?? null
  const contents = useSessionQuery<CollectionCardDTO[]>(selected?.id != null
    ? `/api/collection/binders/${selected.id}/cards` : null)
  const refreshList = list.refresh, refreshContents = contents.refresh

  useEffect(() => {
    if (!list.isReady) setSelectedId(null)
  }, [list.isReady])
  useEffect(() => {
    const refresh = () => { refreshList(); refreshContents() }
    window.addEventListener("focus", refresh)
    return () => window.removeEventListener("focus", refresh)
  }, [refreshList, refreshContents])

  return { binders, selected, selectBinder: setSelectedId, items: contents.data ?? [],
    loading: list.loading || contents.loading, error: list.error ?? contents.error }
}
