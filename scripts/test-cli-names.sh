#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")/.."

parse_commands() {
  python3 - "$1" <<'PY'
import re, sys
raw = open(sys.argv[1], "r", encoding="utf-8").read()
text = re.sub(r"\x1b\[[0-9;]*m", "", raw).splitlines()
commands = []
in_cmd = False
for line in text:
    if line.strip() == "COMMANDS:":
        in_cmd = True
        continue
    if in_cmd:
        if not line.strip():
            continue
        m = re.match(r"^([a-z0-9\-]+)\b", line.strip())
        if m:
            commands.append(m.group(1))
        else:
            if line.startswith("OPTIONS:") or line.startswith("USAGE:") or line.startswith("DESCRIPTION:"):
                break
print("\n".join(commands))
PY
}

cli_dll="src/Awk.Cli/bin/Debug/net10.0/awork.dll"
if [[ ! -f "$cli_dll" ]]; then
  echo "CLI build output not found. Run ./scripts/test-build.sh first." >&2
  exit 1
fi

tmp_root="$(mktemp -d)"
top_help="$tmp_root/top.txt"
NO_COLOR=1 dotnet "$cli_dll" --help > "$top_help" 2>&1

top_commands=()
while IFS= read -r cmd; do
  [[ -n "$cmd" ]] && top_commands+=("$cmd")
done < <(parse_commands "$top_help")

if [[ ${#top_commands[@]} -eq 0 ]]; then
  echo "no top-level commands detected" >&2
  exit 1
fi

bad=()
patterns=("list-get" "get-get" "create-create" "list-list" "users-users" "roles-roles" "teams-teams" "projects-projects")

for cmd in "${top_commands[@]}"; do
  sub_help="$tmp_root/$cmd.txt"
  NO_COLOR=1 dotnet "$cli_dll" "$cmd" --help > "$sub_help" 2>&1 || continue
  while IFS= read -r subcmd; do
    for pat in "${patterns[@]}"; do
      if [[ "$subcmd" == *"$pat"* ]]; then
        bad+=("$cmd $subcmd")
      fi
    done
  done < <(parse_commands "$sub_help")
done

if [[ ${#bad[@]} -gt 0 ]]; then
  echo "bad command names:"
  printf '%s\n' "${bad[@]}"
  exit 1
fi

echo "ok"
