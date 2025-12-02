import { useState, useEffect } from "react"
import { useClientState } from "@/hooks/use-client-state"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Area, AreaChart, ResponsiveContainer, YAxis, Tooltip } from "recharts"
import { Activity } from "lucide-react"

export default function Settings() {
  const { state: clientState } = useClientState()
  const [memoryHistory, setMemoryHistory] = useState<{
    time: number
    usage: number
    workingSet?: number
    virtualMemory?: number
  }[]>([])

  // Update memory history when client state changes
  useEffect(() => {
    if (clientState.memoryUsage) {
      setMemoryHistory(prev => {
        const now = Date.now()
        // Keep last 60 seconds of data
        const newHistory = [
          ...prev,
          {
            time: now,
            usage: clientState.memoryUsage!,
            workingSet: clientState.workingSet,
            virtualMemory: clientState.virtualMemory,
          },
        ]
        return newHistory.filter(item => now - item.time < 60000)
      })
    }
  }, [clientState.memoryUsage])

  const formatMemory = (bytes?: number) => {
    if (!bytes) return "N/A"
    const gb = bytes / 1024 / 1024 / 1024
    return `${gb.toFixed(2)} GB`
  }

  const getMemoryDomain = (): [number, number] => {
    if (memoryHistory.length === 0) return [0, 100 * 1024 * 1024] // Default to 100 MB

    const values = memoryHistory.map((h) => h.usage)
    const max = Math.max(...values)
    const minRange = 100 * 1024 * 1024 // 100 MB

    // Always start at 0, ensure minimum range for visibility
    return [0, Math.max(max, minRange)]
  }

  const CustomTooltip = ({ active, payload, label }: any) => {
    if (active && payload && payload.length) {
      const data = payload[0].payload
      const physical = data.workingSet || 0
      const swap = Math.max(0, data.usage - physical)

      return (
        <div className="rounded-lg border bg-background p-2 shadow-sm">
          <div className="grid grid-cols-2 gap-x-4 gap-y-1 text-xs">
            <span className="text-muted-foreground">Physical:</span>
            <span className="font-mono font-medium">{formatMemory(physical)}</span>

            <span className="text-muted-foreground">Swap:</span>
            <span className="font-mono font-medium">{formatMemory(swap)}</span>
          </div>
          <div className="mt-1 text-[10px] text-muted-foreground">
            {new Date(data.time).toLocaleTimeString()}
          </div>
        </div>
      )
    }
    return null
  }

  const chartColor = clientState.status === "ready" ? "#10b981" : "#6b7280"

  return (
    <div className="flex flex-1 flex-col gap-4 p-4 pt-0">
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        <Card className="border-sidebar-border/60">
          <CardHeader className="flex flex-row items-center justify-between space-y-0 p-4 pb-0">
            <CardTitle className="text-sm font-medium text-muted-foreground">Client Status</CardTitle>
            <Activity className="h-4 w-4 text-muted-foreground" />
          </CardHeader>
          <CardContent className="p-4 pt-0">
            <div className="flex items-end justify-between">
              <div>
                <div className="text-xl font-bold capitalize">{clientState.status}</div>
                <div className="text-xs text-muted-foreground mt-1">
                  PID: {clientState.processId || "N/A"}
                </div>
              </div>
              <div className="text-right">
                <div className="text-xl font-bold">{formatMemory(clientState.memoryUsage)}</div>
                <div className="text-xs text-muted-foreground">Memory</div>
              </div>
            </div>

            <div className="h-[40px] mt-2 -mx-[5px] -mb-4 [&_.recharts-surface]:outline-none [&_.recharts-wrapper]:outline-none">
              <ResponsiveContainer width="100%" height="100%">
                <AreaChart data={memoryHistory}>
                  <defs>
                    <linearGradient id="colorUsage" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="5%" stopColor={chartColor} stopOpacity={0.3} />
                      <stop offset="95%" stopColor={chartColor} stopOpacity={0} />
                    </linearGradient>
                  </defs>
                  <Tooltip
                    content={<CustomTooltip />}
                    cursor={{ stroke: "hsl(var(--muted-foreground))", strokeWidth: 1, strokeDasharray: "4 4" }}
                    allowEscapeViewBox={{ x: false, y: true }}
                  />
                  <YAxis domain={getMemoryDomain()} hide />
                  <Area
                    type="monotone"
                    dataKey="usage"
                    stroke={chartColor}
                    strokeWidth={2}
                    fillOpacity={1}
                    fill="url(#colorUsage)"
                    isAnimationActive={false}
                  />
                </AreaChart>
              </ResponsiveContainer>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  )
}
