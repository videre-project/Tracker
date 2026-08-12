#!/bin/sh
set -eu

# Development mode: Run Vite dev server only (for frontend HMR)
# The .NET API must run separately (on Windows or in production container)

# Run in CI mode to avoid TTY issues with pnpm
export CI=true

echo "Starting Vite dev server for frontend HMR..."
cd /workspace/src/client

# The source tree is bind-mounted while node_modules lives in a named volume.
# Reconcile it on every start so package and lockfile changes are picked up.
videre_registry="${VIDERE_NPM_REGISTRY:-http://host.docker.internal:4873/}"
echo "Synchronizing client dependencies from ${videre_registry}..."
pnpm install --frozen-lockfile --config.@videreproject:registry="$videre_registry"

# Run Vite dev server
exec pnpm exec vite --host 0.0.0.0 --port 5279 --force
