/** @file
  Copyright (c) 2026, The Videre Project Authors. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { createHash } from 'node:crypto'
import { createReadStream } from 'node:fs'
import { stat } from 'node:fs/promises'
import { createServer } from 'node:http'
import { resolve } from 'node:path'

const [version, executable, portValue = '8750'] = process.argv.slice(2)
if (!/^\d+\.\d+\.\d+$/.test(version ?? '') || !executable) {
  throw new Error('Usage: node docker/update-feed.mjs <version> <published-exe> [port]')
}
const port = Number(portValue)
if (!Number.isInteger(port) || port < 1 || port > 65535) throw new Error('Invalid port.')
const path = resolve(executable)
const info = await stat(path)
if (!info.isFile()) throw new Error('The published executable must be a file.')
const hash = createHash('sha256')
for await (const chunk of createReadStream(path)) hash.update(chunk)
const release = JSON.stringify({
  tag_name: `v${version}`,
  name: `Local Tracker ${version}`,
  body: `Local upgrade verification build ${version}.`,
  draft: false,
  prerelease: false,
  published_at: new Date().toISOString(),
  assets: [{
    name: 'Videre.Tracker.exe',
    label: 'Videre Tracker.exe',
    browser_download_url: '/Videre.Tracker.exe',
    size: info.size,
    digest: `sha256:${hash.digest('hex')}`,
  }],
})
createServer((request, response) => {
  if (request.method !== 'GET') { response.writeHead(405).end(); return }
  const pathname = new URL(request.url, 'http://localhost').pathname
  if (pathname === '/release.json') {
    response.writeHead(200, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' }).end(release)
  } else if (pathname === '/Videre.Tracker.exe') {
    response.writeHead(200, { 'Content-Type': 'application/octet-stream', 'Content-Length': info.size })
    const stream = createReadStream(path)
    stream.on('error', () => response.destroy())
    response.on('close', () => stream.destroy())
    stream.pipe(response)
  } else response.writeHead(404).end()
}).listen(port, '127.0.0.1', () => {
  console.log(`Updates__FeedUrl=http://127.0.0.1:${port}/release.json`)
  console.log(`Serving Tracker ${version} (${info.size} bytes) from ${path}`)
})
