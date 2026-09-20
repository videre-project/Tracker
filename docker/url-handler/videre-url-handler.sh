#!/usr/bin/env bash
set -euo pipefail

# Bridge website imports from the Linux browser to Tracker running under Wine.
scheme_url="${1:-}"
if [[ "${scheme_url}" != videre://import/deck\?payload=* ]]; then
  echo "Unsupported Videre URL" >&2
  exit 2
fi

payload="${scheme_url#*payload=}"
payload="${payload%%&*}"
request_json="$(python3 - "${payload}" <<'PY'
import base64
import sys

encoded = sys.argv[1]
encoded += '=' * (-len(encoded) % 4)
sys.stdout.write(base64.urlsafe_b64decode(encoded).decode('utf-8'))
PY
)"

# The development endpoint uses a self-signed localhost certificate.
tracker_api_url="${TRACKER_API_URL:-https://127.0.0.1:7101}"
curl --fail --silent --show-error --insecure \
  --header 'Content-Type: application/json' \
  --data-raw "${request_json}" \
  "${tracker_api_url}/api/decks/import-and-open"
