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
if [ -n "${VIDERE_NPM_REGISTRY:-}" ]; then
  echo "Synchronizing client dependencies from ${VIDERE_NPM_REGISTRY}..."
  pnpm install --frozen-lockfile --config.@videreproject:registry="${VIDERE_NPM_REGISTRY}"
else
  echo "Synchronizing client dependencies..."
  pnpm install --frozen-lockfile
fi

# Run Vite dev server
exec pnpm exec vite --host 0.0.0.0 --port 5279 --force
