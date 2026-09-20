#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
desktop_dir="${XDG_DATA_HOME:-${HOME}/.local/share}/applications"
desktop_file="${desktop_dir}/videre.desktop"

mkdir -p "${desktop_dir}"
cat > "${desktop_file}" <<EOF
[Desktop Entry]
Type=Application
Name=Videre Tracker URL Handler
Comment=Import decks into Videre Tracker
Exec="${script_dir}/videre-url-handler.sh" %u
Terminal=false
NoDisplay=true
MimeType=x-scheme-handler/videre;
EOF

update-desktop-database "${desktop_dir}"
xdg-mime default videre.desktop x-scheme-handler/videre

echo "Registered videre:// with ${desktop_file}"
