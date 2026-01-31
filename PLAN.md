# PLAN

## Goals
- CLI covers full awork API (swagger-driven), stable naming, strict param validation.
- Auth: API token + OAuth (DCR), agent-friendly output.
- DRY codegen and CLI helpers.

## Decisions
- Auth precedence: API token wins in auto; `--auth-mode` to force OAuth.
- OAuth uses DCR if no client id; tokens stored in user config.
- JSON envelope everywhere (statusCode, traceId, response).

## Work Items
- [x] Swagger generator + command naming cleanup.
- [x] Token auth via `.env` + env vars.
- [x] OAuth (DCR) auth commands: `auth login/status/logout`.
- [x] Config persistence at `~/.config/awork-cli/config.json`.
- [x] Auth mode flag (`--auth-mode`).
- [x] Auth resolver with refresh + warnings.
- [x] Unit + integration tests, bash scripts.
- [x] README updated for publish.

## Notes
- Backend reference: `~/Repositories/awork-backend` for auth behavior and endpoint semantics.

## TODO
- [ ] Add OAuth refresh integration test with mock token endpoint.
- [ ] Add `auth login --token-stdin` example to README.
- [ ] Consider `--no-save` flag for ephemeral tokens.
