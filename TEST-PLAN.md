# TEST PLAN

## Automated
- `./scripts/test-build.sh`
- `./scripts/test-cli-names.sh`
- `./scripts/test-unit.sh`
- `./scripts/test-auth.sh`
- `./scripts/test-params.sh`
- `./scripts/test-example.sh` (uses `.env` token)

## Unit Coverage
- Generator naming + no-async-suffix rules.
- CLI integration (mock server).
- Auth resolution precedence + error paths.

## Manual/Exploratory
- `awk auth login` OAuth flow (browser + callback).
- Token + OAuth conflict behavior (`--auth-mode`).
