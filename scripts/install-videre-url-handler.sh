#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
desktop_dir="${XDG_DATA_HOME:-${HOME}/.local/share}/applications"
desktop_file="${desktop_dir}/videre.desktop"

mkdir -p "${desktop_dir}"
sed "s#^Exec=.*#Exec=${script_dir}/videre-url-handler.sh %u#" \
  "${script_dir}/videre.desktop" > "${desktop_file}"
chmod +x "${script_dir}/videre-url-handler.sh"
update-desktop-database "${desktop_dir}" 2>/dev/null || true
xdg-mime default videre.desktop x-scheme-handler/videre

echo "Registered videre:// with ${desktop_file}"
