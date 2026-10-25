import { describe, expect, it } from "vitest"
import { activityClockProgress } from "./activity-clock"
import type { ActivityClockDTO } from "@/types/api"

const sampledAt = "2026-10-23T12:00:00Z"
const now = Date.parse(sampledAt)
const clock: ActivityClockDTO = { sampledAt, durationSeconds: 100, remainingSeconds: 60, running: true }

describe("activity clock progress", () => {
  it("advances from the measured remaining time between snapshots", () => {
    expect(activityClockProgress(clock, now)).toBeCloseTo(0.4)
    expect(activityClockProgress(clock, now + 10000)).toBeCloseTo(0.5)
    expect(activityClockProgress({ ...clock, sampledAt: new Date(now + 10000).toISOString(), remainingSeconds: 50 }, now + 10000)).toBeCloseTo(0.5)
  })

  it("holds paused clocks and handles client/server clock skew", () => {
    expect(activityClockProgress({ ...clock, running: false }, now + 30000)).toBeCloseTo(0.4)
    expect(activityClockProgress(clock, now - 30000)).toBeCloseTo(0.4)
  })

  it("bounds expired clocks and rejects unavailable or invalid timing", () => {
    expect(activityClockProgress(clock, now + 120000)).toBe(1)
    expect(activityClockProgress({ ...clock, remainingSeconds: 120 }, now)).toBe(0)
    expect(activityClockProgress(null, now)).toBeNull()
    expect(activityClockProgress({ ...clock, durationSeconds: 0 }, now)).toBeNull()
    expect(activityClockProgress({ ...clock, sampledAt: "invalid" }, now)).toBeNull()
    expect(activityClockProgress({ ...clock, remainingSeconds: NaN }, now)).toBeNull()
  })
})
