#!/usr/bin/env bash
set -euo pipefail

# Development bridge for Linux browsers running alongside the Wine Tracker.
# The Tracker API remains HTTPS-only; curl's --insecure flag is limited to the
# locally generated localhost certificate.

scheme_url="${1:-}"
if [[ "${scheme_url}" != videre://import/deck\?payload=* ]]; then
  echo "Unsupported Videre URL: ${scheme_url}" >&2
  exit 2
fi

payload="${scheme_url#*payload=}"
payload="${payload%%&*}"

request_json="$(python3 - "${payload}" <<'PY'
import base64
import sys

encoded = sys.argv[1].replace('-', '+').replace('_', '/')
encoded += '=' * (-len(encoded) % 4)
sys.stdout.write(base64.b64decode(encoded).decode('utf-8'))
PY
)"

tracker_api_url="${TRACKER_API_URL:-https://127.0.0.1:7101}"
curl --fail --silent --show-error --insecure \
  --header 'Content-Type: application/json' \
  --data "${request_json}" \
  "${tracker_api_url}/api/decks/import-and-open"
