/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

const fs = require("node:fs/promises")
const path = require("node:path")

const OPENAPI_URL = "https://api.videreproject.com/openapi.json"
const REQUEST_TIMEOUT_MS = 15_000

async function main() {
  const outputPath = process.argv[2]
  if (!outputPath) {
    throw new Error("Usage: node prepareVidereOpenAPI.cjs <output-path>")
  }

  const response = await fetch(OPENAPI_URL, {
    headers: { accept: "application/json" },
    signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
  })
  if (!response.ok) throw new Error(`Videre OpenAPI request failed with HTTP ${response.status}`)
  const document = await response.json()
  if (!document?.openapi || !document?.paths || !document?.components?.schemas) {
    throw new Error("Videre returned a malformed OpenAPI document")
  }

  const resolvedOutputPath = path.resolve(outputPath)
  await fs.mkdir(path.dirname(resolvedOutputPath), { recursive: true })
  await fs.writeFile(resolvedOutputPath, `${JSON.stringify(document, null, 2)}\n`, "utf8")
  const { default: openapiTS, astToString } = await import("openapi-typescript")
  const typesPath = path.resolve(__dirname, "../../client/src/types/videre.g.d.ts")
  await fs.writeFile(typesPath, astToString(await openapiTS(document)), "utf8")
  console.log(`Prepared Videre OpenAPI schema with ${Object.keys(document.components.schemas).length} named schemas.`)
}

main().catch(error => {
  console.error(error instanceof Error ? error.message : error)
  process.exitCode = 1
})
