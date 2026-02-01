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

json_first_active_user_id() {
  local file="$1"
  python3 - "$file" <<'PY'
import json, sys

with open(sys.argv[1], "r", encoding="utf-8") as f:
    data = json.load(f)

users = data.get("response")
if not isinstance(users, list):
    print("")
    raise SystemExit

for user in users:
    if not isinstance(user, dict):
        continue
    if user.get("isArchived") is False and user.get("isDeactivated") is False:
        print(user.get("id", ""))
        raise SystemExit

print("")
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
WORKSPACE_ID="$(json_get "$ME_JSON" "response.workspace.id")"

if [[ -z "$USER_ID" || -z "$WORKSPACE_ID" ]]; then
  echo "missing user/workspace id" >&2
  exit 1
fi

TEAMS_JSON="$(run_checked workspace teams list)"
TEAM_ID="$(json_first_id_in_response_array "$TEAMS_JSON")"

if [[ -z "$TEAM_ID" ]]; then
  TEAM_JSON="$(run_checked workspace teams create --name "Agent Team" --color blue)"
  TEAM_ID="$(json_get "$TEAM_JSON" "response.id")"
fi

if [[ -z "$TEAM_ID" ]]; then
  echo "missing team id" >&2
  exit 1
fi

ROLES_JSON="$(run_checked workspace roles list)"
ROLE_ID="$(json_first_id_in_response_array "$ROLES_JSON")"

if [[ -z "$ROLE_ID" ]]; then
  echo "missing role id" >&2
  exit 1
fi

USERS_JSON="$(run_checked users list)"
ACTIVE_USER_ID="$(json_first_active_user_id "$USERS_JSON")"
TARGET_USER_ID="$ACTIVE_USER_ID"
if [[ -z "$TARGET_USER_ID" ]]; then
  TARGET_USER_ID="$USER_ID"
fi

if [[ -z "$TARGET_USER_ID" ]]; then
  echo "missing user id" >&2
  exit 1
fi

REGIONS_JSON="$(run_checked workspace absence-regions list)"
REGION_ID="$(json_first_id_in_response_array "$REGIONS_JSON")"

if [[ -z "$REGION_ID" ]]; then
  REGION_JSON="$(run_checked workspace absence-regions create --name "Agent Region" --country-code "US")"
  REGION_ID="$(json_get "$REGION_JSON" "response.id")"
fi

if [[ -z "$REGION_ID" ]]; then
  echo "missing absence region id" >&2
  exit 1
fi

run_checked workspace absence-regions users-assign --region-id "$REGION_ID" --user-ids "$USER_ID" >/dev/null

run_checked users capacities update-capacity "$TARGET_USER_ID" \
  --mon 28800 --tue 28800 --wed 28800 --thu 28800 --fri 28800 --sat 0 --sun 0 >/dev/null

run_checked users update "$TARGET_USER_ID" --position "AI Agent" >/dev/null

run_checked tasks create --name "Profile: set working hours" --base-type private --entity-id "$TARGET_USER_ID" >/dev/null
run_checked tasks create --name "Profile: set holiday region" --base-type private --entity-id "$TARGET_USER_ID" >/dev/null
run_checked tasks create --name "Team: confirm membership" --base-type private --entity-id "$TARGET_USER_ID" >/dev/null

run_checked workspace teams add-users "$TEAM_ID" --body "[\"$TARGET_USER_ID\"]" >/dev/null

EMAIL="agent+$(date +%s)@example.com"
INVITE_JSON="$(run_checked users invitations create \
  --workspace-id "$WORKSPACE_ID" \
  --invitation-flow invite \
  --role-id "$ROLE_ID" \
  --email "$EMAIL" \
  --first-name "Agent" \
  --last-name "Test" \
  --skip-sending-email true \
  --password "TmpP@ssw0rd!")"

INVITE_CODE="$(json_get "$INVITE_JSON" "response.invitationCode")"
if [[ -n "$INVITE_CODE" ]]; then
  run_checked users invitations accept --invitation-code "$INVITE_CODE" >/dev/null
fi

echo "ok"
