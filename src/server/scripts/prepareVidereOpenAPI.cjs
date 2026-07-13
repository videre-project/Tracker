/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

const fs = require("node:fs/promises")
const path = require("node:path")

const OPENAPI_URL = "https://api.videreproject.com/openapi.json"
const REQUEST_TIMEOUT_MS = 15_000

function countEnums(value) {
  if (!value || typeof value !== "object") return 0
  let count = Array.isArray(value.enum) ? 1 : 0
  for (const child of Object.values(value)) count += countEnums(child)
  return count
}

function resolveReference(document, reference) {
  if (!reference.startsWith("#/")) {
    throw new Error(`Unsupported external OpenAPI reference: ${reference}`)
  }

  return reference.slice(2).split("/").reduce((value, segment) => {
    const key = segment.replace(/~1/g, "/").replace(/~0/g, "~")
    if (!value || typeof value !== "object" || !(key in value)) {
      throw new Error(`Unresolved OpenAPI reference: ${reference}`)
    }
    return value[key]
  }, document)
}

function mergeSchemas(base, extension) {
  const merged = { ...base, ...extension }

  if (base.properties || extension.properties) {
    merged.properties = {
      ...(base.properties ?? {}),
      ...(extension.properties ?? {}),
    }
  }

  if (base.required || extension.required) {
    merged.required = [...new Set([
      ...(base.required ?? []),
      ...(extension.required ?? []),
    ])]
  }

  return merged
}

function flattenCompositions(document, value, references = []) {
  if (!value || typeof value !== "object") return value
  if (Array.isArray(value)) {
    return value.map(item => flattenCompositions(document, item, references))
  }

  const flattened = Object.fromEntries(Object.entries(value)
    .filter(([key]) => key !== "allOf")
    .map(([key, child]) => [key, flattenCompositions(document, child, references)]))

  if (!Array.isArray(value.allOf)) return flattened

  let merged = {}
  for (const branch of value.allOf) {
    if (branch?.$ref) {
      if (references.includes(branch.$ref)) {
        throw new Error(`Circular OpenAPI composition: ${[...references, branch.$ref].join(" -> ")}`)
      }
      merged = mergeSchemas(merged, flattenCompositions(
        document,
        resolveReference(document, branch.$ref),
        [...references, branch.$ref],
      ))
      continue
    }

    merged = mergeSchemas(merged, flattenCompositions(document, branch, references))
  }

  return mergeSchemas(merged, flattened)
}

async function main() {
  const outputPath = process.argv[2]
  if (!outputPath) {
    throw new Error("Usage: node prepareVidereOpenAPI.cjs <output-path>")
  }

  const response = await fetch(OPENAPI_URL, {
    headers: { accept: "application/json" },
    signal: AbortSignal.timeout(REQUEST_TIMEOUT_MS),
  })
  if (!response.ok) {
    throw new Error(`Videre OpenAPI request failed with HTTP ${response.status}`)
  }

  let document = await response.json()
  if (!document?.openapi || !document?.paths || !document?.components?.schemas) {
    throw new Error("Videre returned a malformed OpenAPI document")
  }
  document = flattenCompositions(document, document)

  const resolvedOutputPath = path.resolve(outputPath)
  await fs.mkdir(path.dirname(resolvedOutputPath), { recursive: true })
  await fs.writeFile(resolvedOutputPath, `${JSON.stringify(document, null, 2)}\n`, "utf8")
  console.log(`Cached Videre OpenAPI schema with ${countEnums(document)} enum locations.`)
}

main().catch(error => {
  console.error(error instanceof Error ? error.message : error)
  process.exitCode = 1
})
