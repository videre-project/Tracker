/** @file
  Copyright (c) 2023, Cory Bennett. All rights reserved.
  SPDX-License-Identifier: Apache-2.0
**/

import { fileURLToPath, URL } from 'node:url';

import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import fs from 'fs';
import path from 'path';
import child_process from 'child_process';
import { env } from 'process';

import rootPackageJson from '../../package.json';


const baseFolder =
  process.env.APPDATA !== undefined && process.env.APPDATA !== ''
    ? `${process.env.APPDATA}/ASP.NET/https`
    : `${process.env.HOME}/.aspnet/https`;

const certificateName = "client";
const certFilePath = path.join(baseFolder, `${certificateName}.pem`);
const keyFilePath = path.join(baseFolder, `${certificateName}.key`);

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

const targetOverride = env.TRACKER_BACKEND_URL || env.VITE_BACKEND_URL;

const aspnetcoreUrls = (env.ASPNETCORE_URLS ?? '')
  .split(';')
  .map((u) => u.trim())
  .filter(Boolean);

const aspnetcoreHttpsUrl = aspnetcoreUrls.find((u) => u.startsWith('https://'));
const aspnetcoreFirstUrl = aspnetcoreUrls[0];

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

// https://vitejs.dev/config/
// https://vitejs.dev/config/
export default defineConfig({
  plugins: [
    react()
  ],
  define: {
    __APP_VERSION__: JSON.stringify(rootPackageJson.version),
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url))
    }
  },
  server: {
    port: parseInt(env.DEV_SERVER_PORT || '5279'),
    strictPort: true,
    https: {
      key: fs.readFileSync(keyFilePath),
      cert: fs.readFileSync(certFilePath)
    },
    proxy: {
      '/api': {
        target,
        secure: false
      },
      '/docs': {
        target,
        secure: false
      }
    }
  }
});
