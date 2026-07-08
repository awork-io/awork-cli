#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

source_path="${AWORK_OPENAPI_SOURCE:-}"
api_base_url="${API_BASE_URL:-}"
service="${AWORK_OPENAPI_SERVICE:-core}"
private_token="${AWORK_DOCS_PRIVATE_TOKEN:-BZFb1BVs}"
default_source_path="../app/backend/services/ai-service/service/Assets/AworkOpenApiV1.json"

fetch_url=""

if [[ -z "$source_path" && -z "$api_base_url" && -f "$default_source_path" ]]; then
  source_path="$default_source_path"
fi

tmp="$(mktemp "${TMPDIR:-/tmp}/awork-openapi.XXXXXX.json")"
trap 'rm -f "$tmp"' EXIT

if [[ -n "$source_path" ]]; then
  if [[ ! -f "$source_path" ]]; then
    echo "OpenAPI source not found: $source_path" >&2
    exit 1
  fi

  jq --indent 4 . "$source_path" > "$tmp"
elif [[ -n "$api_base_url" ]]; then
  api_base_url="${api_base_url%/}"
  if [[ "$api_base_url" == */api/v1 ]]; then
    fetch_url="${api_base_url}/docs/${service}/v1/swagger.json?privateToken=${private_token}&hideParams=true"
  else
    fetch_url="${api_base_url}/api/v1/docs/${service}/v1/swagger.json?privateToken=${private_token}&hideParams=true"
  fi

  curl -fsSL "$fetch_url" | jq --indent 4 . > "$tmp"
else
  echo "Could not locate the default awork app OpenAPI asset: $default_source_path" >&2
  echo "Set AWORK_OPENAPI_SOURCE=/path/to/AworkOpenApiV1.json or API_BASE_URL=https://app.cwork.io." >&2
  exit 1
fi

mv "$tmp" swagger.json
