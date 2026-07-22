#!/bin/bash
set -euo pipefail

bootstrap=/home/wine/mtgo_setup.exe
persisted_bootstrap=/home/wine/.wine/mtgo_setup.exe

if [[ ! -f "$bootstrap" && -f "$persisted_bootstrap" ]]; then
  cp "$persisted_bootstrap" "$bootstrap"
fi

if [[ ! -f "$bootstrap" ]]; then
  install-mtgo.sh
  cp "$bootstrap" "$persisted_bootstrap"
fi

# MTGO's WPF surface can render black through Wine's accelerated composition.
# Force WPF software rendering in this Wine prefix.
wine reg add 'HKCU\Software\Microsoft\Avalon.Graphics' \
  /v DisableHWAcceleration /t REG_DWORD /d 1 /f >/dev/null

mtgo
exec wineserver -w
