#!/bin/bash
set -euo pipefail

# Development mode: MTGO + Tracker + Vite in this container.
# Vite is started here unless TRACKER_UI_URL is already reachable
# (for example a host `pnpm dev:client` at host.docker.internal:5279).

# Run in CI mode to avoid TTY issues with pnpm
export CI=true

# Enable dev container mode for SpaProxy config
export TRACKER_DEV_CONTAINER=true

vite_pid=
mtgo_launcher_pid=

cleanup() {
  if [[ -n "${vite_pid:-}" ]]; then
    kill "$vite_pid" 2>/dev/null || true
  fi
  # Background Wine services can remain after both application windows close.
  # This Wine prefix belongs to the development container being shut down.
  wineserver -k 2>/dev/null || true
  if [[ -n "${vite_pid:-}" ]]; then
    wait "$vite_pid" 2>/dev/null || true
  fi
  wineserver -w 2>/dev/null || true
}
trap cleanup EXIT
trap 'exit 143' TERM
trap 'exit 130' INT

ui_reachable() {
  local url=$1
  curl -fsS -o /dev/null --max-time 2 "$url" 2>/dev/null
}


skip_mtgo=false
if [[ "${TRACKER_SKIP_MTGO:-0}" =~ ^(1|true|yes|on)$ ]]; then
  skip_mtgo=true
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

# Disable X11 MIT-SHM. Wine reads this as a Y/N string; a DWORD 0 is ignored
# and WebView2 then aborts on X_ShmPutImage against the host X server.
wine reg delete 'HKCU\Software\Wine\X11 Driver' /v UseXShm /f >/dev/null 2>&1 || true
wine reg add 'HKCU\Software\Wine\X11 Driver' \
  /v UseXShm /t REG_SZ /d N /f >/dev/null

if [[ "$skip_mtgo" == true ]]; then
  echo "Skipping MTGO launch (TRACKER_SKIP_MTGO is enabled)."
else
  # The launcher blocks until MTGO exits, so start it in the background and
  # let Tracker attach to MTGO.exe in this same Wine prefix.
  start_mtgo=/usr/local/bin/start-mtgo
  if [[ ! -x "$start_mtgo" ]]; then
    start_mtgo=/workspace/docker/start-mtgo.sh
  fi
  "$start_mtgo" &
  mtgo_launcher_pid=$!
fi

vite_js=/workspace/src/client/node_modules/vite/bin/vite.js
# Relink from the local pnpm store only. A network install fights npmjs over
# @videreproject packages and can wipe node_modules.
if [[ ! -f "$vite_js" ]]; then
  echo "Installing client dependencies from the local store..."
  cd /workspace/src/client
  pnpm install --offline --ignore-scripts
fi
if [[ ! -f "$vite_js" ]]; then
  echo "Vite is not installed. Run 'pnpm install --offline' in src/client on the host." >&2
  exit 1
fi

ui_url=${TRACKER_UI_URL:-http://127.0.0.1:5279}
if ui_reachable "$ui_url"; then
  echo "Using existing Vite at $ui_url"
else
  export DEV_SERVER_HTTP=1
  export DEV_SERVER_PORT="${DEV_SERVER_PORT:-5279}"
  export TRACKER_BACKEND_URL="${TRACKER_BACKEND_URL:-https://127.0.0.1:7101}"
  ui_url="http://127.0.0.1:${DEV_SERVER_PORT}"
  export TRACKER_UI_URL=$ui_url
  export SPA_PROXY_SERVER_URL=$ui_url
  echo "Starting Vite at $ui_url ..."
  cd /workspace/src/client
  node "$vite_js" --host 0.0.0.0 --port "${DEV_SERVER_PORT}" &
  vite_pid=$!
fi

# Build server if needed
echo "Building server..."
cd /workspace/src/server && dotnet restore --force-evaluate && dotnet build -c Debug

if ! ui_reachable "$ui_url"; then
  echo "Waiting for Vite at $ui_url ..."
  for _ in $(seq 1 60); do
    if ui_reachable "$ui_url"; then
      break
    fi
    if [[ -n "${vite_pid:-}" ]] && ! kill -0 "$vite_pid" 2>/dev/null; then
      echo "Vite exited before becoming ready." >&2
      exit 1
    fi
    sleep 1
  done
  if ! ui_reachable "$ui_url"; then
    echo "Vite did not become ready at $ui_url." >&2
    exit 1
  fi
fi
echo "Vite is ready at $ui_url"

echo "Launching Videre Tracker (UI $ui_url)"
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

# Keep MTGO available when Tracker is closed for a rebuild. Wait for the
# launcher first so a first-run installation can finish starting MTGO.
if [[ -n "$mtgo_launcher_pid" ]]; then
  if ! wait "$mtgo_launcher_pid"; then
    echo "MTGO launcher exited unsuccessfully; waiting for any open MTGO window to close." >&2
  fi
fi
while pgrep -x 'MTGO\.exe' >/dev/null; do
  sleep 1
done
exit "$exit_code"
