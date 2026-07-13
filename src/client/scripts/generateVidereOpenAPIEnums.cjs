/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

const fs = require("node:fs")
const path = require("node:path")

function extractEnums(value, key) {
  if (!value || typeof value !== "object") return undefined
  if (Array.isArray(value.enum)) return value.enum

  if (Array.isArray(value)) {
    if (key === "parameters") {
      const parameters = Object.fromEntries(value.flatMap(parameter => {
        if (!parameter?.name) return []
        const extracted = extractEnums(parameter.schema, "schema")
        return extracted === undefined ? [] : [[parameter.name, extracted]]
      }))
      return Object.keys(parameters).length > 0 ? parameters : undefined
    }

    const items = value
      .map(item => extractEnums(item))
      .filter(item => item !== undefined)
    return items.length > 0 ? items : undefined
  }

  const entries = Object.entries(value).flatMap(([childKey, child]) => {
    const extracted = extractEnums(child, childKey)
    return extracted === undefined ? [] : [[childKey, extracted]]
  })
  return entries.length > 0 ? Object.fromEntries(entries) : undefined
}

function isEnumValues(value) {
  return Array.isArray(value) && value.every(item =>
    item === null || ["string", "number", "boolean"].includes(typeof item)
  )
}

const STRUCTURAL_ENUM_KEYS = new Set(["items", "additionalProperties", "oneOf"])

function collectEnumValues(value, occurrences, names, order, path = []) {
  if (!value || typeof value !== "object") return
  if (isEnumValues(value)) {
    const signature = JSON.stringify(value)
    if (!occurrences.has(signature)) {
      order.push(signature)
      names.set(signature, [...path].reverse().find(key =>
        !STRUCTURAL_ENUM_KEYS.has(key)
      ) ?? "enum")
    }
    occurrences.set(signature, (occurrences.get(signature) ?? 0) + 1)
    return
  }

  for (const [key, child] of Object.entries(value)) {
    collectEnumValues(child, occurrences, names, order, [...path, key])
  }
}

function createEnumIdentifier(name, usedIdentifiers) {
  const base = `VIDERE_${name.replace(/[^a-z0-9]+/gi, "_").toUpperCase()}_VALUES`
  const count = (usedIdentifiers.get(base) ?? 0) + 1
  usedIdentifiers.set(base, count)
  return count === 1 ? base : `${base}_${count}`
}

function renderValue(value, indent, sharedEnums) {
  if (isEnumValues(value)) {
    return sharedEnums.get(JSON.stringify(value)) ?? JSON.stringify(value, null, 2)
      .split("\n")
      .map((line, index) => index === 0 ? line : `${" ".repeat(indent)}${line}`)
      .join("\n")
  }

  if (Array.isArray(value)) {
    if (value.length === 0) return "[]"
    const padding = " ".repeat(indent)
    const childPadding = " ".repeat(indent + 2)
    return `[\n${value.map(item =>
      `${childPadding}${renderValue(item, indent + 2, sharedEnums)}`
    ).join(",\n")}\n${padding}]`
  }

  if (value && typeof value === "object") {
    const entries = Object.entries(value)
    if (entries.length === 0) return "{}"
    const padding = " ".repeat(indent)
    const childPadding = " ".repeat(indent + 2)
    return `{\n${entries.map(([key, child]) =>
      `${childPadding}${JSON.stringify(key)}: ${renderValue(child, indent + 2, sharedEnums)}`
    ).join(",\n")}\n${padding}}`
  }

  return JSON.stringify(value)
}

function main() {
  const inputPath = process.argv[2]
  const outputPath = process.argv[3]
  if (!inputPath || !outputPath) {
    throw new Error(
      "Usage: node generateVidereOpenAPIEnums.cjs <videre-openapi-path> <output-path>"
    )
  }

  const document = JSON.parse(fs.readFileSync(inputPath, "utf8"))
  if (!document?.paths || !document?.components?.schemas) {
    throw new Error("Videre returned a malformed OpenAPI document")
  }

  const enums = extractEnums(document)
  if (!enums) throw new Error("Videre OpenAPI does not define any enums")

  const occurrences = new Map()
  const enumNames = new Map()
  const enumOrder = []
  collectEnumValues(enums, occurrences, enumNames, enumOrder)
  const usedIdentifiers = new Map()
  const sharedEnums = new Map(enumOrder
    .filter(signature => occurrences.get(signature) > 1)
    .map(signature => [
      signature,
      createEnumIdentifier(enumNames.get(signature), usedIdentifiers),
    ]))

  const sharedDeclarations = [...sharedEnums].map(([signature, identifier]) =>
    `const ${identifier} = ${JSON.stringify(JSON.parse(signature))} as const`
  ).join("\n")

  const content = `/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

// Generated from Videre's OpenAPI contract by the server build. Do not edit directly.

${sharedDeclarations ? `${sharedDeclarations}\n\n` : ""}export const VIDERE_OPENAPI_ENUMS = ${renderValue(enums, 0, sharedEnums)} as const
`

  const resolvedOutputPath = path.resolve(outputPath)
  fs.mkdirSync(path.dirname(resolvedOutputPath), { recursive: true })
  fs.writeFileSync(resolvedOutputPath, content, "utf8")
  console.log(`Generated ${path.relative(process.cwd(), resolvedOutputPath)} from Videre OpenAPI.`)
}

try {
  main()
} catch (error) {
  console.error(error instanceof Error ? error.message : error)
  process.exitCode = 1
}
