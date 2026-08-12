/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

export function formatTimeShort(dateStr?: string): string | undefined {
  if (!dateStr) return undefined
  return new Date(dateStr).toLocaleTimeString([], {
    hour: "numeric",
    minute: "2-digit",
  })
}

export function normalizeFormatName(format?: string | null): string {
  if (!format) return ""
  const trimmed = format.replace(/\0+$/g, "").trim()
  const withoutFalseMultiplierSuffix = trimmed.replace(/(x[36])\s*[0o]$/i, "$1")
  return withoutFalseMultiplierSuffix.replace(/^([^\d]*[A-Za-z])0$/, "$1")
}
