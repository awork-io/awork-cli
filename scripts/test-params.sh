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
import json, sys, re

file_path = sys.argv[1]
path = sys.argv[2]

with open(file_path, "r", encoding="utf-8") as f:
    data = json.load(f)

def get(obj, token):
    if token == "":
        return obj
    if token.endswith("]"):
        m = re.match(r"(.+?)\\[(\\d+)\\]$", token)
        if not m:
            return None
        name, idx = m.group(1), int(m.group(2))
        if name:
            if not isinstance(obj, dict):
                return None
            obj = obj.get(name)
        if not isinstance(obj, list) or idx >= len(obj):
            return None
        return obj[idx]
    if isinstance(obj, dict):
        return obj.get(token)
    if isinstance(obj, list):
        try:
            return obj[int(token)]
        except Exception:
            return None
    return None

current = data
for part in path.split("."):
    current = get(current, part)
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

json_first_id_in_response_array() {
  local file="$1"
  python3 - "$file" <<'PY'
import json, sys

with open(sys.argv[1], "r", encoding="utf-8") as f:
    data = json.load(f)

items = data.get("response")
if not isinstance(items, list):
    print("")
    raise SystemExit

for item in items:
    if isinstance(item, dict) and item.get("id"):
        print(item["id"])
        raise SystemExit

print("")
PY
}

run_checked() {
  local tmp
  tmp="$(mktemp)"
  run_cli "$@" | tee "$tmp" >/dev/stderr
  local status
  status="$(json_get "$tmp" "statusCode")"
  if [[ -z "$status" || "$status" -lt 200 || "$status" -ge 300 ]]; then
    echo "request failed: $* (status=${status:-missing})" >&2
    cat "$tmp" >&2
    rm -f "$tmp"
    exit 1
  fi
  echo "$tmp"
}

ME_JSON="$(run_checked doctor)"
USER_ID="$(json_get "$ME_JSON" "response.id")"
RUN_ID="$(date +%s)-$RANDOM"

if [[ -z "$USER_ID" ]]; then
  echo "missing user id" >&2
  exit 1
fi

run_checked search get-search \
  --search-term "agent" \
  --search-types "user" \
  --top 3 \
  --include-closed-and-stuck true >/dev/null

run_checked workspace custom-fields list-custom-field-definitions --include-linked-project-ids true >/dev/null

run_checked workspace teams create --body "{\"name\":\"Param Team Inline ${RUN_ID}\",\"color\":\"orange\"}" >/dev/null

TEAM_FILE="$(mktemp)"
cat > "$TEAM_FILE" <<'JSON'
{"name":"Param Team File PLACEHOLDER","color":"blue"}
JSON
python3 - "$TEAM_FILE" "$RUN_ID" <<'PY'
import json, sys
path = sys.argv[1]
run_id = sys.argv[2]
data = json.loads(open(path, "r", encoding="utf-8").read())
data["name"] = data["name"].replace("PLACEHOLDER", run_id)
open(path, "w", encoding="utf-8").write(json.dumps(data))
PY
run_checked workspace teams create --body "@$TEAM_FILE" >/dev/null

REGIONS_JSON="$(run_checked workspace absence-regions list)"
REGION_ID="$(json_first_id_in_response_array "$REGIONS_JSON")"

if [[ -z "$REGION_ID" ]]; then
  REGION_JSON="$(run_checked workspace absence-regions create --name "Param Region" --country-code "US")"
  REGION_ID="$(json_get "$REGION_JSON" "response.id")"
fi

if [[ -z "$REGION_ID" ]]; then
  echo "missing absence region id" >&2
  exit 1
fi

USER_IDS_FILE="$(mktemp)"
printf '[\"%s\"]\n' "$USER_ID" > "$USER_IDS_FILE"

run_checked workspace absence-regions users-assign \
  --set "regionId=$REGION_ID" \
  --set-json "userIds=@$USER_IDS_FILE" >/dev/null

echo "ok"
