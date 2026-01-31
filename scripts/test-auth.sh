#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

run_cli() {
  dotnet run --project src/Awk.Cli -- "$@"
}

json_get() {
  local file="$1"
  local path="$2"
  python3 - "$file" "$path" <<'PY'
import json, sys

file_path = sys.argv[1]
path = sys.argv[2]

with open(file_path, "r", encoding="utf-8") as f:
    data = json.load(f)

current = data
for part in path.split("."):
    if part == "":
        continue
    if isinstance(current, dict):
        current = current.get(part)
    else:
        current = None
    if current is None:
        break

if current is None:
    print("")
elif isinstance(current, (dict, list)):
    print(json.dumps(current))
else:
    print(current)
PY
}

tmp_config="$(mktemp)"
tmp_env="$(mktemp)"
tmp_out="$(mktemp)"

run_cli auth status --config "$tmp_config" --env "$tmp_env" >"$tmp_out"
status="$(json_get "$tmp_out" "statusCode")"
if [[ "$status" != "0" ]]; then
  echo "auth status failed" >&2
  cat "$tmp_out" >&2
  exit 1
fi

run_cli auth login --config "$tmp_config" --env "$tmp_env" --token "test-token" >"$tmp_out"
status="$(json_get "$tmp_out" "statusCode")"
if [[ "$status" != "0" ]]; then
  echo "auth login failed" >&2
  cat "$tmp_out" >&2
  exit 1
fi

run_cli auth status --config "$tmp_config" --env "$tmp_env" >"$tmp_out"
masked="$(json_get "$tmp_out" "response.apiToken")"
if [[ -z "$masked" ]]; then
  echo "auth status missing token after login" >&2
  cat "$tmp_out" >&2
  exit 1
fi

run_cli auth logout --config "$tmp_config" --env "$tmp_env" >"$tmp_out"
status="$(json_get "$tmp_out" "statusCode")"
if [[ "$status" != "0" ]]; then
  echo "auth logout failed" >&2
  cat "$tmp_out" >&2
  exit 1
fi

run_cli auth status --config "$tmp_config" --env "$tmp_env" >"$tmp_out"
masked="$(json_get "$tmp_out" "response.apiToken")"
if [[ -n "$masked" ]]; then
  echo "auth logout did not clear token" >&2
  cat "$tmp_out" >&2
  exit 1
fi

rm -f "$tmp_out" "$tmp_config" "$tmp_env"
echo "auth script ok"
