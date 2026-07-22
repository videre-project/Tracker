#!/bin/bash
set -euo pipefail

tracker_exe='/workspace/publish/Videre Tracker.exe'
tracker_certificate=/opt/tracker/localhost.pfx
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

if [[ ! -f "$tracker_exe" ]]; then
  echo "Tracker executable not found at: $tracker_exe" >&2
  echo "Run 'pnpm run publish' on the host before starting tracker-wayland." >&2
  exit 1
fi

# The published app explicitly enables HTTPS.
# Point Kestrel at the loopback-only certificate since Wine has no ASP.NET dev cert.
export ASPNETCORE_Kestrel__Certificates__Default__Path
ASPNETCORE_Kestrel__Certificates__Default__Path="$(winepath -w "$tracker_certificate")"
export ASPNETCORE_Kestrel__Certificates__Default__Password=tracker-localhost

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

echo "Launching Videre Tracker..."
wine "$tracker_exe" &
tracker_pid=$!

if wait "$tracker_pid"; then
  tracker_status=0
else
  tracker_status=$?
fi
wineserver -w
exit "$tracker_status"
