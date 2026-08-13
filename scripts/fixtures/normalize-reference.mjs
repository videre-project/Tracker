#!/usr/bin/env node

import { readFile, writeFile } from "node:fs/promises"
import path from "node:path"

function usage() {
  console.error(`Usage:
  node scripts/fixtures/normalize-reference.mjs \
    --input <json-file> \
    --output <fixture-file> \
    --scenario <name> \
    --source <description> \
    [--expected-behavior <description>] \
    [--coverage <comma,separated,areas>]`)
  process.exit(2)
}

function parseArguments(argv) {
  const values = new Map()
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index]
    if (!argument.startsWith("--") || !argv[index + 1]) usage()
    values.set(argument.slice(2), argv[index + 1])
    index += 1
  }
  return values
}

const argumentsMap = parseArguments(process.argv.slice(2))
const input = argumentsMap.get("input")
const output = argumentsMap.get("output")
const scenario = argumentsMap.get("scenario")
const source = argumentsMap.get("source")
const expectedBehavior = argumentsMap.get("expected-behavior")
const coverage = argumentsMap.get("coverage")
  ?.split(",")
  .map(value => value.trim())
  .filter(Boolean)

if (!input || !output || !scenario || !source) usage()

const raw = JSON.parse(await readFile(input, "utf8"))
const timestampPattern = /^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}/
const sensitiveKeyPattern = /(password|secret|token|credential|cookie|session)/i
const unstableKeyPattern = /(processid|pid|avatarid|userid|accountid|sessionid|remoteid|objectid)/i
const playerKeyPattern = /^(player|username|opponent|partnername)$/i
const playerNames = new Map()

function normalizedPlayerName(value) {
  const key = String(value)
  if (!playerNames.has(key)) {
    playerNames.set(key, `Player${String.fromCharCode(65 + playerNames.size)}`)
  }
  return playerNames.get(key)
}

function normalize(value, key = "", parentKey = "") {
  if (Array.isArray(value)) return value.map(item => normalize(item, key, parentKey))

  if (value && typeof value === "object") {
    return Object.fromEntries(
      Object.entries(value)
        .filter(([entryKey]) => !sensitiveKeyPattern.test(entryKey))
        .sort(([left], [right]) => left.localeCompare(right))
        .map(([entryKey, entryValue]) => [
          entryKey,
          normalize(entryValue, entryKey, key),
        ]),
    )
  }

  if (value == null) return value
  const isNamedPlayer = key.toLowerCase() === "name"
    && /(player|players|opponent|partner|user)/i.test(parentKey)
  if ((playerKeyPattern.test(key) || isNamedPlayer) && typeof value === "string") {
    return normalizedPlayerName(value)
  }
  if (unstableKeyPattern.test(key)) return 1
  if (typeof value === "string" && timestampPattern.test(value)) {
    return "2026-01-01T00:00:00.000Z"
  }
  return value
}

const fixture = {
  metadata: {
    fixtureVersion: 1,
    scenario,
    source,
    sanitized: true,
    ...(expectedBehavior ? { expectedBehavior } : {}),
    ...(coverage?.length ? { coverage } : {}),
    normalizedBy: "scripts/fixtures/normalize-reference.mjs",
    redactions: [
      "player names",
      "timestamps",
      "process/account/avatar identifiers",
      "credential-like fields",
    ],
  },
  data: normalize(raw),
}

await writeFile(
  path.resolve(output),
  `${JSON.stringify(fixture, null, 2)}\n`,
  "utf8",
)
