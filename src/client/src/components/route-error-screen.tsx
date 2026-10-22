/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { Fragment, useEffect, useState } from "react"
import { AlertCircle, Check, Copy, RotateCw, Terminal } from "lucide-react"
import { Link, isRouteErrorResponse, useRouteError } from "react-router-dom"
import { Button } from "@videreproject/ui"

function StackLocation({ value }: { value: string }) {
  const match = value.match(/^(.*?)(:\d+(?::\d+)?)$/)
  const path = match?.[1] ?? value
  const slash = Math.max(path.lastIndexOf("/"), path.lastIndexOf("\\"))
  const [filename, query] = path.slice(slash + 1).split(/\?(.*)/s)
  return (
    <>
      <span className="text-slate-400">{path.slice(0, slash + 1)}</span>
      <span className="text-slate-200">{filename}</span>
      {query != null && <span className="text-slate-400">?{query}</span>}
      {match && <span className="text-amber-300">{match[2]}</span>}
    </>
  )
}

function StackLine({ line, first }: { line: string; first: boolean }) {
  if (first) {
    const message = line.match(/^([\w.]*Error:)(.*)$/)
    return message ? (
      <><span className="font-semibold text-rose-400">{message[1]}</span><span className="text-rose-200">{message[2]}</span></>
    ) : <span className="text-rose-300">{line}</span>
  }
  const frame = line.match(/^(\s*at\s+)(?:(.*?) \((.*)\)|(.*))$/)
  if (frame) return (
    <>
      <span className="text-slate-400">{frame[1]}</span>
      {frame[2] != null && <><span className="text-sky-300">{frame[2]}</span><span className="text-slate-400"> (</span></>}
      <StackLocation value={frame[3] ?? frame[4]} />
      {frame[2] != null && <span className="text-slate-400">)</span>}
    </>
  )
  const firefoxFrame = line.match(/^([^@]*)@(.+)$/)
  if (firefoxFrame) return (
    <><span className="text-sky-300">{firefoxFrame[1]}</span><span className="text-slate-400">@</span><StackLocation value={firefoxFrame[2]} /></>
  )
  return <span className="text-slate-300">{line}</span>
}

export function RouteErrorScreen({ standalone = false }: { standalone?: boolean }) {
  const error = useRouteError()
  const [copyStatus, setCopyStatus] = useState<"copied" | "failed" | null>(null)
  const missing = isRouteErrorResponse(error) && error.status === 404
  const hasStack = error instanceof Error && !!error.stack
  const details = isRouteErrorResponse(error)
    ? `${error.status} ${error.statusText}`
    : error instanceof Error ? error.stack || error.message : String(error ?? "Unknown error")
  const lines = details.split(/\r?\n/)

  useEffect(() => {
    if (copyStatus !== "copied") return
    const timer = window.setTimeout(() => setCopyStatus(null), 2000)
    return () => window.clearTimeout(timer)
  }, [copyStatus])

  async function copyDetails() {
    try {
      if (navigator.clipboard?.writeText) {
        await navigator.clipboard.writeText(details)
      } else {
        const active = document.activeElement
        const input = document.createElement("textarea")
        input.value = details
        input.readOnly = true
        input.style.cssText = "position:fixed;opacity:0;pointer-events:none"
        document.body.append(input)
        try {
          input.select()
          if (!document.execCommand("copy")) throw new Error("Copy failed")
        } finally {
          input.remove()
          if (active instanceof HTMLElement) active.focus({ preventScroll: true })
        }
      }
      setCopyStatus("copied")
    } catch {
      setCopyStatus("failed")
    }
  }

  return (
    <section
      aria-labelledby="route-error-heading"
      className={`min-h-0 overflow-y-auto bg-background px-8 pb-10 font-sans text-foreground md:px-20 xl:px-32 ${standalone
        ? "h-full pt-[calc(2.5rem+var(--tracker-content-offset,0px))] md:pt-[calc(3rem+var(--tracker-content-offset,0px))]"
        : "flex-1 pt-10 md:pt-12"}`}
    >
      <div className="mx-auto w-full max-w-5xl space-y-6">
        <div role="alert" className="flex items-start gap-3">
          <AlertCircle aria-hidden="true" className="mt-1 h-5 w-5 shrink-0 text-muted-foreground" />
          <div className="min-w-0 space-y-3">
            <h1 id="route-error-heading" className="text-xl font-semibold leading-7">
              {missing ? "Page not found" : "Unable to display this page"}
            </h1>
            <p className="text-sm text-muted-foreground">
              {missing
                ? "This address does not match a page in Tracker."
                : "Try reloading the page, or return to the dashboard."}
            </p>
          </div>
        </div>
        <div className="flex flex-wrap gap-2 pl-8">
          {!missing && (
            <Button size="sm" onClick={() => window.location.reload()}>
              <RotateCw aria-hidden="true" className="mr-2 h-3.5 w-3.5" />
              Reload page
            </Button>
          )}
          <Button size="sm" variant="outline" asChild>
            <Link to="/">Go to dashboard</Link>
          </Button>
        </div>
        <section aria-labelledby="error-trace-heading" className="overflow-hidden rounded-lg border border-slate-800 bg-slate-950 font-mono text-xs text-slate-300">
          <div className="flex items-center gap-2.5 bg-white/[0.03] px-4 py-3">
            <Terminal aria-hidden="true" className="h-3.5 w-3.5 text-slate-400" />
            <h2 id="error-trace-heading" className="font-normal">{hasStack ? "Stack trace" : "Error details"}</h2>
            <Button type="button" size="sm" variant="ghost" onClick={() => void copyDetails()}
              aria-label={hasStack ? "Copy stack trace" : "Copy error details"}
              className="-my-1 ml-auto h-6 gap-1.5 px-2 font-sans text-xs text-slate-400 hover:bg-white/5 hover:text-slate-100">
              {copyStatus === "copied" ? <Check aria-hidden="true" className="h-3.5 w-3.5" /> : <Copy aria-hidden="true" className="h-3.5 w-3.5" />}
              {copyStatus === "copied" ? "Copied" : "Copy"}
            </Button>
            <span role="status" className="sr-only">{copyStatus === "copied" ? "Copied to clipboard." : ""}</span>
          </div>
          {copyStatus === "failed" && <p role="status" className="border-t border-slate-800 px-4 py-2 text-rose-300">
            Could not copy. Select the text below to copy it manually.
          </p>}
          <pre aria-label={hasStack ? "Error stack trace" : "Error details"} tabIndex={0} className="max-h-[45vh] select-text overflow-auto whitespace-pre border-t border-slate-800 p-4 font-mono text-xs leading-6 outline-none focus-visible:ring-1 focus-visible:ring-inset focus-visible:ring-ring">
            <code>{lines.map((line, index) => (
              <Fragment key={index}>
                <StackLine line={line} first={index === 0} />
                {index < lines.length - 1 ? "\n" : null}
              </Fragment>
            ))}</code>
          </pre>
        </section>
      </div>
    </section>
  )
}
