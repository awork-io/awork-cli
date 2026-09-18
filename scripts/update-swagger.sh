#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

source_path="${AWORK_OPENAPI_SOURCE:-}"
openapi_url="${AWORK_OPENAPI_URL:-https://aworkcdn.blob.core.windows.net/assets/awork-openapi-v1-develop.json}"
api_base_url="${AWORK_API_BASE_URL:-${API_BASE_URL:-https://api.awork.com/api/v1}}"

api_base_url="${api_base_url%/}"
if [[ "$api_base_url" != */api/v1 ]]; then
  api_base_url="${api_base_url}/api/v1"
fi

tmp="$(mktemp "${TMPDIR:-/tmp}/awork-openapi.XXXXXX.json")"
trap 'rm -f "$tmp"' EXIT

if [[ -n "$source_path" ]]; then
  if [[ ! -f "$source_path" ]]; then
    echo "OpenAPI source not found: $source_path" >&2
    exit 1
  fi

  jq --indent 4 --arg apiBaseUrl "$api_base_url" \
    '.servers = [{"url": $apiBaseUrl, "description": "awork Production"}]' \
    "$source_path" > "$tmp"
else
  curl -fsSL "$openapi_url" | jq --indent 4 --arg apiBaseUrl "$api_base_url" \
    '.servers = [{"url": $apiBaseUrl, "description": "awork Production"}]' > "$tmp"
fi

mv "$tmp" swagger.json
