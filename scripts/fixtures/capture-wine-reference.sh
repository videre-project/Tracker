#!/usr/bin/env bash
# @file
# Copyright (c) 2026, Cory Bennett. All rights reserved.
# SPDX-License-Identifier: Apache-2.0

set -euo pipefail

repo_root=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)
volume_name=${TRACKER_WINE_VOLUME:-tracker_tracker-wine-data}
volume_image=${TRACKER_WINE_IMAGE:-videreproject/mtgosdk:headless}
output_directory=${1:-"$repo_root/tests/fixtures"}
temporary_directory=$(mktemp -d)
container_name="tracker-fixture-capture-$$"

cleanup() {
  docker rm "$container_name" >/dev/null 2>&1 || true
  rm -rf "$temporary_directory"
}
trap cleanup EXIT

docker create \
  --name "$container_name" \
  --entrypoint bash \
  --volume "$volume_name:/data:ro" \
  "$volume_image" \
  -lc 'sleep 60' >/dev/null

docker cp \
  "$container_name:/data/drive_c/users/wine/AppData/Local/Videre Tracker/Database/Event.db" \
  "$temporary_directory/Event.db"

node "$repo_root/scripts/fixtures/sanitize-event-db.mjs" \
  --input "$temporary_directory/Event.db" \
  --output "$output_directory/event.db" \
  --metadata "$output_directory/event.json" \
  --source "Tracker Event.db from Docker volume $volume_name (bot-vs-bot reference)"
