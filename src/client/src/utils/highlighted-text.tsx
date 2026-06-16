import React from "react"

function normalizeSearchText(text?: string) {
  const value = text?.trim()
  return value ? value.toLowerCase() : ""
}

export function HighlightedText({
  text,
  highlight,
  markClassName,
}: {
  text: string
  highlight?: string
  markClassName?: string
}) {
  const needle = normalizeSearchText(highlight)
  if (!needle) return <React.Fragment>{text}</React.Fragment>

  const lowerText = text.toLowerCase()
  const nodes: React.ReactNode[] = []
  let cursor = 0
  let index = 0
  let matchIndex = lowerText.indexOf(needle, cursor)

  while (matchIndex >= 0) {
    if (matchIndex > cursor) {
      nodes.push(
        <React.Fragment key={`t-${index++}`}>
          {text.slice(cursor, matchIndex)}
        </React.Fragment>
      )
    }

    const end = matchIndex + needle.length
    nodes.push(
      <mark
        key={`m-${index++}`}
        className={markClassName ?? "rounded-sm bg-amber-300/35 px-0.5 text-inherit"}
      >
        {text.slice(matchIndex, end)}
      </mark>
    )

    cursor = end
    matchIndex = lowerText.indexOf(needle, cursor)
  }

  if (cursor < text.length) {
    nodes.push(
      <React.Fragment key={`t-${index++}`}>
        {text.slice(cursor)}
      </React.Fragment>
    )
  }

  return <React.Fragment>{nodes}</React.Fragment>
}
