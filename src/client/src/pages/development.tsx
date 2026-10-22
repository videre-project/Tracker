/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { useState } from "react"
import { useOutletContext } from "react-router-dom"
import { Button } from "@videreproject/ui"

export default function Development() {
  const { canReload } = useOutletContext<{ canReload: boolean }>()
  const [previewError, setPreviewError] = useState(false)

  if (previewError) {
    throw new TypeError("Development preview: simulated page render failure.")
  }

  return (
    <div className="flex w-full flex-col gap-6 px-4 py-6">
      <header className="space-y-2 border-b border-sidebar-border/60 pb-6">
        <h1 className="text-xl font-semibold">Development</h1>
        <p className="text-sm text-muted-foreground">Preview application errors and test recovery.</p>
      </header>
      <section aria-labelledby="error-preview-heading">
        <h2 id="error-preview-heading" className="text-sm font-medium">Error handling</h2>
        <div className="flex flex-wrap items-center justify-between gap-6 py-5">
          <div className="space-y-1">
            <p className="text-sm font-medium">Error screen</p>
            <p className="text-sm leading-relaxed text-muted-foreground">Trigger a page error to inspect the stack trace and test recovery. Reload to return here.</p>
          </div>
          <Button size="sm" variant="outline" disabled={!canReload} onClick={() => setPreviewError(true)}>
            Preview error screen
          </Button>
        </div>
      </section>
    </div>
  )
}
