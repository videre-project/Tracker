/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { execFileSync } from "node:child_process"
import { copyFile, mkdir, writeFile } from "node:fs/promises"
import path from "node:path"

function usage() {
  console.error(`Usage:
  node scripts/fixtures/sanitize-event-db.mjs \\
    --input <Event.db> \\
    --output <event.db> \\
    [--metadata <event.json>] \\
    [--source <description>]`)
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
const metadataPath = argumentsMap.get("metadata")
const source = argumentsMap.get(
  "source",
  "Tracker Event.db from the local Wine/Docker bot reference",
)

if (!input || !output) usage()

function sqlite(database, sql) {
  return execFileSync("sqlite3", [database, sql], { encoding: "utf8" }).trim()
}

function query(database, sql) {
  const result = execFileSync(
    "sqlite3",
    ["-readonly", "-json", database, sql],
    { encoding: "utf8" },
  ).trim()
  return result ? JSON.parse(result) : []
}

function one(database, sql) {
  const rows = query(database, sql)
  if (!rows[0]) throw new Error(`Query returned no rows: ${sql}`)
  return rows[0]
}

await mkdir(path.dirname(path.resolve(output)), { recursive: true })
await copyFile(path.resolve(input), path.resolve(output))

const keepMatch = one(
  output,
  `SELECT m.Id, m.EventId
   FROM Matches m
   ORDER BY (SELECT count(*) FROM Games g WHERE g.MatchId = m.Id) DESC,
            (SELECT count(*) FROM GameStates s
             JOIN Games g ON g.Id = s.GameId
             WHERE g.MatchId = m.Id) DESC,
            m.Id
   LIMIT 1`,
)

const dropGameIds = query(
  output,
  `SELECT Id FROM Games WHERE MatchId != ${keepMatch.Id}`,
).map(row => row.Id)
const dropStateFilter = dropGameIds.length === 0
  ? "0"
  : `GameId IN (${dropGameIds.join(",")})`
const dropStateIds = dropGameIds.length === 0
  ? []
  : query(output, `SELECT Id FROM GameStates WHERE ${dropStateFilter}`)
      .map(row => row.Id)
const dropStateIdFilter = dropStateIds.length === 0
  ? "0"
  : `GameStateId IN (${dropStateIds.join(",")})`

sqlite(output, "PRAGMA foreign_keys = OFF")
if (dropStateIds.length > 0) {
  sqlite(output, `DELETE FROM GameLogs WHERE ${dropStateIdFilter}`)
  sqlite(output, `DELETE FROM GameActions WHERE ${dropStateIdFilter}`)
  sqlite(output, `DELETE FROM ZoneTransfers WHERE ${dropStateIdFilter}`)
  sqlite(output, `DELETE FROM CardStateChanges WHERE ${dropStateIdFilter}`)
  sqlite(output, `DELETE FROM PlayerStateChanges WHERE ${dropStateIdFilter}`)
  sqlite(output, `DELETE FROM GameStates WHERE ${dropStateFilter}`)
}
if (dropGameIds.length > 0) {
  sqlite(output, `DELETE FROM GameCards WHERE ${dropStateFilter}`)
  sqlite(output, `DELETE FROM GamePlayers WHERE ${dropStateFilter}`)
  sqlite(output, `DELETE FROM Games WHERE MatchId != ${keepMatch.Id}`)
}
sqlite(output, `DELETE FROM Matches WHERE Id != ${keepMatch.Id}`)
sqlite(output, `DELETE FROM Events WHERE Id != ${keepMatch.EventId}`)
sqlite(output, `DELETE FROM GameLogs
  WHERE GameLogType LIKE '%Chat%'
     OR Data LIKE '%chat%'
     OR Data LIKE '%whisper%'`)

const names = query(
  output,
  "SELECT DISTINCT Name FROM GamePlayers ORDER BY Name",
).map(row => row.Name)
const replacements = names.map((name, index) => ({
  from: name,
  to: `Player${String.fromCharCode(65 + index)}`,
}))
const userIds = query(
  output,
  "SELECT DISTINCT UserId FROM GamePlayers WHERE UserId > 0 ORDER BY UserId",
).map(row => row.UserId)

const textColumns = [
  ["Events", "Description"],
  ["Events", "Format"],
  ["Matches", "PlayerResults"],
  ["Matches", "OpponentDeckArchetype"],
  ["Games", "GamePlayerResults"],
  ["GamePlayers", "Name"],
  ["GameStates", "PromptText"],
  ["GameStates", "PromptOptions"],
  ["GameLogs", "Data"],
  ["GameActions", "ActionName"],
  ["GameActions", "Data"],
  ["GameCards", "Name"],
  ["ZoneTransfers", "CardName"],
  ["CardStateChanges", "CardName"],
  ["CardStateChanges", "OldValue"],
  ["CardStateChanges", "NewValue"],
  ["PlayerStateChanges", "PlayerName"],
  ["PlayerStateChanges", "OldValue"],
  ["PlayerStateChanges", "NewValue"],
]

for (const { from, to } of [...replacements].sort(
  (left, right) => right.from.length - left.from.length,
)) {
  const escapedFrom = from.replaceAll("'", "''")
  const escapedTo = to.replaceAll("'", "''")
  for (const [table, column] of textColumns) {
    sqlite(output, `UPDATE ${table}
      SET ${column} = REPLACE(${column}, '${escapedFrom}', '${escapedTo}')
      WHERE ${column} IS NOT NULL AND instr(${column}, '${escapedFrom}') > 0`)
  }
}

userIds.forEach((userId, index) => {
  sqlite(output, `UPDATE GamePlayers SET UserId = ${index + 1}
    WHERE UserId = ${userId}`)
})

sqlite(output, "VACUUM")

const games = query(
  output,
  `SELECT Id FROM Games WHERE MatchId = ${keepMatch.Id} ORDER BY Id`,
)
const openingHands = {}
for (const game of games) {
  const keepState = query(
    output,
    `SELECT s.Id
     FROM GameStates s
     JOIN GameActions a ON a.GameStateId = s.Id
     WHERE s.GameId = ${game.Id}
       AND (a.ActionName = 'Keep' OR a.Data LIKE '%"name": "Keep"%')
     ORDER BY s.Id
     LIMIT 1`,
  )[0]
  const transfers = query(
    output,
    `SELECT z.CardName AS card, z.ToZone AS toZone
     FROM ZoneTransfers z
     JOIN GameStates s ON s.Id = z.GameStateId
     WHERE s.GameId = ${game.Id}
       ${keepState ? `AND s.Id < ${keepState.Id}` : ""}
       AND z.ToZone = 'Hand'
     ORDER BY s.Id, z.Id`,
  )
  const counts = {}
  for (const transfer of transfers) {
    counts[transfer.card] = (counts[transfer.card] ?? 0) + 1
  }
  openingHands[game.Id] = counts
}

const metadata = {
  fixtureVersion: 1,
  scenario: "bot-vs-bot multi-game replay with sideboarding",
  source,
  sanitized: true,
  database: path.basename(output),
  eventId: keepMatch.EventId,
  matchId: keepMatch.Id,
  gameIds: games.map(game => game.Id),
  openingHands,
  redactions: [
    "player names",
    "account user ids",
    "chat and whisper logs",
    "unrelated matches",
  ],
}

if (metadataPath) {
  await mkdir(path.dirname(path.resolve(metadataPath)), { recursive: true })
  await writeFile(
    path.resolve(metadataPath),
    `${JSON.stringify(metadata, null, 2)}\n`,
  )
}

const leftover = sqlite(
  output,
  "SELECT count(*) FROM GamePlayers WHERE Name LIKE '%Bot%' OR Name LIKE '%Videre%'",
)
if (leftover !== "0") {
  throw new Error("Sanitization left bot or Videre names in GamePlayers")
}

console.log(`Wrote sanitized Event.db: ${output}`)
if (metadataPath) console.log(`Wrote fixture metadata: ${metadataPath}`)
