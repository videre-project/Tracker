/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { execFileSync } from "node:child_process"
import { appendFileSync, readFileSync } from "node:fs"

function git(args, options = {}) {
  try {
    return execFileSync("git", args, {
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
      ...options,
    }).trim()
  } catch {
    return ""
  }
}

function versionFromPackageJson(source) {
  try {
    return JSON.parse(source).version ?? ""
  } catch {
    return ""
  }
}

const current = versionFromPackageJson(readFileSync("package.json", "utf8"))
const previous = versionFromPackageJson(git(["show", "HEAD^:package.json"]))
const tag = current ? `v${current}` : ""

const remoteTag = tag
  ? git(["ls-remote", "--tags", "origin", `refs/tags/${tag}`])
  : ""
const localTag = tag
  ? git(["rev-parse", "-q", "--verify", `refs/tags/${tag}`])
  : ""
const tagExists = Boolean(remoteTag || localTag)

const shouldRelease = Boolean(
  current &&
  current !== "0.0.0" &&
  current !== previous &&
  !tagExists,
)

const summary = { current, previous, tag, tagExists, shouldRelease }
console.log(JSON.stringify(summary))

if (process.env.GITHUB_OUTPUT) {
  appendFileSync(process.env.GITHUB_OUTPUT, `version=${current}\n`)
  appendFileSync(process.env.GITHUB_OUTPUT, `should_release=${shouldRelease}\n`)
}
