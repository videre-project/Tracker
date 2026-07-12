/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { getApiUrl } from "@/utils/api-config"

const scalarDocsUrl = getApiUrl("/docs")

export default function ApiDocs() {
  return (
    <div className="h-[calc(100vh-3.5rem)] min-h-0 px-4">
      <div className="h-full min-h-0 overflow-hidden rounded-lg border border-sidebar-border/60 bg-background shadow-sm">
        <iframe
          title="API docs"
          src={scalarDocsUrl}
          className="h-full w-full border-0 bg-background"
        />
      </div>
    </div>
  )
}
