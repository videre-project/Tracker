import { useNavigate } from "react-router-dom"
import { Bug, FileJson } from "lucide-react"

import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"

export default function Settings() {
  const navigate = useNavigate()

  return (
    <div className="flex flex-1 flex-col gap-4 p-4 pt-0">
      <Card className="border-sidebar-border/60">
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <FileJson className="h-5 w-5" />
            API Docs
          </CardTitle>
          <CardDescription>
            View the generated OpenAPI reference.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Button variant="outline" onClick={() => navigate('/settings/api-docs')}>
            Open API Docs
          </Button>
        </CardContent>
      </Card>

      <Card className="border-sidebar-border/60">
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <Bug className="h-5 w-5" />
            Diagnostics
          </CardTitle>
          <CardDescription>
            View IPC metrics, endpoint timings, and log streams.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <Button variant="outline" onClick={() => navigate('/settings/diagnostics')}>
            Open Diagnostics
          </Button>
        </CardContent>
      </Card>
    </div>
  )
}
