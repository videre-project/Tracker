/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { getApiUrl } from "@/utils/api-config"

export async function settingsRequest<T>(path: string, options: RequestInit = {}): Promise<T> {
  const response = await fetch(getApiUrl(`/api/settings${path}`), {
    ...options,
    headers: {
      "X-Tracker-Settings": "1",
      ...(options.body instanceof FormData ? {} : { "Content-Type": "application/json" }),
      ...options.headers
    }
  })
  if (!response.ok) {
    const text = await response.text()
    let message = text
    try {
      const error = JSON.parse(text)
      message = typeof error === "string" ? error : error?.detail ?? error?.title ?? text
    } catch { /* The host also returns plain-text validation errors. */ }
    throw new Error(message || `Request failed (${response.status}).`)
  }
  const text = await response.text()
  return text ? JSON.parse(text) as T : undefined as T
}
