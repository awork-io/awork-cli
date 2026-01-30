# TEST PLAN

Goal: validate CLI param parsing, JSON input modes, query types.

Scope:
- build + naming smoke
- example workflow script
- complex params: query bool/int, --body inline/file, --set, --set-json (array + @file)

Steps:
1. ./scripts/test-build.sh
2. ./scripts/test-cli-names.sh
3. ./scripts/test-example.sh
4. ./scripts/test-params.sh
5. ./scripts/test-unit.sh

Results (2026-01-30):
- test-build: pass
- test-cli-names: pass
- test-example: pass
- test-params: pass
- unit-tests: pass
