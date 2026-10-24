/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/


import type { DateRange } from "react-day-picker"

// Browser requests are relative by default so development uses Vite's proxy and
// production uses Tracker's same-origin API. A host may explicitly override it.

export const getApiBaseUrl = () => {
  const configured = import.meta.env.VITE_API_BASE_URL?.trim()
  return configured ? configured.replace(/\/$/, "") : ""
}

export const getApiUrl = (path: string) => {
  const normalizedPath = path.startsWith("/") ? path : `/${path}`
  return `${getApiBaseUrl()}${normalizedPath}`
}

export function dateRangeParams(range?: DateRange | string) {
  if (typeof range === "string") {
    const to = new Date()
    const from = /^\d+D$/.test(range) ? new Date(to) : undefined
    if (from) from.setDate(from.getDate() - Number.parseInt(range))
    range = { from, to }
  }
  const params = new URLSearchParams()
  if (range?.from) {
    const from = new Date(range.from)
    from.setHours(0, 0, 0, 0)
    params.set("minDate", from.toISOString())
  }
  if (range?.to) {
    const to = new Date(range.to)
    to.setHours(23, 59, 59, 999)
    params.set("maxDate", to.toISOString())
  }
  return params
}
