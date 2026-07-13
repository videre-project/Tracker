/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

const { Readable } = require("node:stream")
const readline = require("node:readline")

const MANIFEST_URL =
  "https://raw.githubusercontent.com/videre-project/CardExporter/main/manifests/mtgo-cdn.csv"
const IGNORED_NAMESPACES = new Set(["cards", "products"])
const ASSET_PATTERNS = {
  "card-counters": /^[a-z0-9]+(?:-[a-z0-9]+)*\.svg$/,
  "mana-symbols": /^[A-Z0-9]+\.svg$/,
  "mtgo-chat-symbols": /^[a-z0-9]+(?:-[a-z0-9]+)*\.(?:png|svg)$/,
  "player-counters": /^[a-z0-9]+(?:-[a-z0-9]+)*\.png$/,
  "set-symbols": /^[A-Z0-9]+-(?:common|uncommon|rare|mythic|bonus|timeshifted)\.png$/,
}

function readFirstCsvField(line) {
  if (!line.startsWith('"')) return line.split(",", 1)[0]

  let field = ""
  for (let index = 1; index < line.length; index += 1) {
    const character = line[index]
    if (character !== '"') {
      field += character
      continue
    }
    if (line[index + 1] === '"') {
      field += '"'
      index += 1
      continue
    }
    return field
  }
  throw new Error("Malformed quoted CSV field")
}

async function main() {
  const response = await fetch(MANIFEST_URL, {
    signal: AbortSignal.timeout(30_000),
  })
  if (!response.ok || !response.body) {
    throw new Error(`Manifest request failed with HTTP ${response.status}`)
  }

  const counts = Object.fromEntries(
    Object.keys(ASSET_PATTERNS).map(namespace => [namespace, 0]),
  )
  const errors = []
  const lines = readline.createInterface({
    input: Readable.fromWeb(response.body),
    crlfDelay: Infinity,
  })

  for await (const line of lines) {
    if (!line.trim()) continue
    const assetPath = readFirstCsvField(line)
    const separator = assetPath.indexOf("/")
    if (separator < 1) continue

    const namespace = assetPath.slice(0, separator)
    if (IGNORED_NAMESPACES.has(namespace)) continue

    const pattern = ASSET_PATTERNS[namespace]
    if (!pattern) {
      errors.push(`Unknown asset namespace: ${namespace}`)
      continue
    }

    const fileName = assetPath.slice(separator + 1)
    if (!pattern.test(fileName)) {
      errors.push(`Unexpected ${namespace} path: ${assetPath}`)
      continue
    }
    counts[namespace] += 1
  }

  if (errors.length > 0) {
    throw new Error([...new Set(errors)].join("\n"))
  }

  console.log("Validated MTGO CDN asset conventions:")
  for (const [namespace, count] of Object.entries(counts)) {
    console.log(`  ${namespace}: ${count}`)
  }
}

main().catch(error => {
  console.error(error instanceof Error ? error.message : error)
  process.exitCode = 1
})
