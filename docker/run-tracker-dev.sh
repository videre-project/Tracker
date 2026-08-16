#!/bin/bash
set -euo pipefail

# Development mode: run the .NET host alongside the external Vite service.
# This keeps React HMR in the separate vite-dev container.

# Run in CI mode to avoid TTY issues with pnpm
export CI=true

# Enable dev container mode for SpaProxy config
export TRACKER_DEV_CONTAINER=true


skip_mtgo=false
if [[ "${TRACKER_SKIP_MTGO:-0}" =~ ^(1|true|yes|on)$ ]]; then
  skip_mtgo=true
fi

if [[ "$skip_mtgo" == false ]]; then
  bootstrap=/home/wine/mtgo_setup.exe
  persisted_bootstrap=/home/wine/.wine/mtgo_setup.exe

  if [[ ! -f "$bootstrap" && -f "$persisted_bootstrap" ]]; then
    cp "$persisted_bootstrap" "$bootstrap"
  fi

  if [[ ! -f "$bootstrap" ]]; then
    install-mtgo.sh
    cp "$bootstrap" "$persisted_bootstrap"
  fi
fi

# Wine's accelerated WPF composition currently renders MTGO as a black surface.
# We instead have WPF fall back to software rendering for compatibility with Wine.
wine reg add 'HKCU\Software\Microsoft\Avalon.Graphics' \
  /v DisableHWAcceleration /t REG_DWORD /d 1 /f >/dev/null

#
# WebView2 currently attempts DirectComposition when it inherits a modern
# Windows version under Wine. Wine bug 58921 documents that WebView2 renders
# when only msedgewebview2.exe is reported as Windows 8.
#
# See https://bugs.winehq.org/show_bug.cgi?id=58921 for details.
#
wine reg add 'HKCU\Software\Wine\AppDefaults\msedgewebview2.exe' \
  /v Version /t REG_SZ /d win8 /f >/dev/null

if [[ "$skip_mtgo" == true ]]; then
  echo "Skipping MTGO launch (TRACKER_SKIP_MTGO is enabled)."
else
  # The ClickOnce bootstrapper detaches after starting MTGO.
  mtgo
fi

# Install client dependencies if needed
if [[ ! -d "/workspace/src/client/node_modules" ]]; then
  echo "Installing client dependencies..."
  cd /workspace/src/client && pnpm install --ignore-scripts
fi

# Build server if needed
echo "Building server..."
cd /workspace/src/server && dotnet restore --force-evaluate && dotnet build -c Debug

# Start socat proxy to forward localhost:5279 to vite-dev:5279
# This allows WebView2 in Wine to access the Vite dev server via localhost:5279
echo "Starting socat proxy for Vite dev server..."
socat TCP-LISTEN:5279,fork,reuseaddr TCP:vite-dev:5279 &
socat_pid=$!

echo "Launching Videre Tracker with SpaProxy (proxies to vite-dev:5279)..."
cd /workspace/src/server
export ASPNETCORE_ENVIRONMENT=Development
export DOTNET_WATCH=1
export DOTNET_ROOT='Z:\home\wine\windows-dotnet'
# Run the Windows-targeted DLL using Wine's dotnet (requires .NET Desktop Runtime in Wine)
wine /home/wine/windows-dotnet/dotnet.exe bin/Debug/net10.0-windows7.0/win-x64/Videre\ Tracker.dll &
dotnet_pid=$!

# Wait for the .NET process without allowing set -e to skip cleanup on failure.
set +e
wait "$dotnet_pid"
exit_code=$?
set -e

# Clean up socat
kill "$socat_pid" 2>/dev/null || true
wineserver -w
exit "$exit_code"
