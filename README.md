# awork CLI (awk)

Token-only awork CLI. DRY: swagger-driven source generator for full client + DTOs + CLI commands.

## Setup
- Create `.env` with a bearer token:
  - `AWORK_TOKEN=...` or `BEARER_TOKEN=...`
- Optional base URL:
  - `AWORK_BASE_URL=https://api.awork.com/api/v1`

## Build
```
dotnet build
```

## Tests
```
./scripts/test-build.sh
./scripts/test-cli-names.sh
./scripts/test-example.sh
./scripts/test-params.sh
./scripts/test-unit.sh
```

## Run
```
dotnet run --project src/Awk.Cli -- doctor
```

## Output contract
Every command prints JSON with:
- `statusCode`
- `traceId` (best effort, from response headers)
- `response` (JSON when possible, otherwise raw text)

## Codegen
Source generator reads `swagger.json` and emits:
- DTOs in `Awk.Generated`
- Full API client with one method per operationId (unique names)
- CLI commands grouped by Swagger tags

If swagger changes, rebuild. No manual DTOs.

## CLI usage
```
dotnet run --project src/Awk.Cli -- --help
dotnet run --project src/Awk.Cli -- tasks --help
```

Path params are positional args in path order (matches awork-debugger style).

### Examples
Invite user (skip email) + accept:
```
dotnet run --project src/Awk.Cli -- invitations create --body @samples/invite.json
dotnet run --project src/Awk.Cli -- invitations accept --body @samples/accept.json
```

Invite user with params:
```
dotnet run --project src/Awk.Cli -- invitations create \
  --workspace-id <workspace-id> \
  --email new.user@example.com \
  --first-name New \
  --last-name User \
  --skip-sending-email true \
  --team-ids <team-id> \
  --team-ids <team-id-2>
```

Create private task:
```
dotnet run --project src/Awk.Cli -- tasks create --body @samples/private-task.json
```

Create private task with params:
```
dotnet run --project src/Awk.Cli -- tasks create \
  --name "Welcome" \
  --base-type private \
  --entity-id <user-id>
```

Get a task (positional path param):
```
dotnet run --project src/Awk.Cli -- tasks get <task-id>
```

### Advanced body input
You can still use JSON bodies and patch values:
```
--body @payload.json
--set name=\"New name\"
--set-json teamIds='[\"id1\",\"id2\"]'
```
