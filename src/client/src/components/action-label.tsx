/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { ArrowRight } from "lucide-react"

export function ActionLabel({ children }: { children: string }) {
  return <span className="inline-flex items-center gap-1 whitespace-nowrap">
    <span>{children}</span>
    <ArrowRight aria-hidden="true" className="-mr-0.5 h-3.5 w-3.5 shrink-0 translate-y-px" />
  </span>
}
