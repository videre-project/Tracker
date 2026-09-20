/** @file
  Copyright (c) 2026, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { fileURLToPath, URL } from 'node:url';

import { defineConfig, searchForWorkspaceRoot } from 'vite';
import react from '@vitejs/plugin-react';
import fs from 'fs';
import path from 'path';
import child_process from 'child_process';
import { env } from 'process';

import rootPackageJson from '../../package.json';
import sourcePaths from './tsconfig.paths.json';


const baseFolder =
  process.env.APPDATA !== undefined && process.env.APPDATA !== ''
    ? `${process.env.APPDATA}/ASP.NET/https`
    : `${process.env.HOME}/.aspnet/https`;

const certificateName = "client";
const certFilePath = path.join(baseFolder, `${certificateName}.pem`);
const keyFilePath = path.join(baseFolder, `${certificateName}.key`);

// The Wine dev container reaches Vite over the Docker bridge and cannot trust
// the self-signed dev certificate, so it needs a plain-HTTP listener on all
// interfaces. Enable with DEV_SERVER_HTTP=1 (see `pnpm dev:client`).
const httpExpose = env.DEV_SERVER_HTTP === '1' || env.DEV_SERVER_HTTP === 'true';

// Skip certificate generation when serving plain HTTP (no dotnet needed)
if (!httpExpose) {
  if (!fs.existsSync(baseFolder)) {
    fs.mkdirSync(baseFolder, { recursive: true });
  }

  if (!fs.existsSync(certFilePath) || !fs.existsSync(keyFilePath)) {
    if (0 !== child_process.spawnSync('dotnet', [
        'dev-certs',
        'https',
        '--export-path',
        certFilePath,
        '--format',
        'Pem',
        '--no-password',
    ], { stdio: 'inherit', }).status) {
      throw new Error("Could not create certificate.");
    }
  }

  // Trust the dev certificate for Node.js (fixes TLS errors with self-signed certs)
  process.env.NODE_EXTRA_CA_CERTS = certFilePath;
}

const targetOverride = env.TRACKER_BACKEND_URL || env.VITE_BACKEND_URL;

const aspnetcoreUrls = (env.ASPNETCORE_URLS ?? '')
  .split(';')
  .map((u) => u.trim())
  .filter(Boolean);

const aspnetcoreHttpsUrl = aspnetcoreUrls.find((u) => u.startsWith('https://'));
const aspnetcoreFirstUrl = aspnetcoreUrls[0];

// On the host, the .NET API (run in the tracker-dev container) is published
// on 127.0.0.1:7101.
const target = targetOverride
  ? targetOverride
  : env.ASPNETCORE_HTTPS_PORT
    ? `https://localhost:${env.ASPNETCORE_HTTPS_PORT}`
    : aspnetcoreHttpsUrl
      ? aspnetcoreHttpsUrl
      : aspnetcoreFirstUrl
        ? aspnetcoreFirstUrl
        : 'https://localhost:7101';

console.log(`[vite] backend proxy target: ${target}`);

// TypeScript and Vite resolve the same source entry points. The relative
// sibling checkout path is identical on the host and at /platform in Docker.
const localUiIndex = path.resolve(__dirname, sourcePaths.compilerOptions.paths['@videreproject/ui'][0]);
const localUiRoot = fs.existsSync(localUiIndex) ? path.dirname(path.dirname(localUiIndex)) : null;
if (localUiRoot) {
  console.log(`[vite] using local @videreproject/ui from ${localUiRoot}`);
}

const alias: Array<{ find: string | RegExp; replacement: string }> = [
  { find: '@', replacement: fileURLToPath(new URL('./src', import.meta.url)) },
];

if (localUiRoot) {
  for (const [name, candidates] of Object.entries(sourcePaths.compilerOptions.paths)) {
    if (name.startsWith('@videreproject/ui')) {
      alias.unshift({ find: name, replacement: path.resolve(__dirname, candidates[0]) });
    }
  }
}

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [
    react()
  ],
  define: {
    __APP_VERSION__: JSON.stringify(rootPackageJson.version),
  },
  resolve: {
    alias,
  },
  optimizeDeps: {
    // Local UI source must not be prebundled; otherwise Tracker keeps serving
    // a stale copy from node_modules/.vite/deps after platform edits.
    exclude: localUiRoot ? ['@videreproject/ui'] : [],
  },
  server: {
    fs: {
      allow: [
        searchForWorkspaceRoot(__dirname),
        ...(localUiRoot ? [path.resolve(localUiRoot, '../..')] : []),
      ],
    },
    host: httpExpose,
    port: parseInt(env.DEV_SERVER_PORT || '5279'),
    strictPort: true,
    // The Wine dev container loads the UI over plain HTTP (WebView2 cannot
    // trust the self-signed dev certificate); host development uses HTTPS.
    https: httpExpose ? undefined : {
      key: fs.readFileSync(keyFilePath),
      cert: fs.readFileSync(certFilePath)
    },
    // Allow requests from the container's Docker-bridge hostname
    allowedHosts: httpExpose
      ? ['host.docker.internal', 'localhost', '127.0.0.1', '0.0.0.0']
      : undefined,
    proxy: {
      '/api': {
        target,
        secure: false
      },
      '/docs': {
        target,
        secure: false
      },
      '/openapi': {
        target,
        secure: false
      }
    }
  }
});
