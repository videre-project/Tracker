/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import type { ActivityClockDTO } from "@/types/api"

export function activityClockProgress(clock: ActivityClockDTO | null | undefined, now: number): number | null {
  if (!clock) return null
  const { durationSeconds = 0, remainingSeconds = -1 } = clock
  if (!Number.isFinite(durationSeconds) || durationSeconds <= 0 ||
      !Number.isFinite(remainingSeconds) || remainingSeconds < 0) return null
  const sampledAt = Date.parse(clock.sampledAt ?? "")
  if (!Number.isFinite(sampledAt)) return null
  const elapsed = clock.running ? Math.max(0, now - sampledAt) / 1000 : 0
  return Math.max(0, Math.min(1, 1 - (remainingSeconds - elapsed) / durationSeconds))
}
