/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { X } from "lucide-react"
import { Button } from "@videreproject/ui"

export function OpponentClassificationNotice({ count, pending, error, onUndo, onDismiss }: {
  count: number
  pending: boolean
  error?: string | null
  onUndo: () => void
  onDismiss: () => void
}) {
  return <div role="status" className="fixed bottom-5 right-5 z-50 max-w-[calc(100vw-2.5rem)] rounded-lg border border-sidebar-border/70 bg-card px-3 py-2 shadow-lg">
    <div className="flex items-center gap-3 text-xs">
      <span>Updated {count === 1 ? "opponent classification" : `${count} opponent classifications`}</span>
      <Button variant="ghost" size="sm" className="h-7 px-2 text-xs" disabled={pending} onClick={onUndo}>Undo</Button>
      <Button variant="ghost" size="icon" className="h-6 w-6" aria-label="Dismiss classification notification" disabled={pending} onClick={onDismiss}><X className="h-3.5 w-3.5" /></Button>
    </div>
    {error && <p role="alert" className="mt-1 text-xs text-destructive">{error}</p>}
  </div>
}
