<p align="center">
  <h1 align="center">awork</h1>
  <p align="center">
    <strong>The awork CLI — built for humans and agents alike</strong>
  </p>
  <p align="center">
    Token or OAuth authentication • Swagger-driven code generation • Structured JSON output
  </p>
  <p align="center">
    <a href="https://github.com/awork-io/awork-cli/actions/workflows/ci.yml"><img src="https://img.shields.io/github/actions/workflow/status/awork-io/awork-cli/ci.yml?style=flat-square&label=CI" alt="CI"></a>
    <a href="https://github.com/awork-io/awork-cli/releases"><img src="https://img.shields.io/github/v/release/awork-io/awork-cli?style=flat-square&color=blue" alt="Release"></a>
    <a href="https://github.com/awork-io/awork-cli/blob/main/LICENSE"><img src="https://img.shields.io/badge/license-MIT-green?style=flat-square" alt="License"></a>
    <img src="https://img.shields.io/badge/.NET-10.0-512BD4?style=flat-square&logo=dotnet" alt=".NET 10">
    <img src="https://img.shields.io/badge/OpenAPI-3.0-6BA539?style=flat-square&logo=openapiinitiative" alt="OpenAPI 3.0">
  </p>
</p>

---

## Why awork CLI?

| Problem | Solution |
|---------|----------|
| Unstable command names across API versions | Commands generated directly from Swagger — always in sync |
| Inconsistent parameter validation | Strict validation at build time via source generation |
| Unparseable output for automation | Every response wrapped in a predictable JSON envelope |
| Manual DTO maintenance | Zero hand-written DTOs — all generated from `swagger.json` |

```
$ awork users list --page-size 3

{
  "statusCode": 200,
  "traceId": "abc123",
  "response": [...]
}
```

---

## Installation

### Homebrew (macOS/Linux)
```bash
brew tap awork-io/tap
brew install awork
```

### Download binary
Grab the latest release for your platform from [GitHub Releases](https://github.com/awork-io/awork-cli/releases).

| Platform | Binary |
|----------|--------|
| macOS (Apple Silicon) | `awork-osx-arm64.tar.gz` |
| macOS (Intel) | `awork-osx-x64.tar.gz` |
| Linux (x64) | `awork-linux-x64.tar.gz` |
| Windows (x64) | `awork-win-x64.zip` |

---

## Quick Start

**1. Set your token (fastest path)**
```bash
echo "AWORK_TOKEN=your-token-here" > .env
```

**2. Or login with OAuth (DCR)**
```bash
awork auth login
```

**3. Verify setup**
```bash
awork doctor
```

**4. Explore**
```bash
awork --help
```

---

## Authentication

### API Token

Set your awork API token via environment variable or `.env` file:

```bash
export AWORK_TOKEN=your-token-here
# or
echo "AWORK_TOKEN=your-token-here" > .env
```

Use `--env <PATH>` to load a different `.env` file.

### OAuth (Dynamic Client Registration)

```bash
awork auth login
```

This opens a browser and stores an OAuth token + refresh token in the user config file.
If your tenant requires DCR authorization, provide a token via `AWORK_DCR_TOKEN`.
Override OAuth settings with `--redirect-uri`, `--scopes`, or env vars:
`AWORK_OAUTH_REDIRECT_URI`, `AWORK_OAUTH_SCOPES`, `AWORK_OAUTH_CLIENT_ID`.

### Auth Precedence

Default: **API token wins**. Override with:

```bash
awork --auth-mode oauth users list
```

Valid modes: `auto` (default), `token`, `oauth`.
Environment variable: `AWK_AUTH_MODE` or `AWORK_AUTH_MODE`.

### Config File

User config is stored at:
- macOS/Linux: `~/.config/awork-cli/config.json`
- Windows: `%APPDATA%\\awork-cli\\config.json`

Override with:

```bash
awork --config /path/to/config.json auth status
```

---

## Usage

### Command Structure

Commands follow a consistent pattern derived from the Swagger spec:

```
awork <domain> [resource] <action> [positional-args] [--options]
```

- **Domains** are top-level buckets: `users`, `tasks`, `projects`, `times`, `workspace`, ...
- **Resources** appear as sub-branches when needed (e.g. `users invitations`, `tasks tags`)
- **Actions** use predictable verbs: `list`, `get`, `create`, `update`, `delete`
- **Positional args** match path parameters in URL order
- **Options** are kebab-case query/body parameters

### Discover Commands

```bash
# List all resource groups
awork --help

# List actions for a domain
awork users --help

# Get help for a specific command
awork users list --help
```

### Auth Commands

```bash
# OAuth login
awork auth login

# Save API token
awork auth login --token "$AWORK_TOKEN"

# Status
awork auth status

# Logout (clear tokens)
awork auth logout
```

### Global Options

These options are available on all API commands:

| Option | Description |
|--------|-------------|
| `--select <FIELDS>` | Filter response fields (client-side). Example: `--select "id,name,createdOn"` |
| `--output <FORMAT>` | Output format: `json` (default) or `table` |
| `--page <N>` | Page number for paginated endpoints (default: 1) |
| `--page-size <N>` | Items per page for paginated endpoints |
| `--env <PATH>` | Load environment variables from a custom `.env` file |
| `--token <TOKEN>` | Override the API token for this request |
| `--auth-mode <MODE>` | Force auth mode: `auto`, `token`, or `oauth` |
| `--config <PATH>` | Use a custom config file path |

---

## Examples

### Basic Operations

```bash
# List users
awork users list

# List with pagination and field selection
awork users list --page-size 5 --select "id,firstName,lastName"

# Table output for quick inspection
awork users list --output table --select "firstName,lastName"

# Get user by ID (positional path param)
awork users get 550e8400-e29b-41d4-a716-446655440000

# Search with filters
awork search get-search \
  --search-term "agent" \
  --search-types "user" \
  --top 3 \
  --include-closed-and-stuck true
```

### Creating Resources

```bash
# Create with inline params
awork tasks create \
  --name "Welcome" \
  --base-type private \
  --entity-id 550e8400-e29b-41d4-a716-446655440000

# Create from JSON file
awork tasks create --body @samples/private-task.json

# Merge file + overrides
awork tasks create \
  --body @payload.json \
  --set name="Override Title"
```

### Advanced Body Construction

```bash
# Inline JSON arrays with --set-json
awork workspace absence-regions users-assign \
  --set regionId=550e8400-e29b-41d4-a716-446655440000 \
  --set-json userIds='["user-1","user-2"]'

# JSON arrays from file
awork workspace absence-regions users-assign \
  --set regionId=550e8400-e29b-41d4-a716-446655440000 \
  --set-json userIds=@/tmp/users.json

# Nested properties
awork task-tags tasks-update-tags \
  --set newTag.name=Priority
```

### Piping with jq

The consistent JSON envelope makes `jq` integration seamless:

```bash
# Get first user's ID
awork users list --page-size 1 | jq -r '.response[0].id'

# List project names only
awork projects list | jq -r '.response[].name'

# Get task count by status
awork tasks list | jq '.response | group_by(.taskStatusId) | map({status: .[0].taskStatusId, count: length})'

# Chain commands: create task for first active user
USER_ID=$(awork users list --page-size 1 | jq -r '.response[0].id')
awork tasks create --name "Welcome" --base-type private --entity-id "$USER_ID"

# Check if request succeeded
awork users me | jq -e '.statusCode == 200' > /dev/null && echo "OK" || echo "Failed"
```

### Real-World Workflow: Onboard a New Team Member

**Step 1 — Invite the user (skip email for programmatic flow)**

```bash
# samples/invite.json
{
  "workspaceId": "<workspace-id>",
  "email": "new.user@example.com",
  "firstName": "New",
  "lastName": "User",
  "title": "Engineer",
  "position": "Platform",
  "roleId": "<role-id>",
  "teamIds": ["<team-id>"],
  "skipSendingEmail": true
}
```

```bash
awork invitations create --body @samples/invite.json
```

**Step 2 — Accept the invitation programmatically**

```bash
# samples/accept.json
{ "invitationCode": "<invitation-code>" }
```

```bash
awork invitations accept --body @samples/accept.json
```

**Step 3 — Create a welcome task for the new user**

```bash
# samples/private-task.json
{
  "name": "Welcome to awork",
  "description": "Start here",
  "baseType": "private",
  "entityId": "<user-id>",
  "isPrio": true,
  "plannedDuration": 1800
}
```

```bash
awork tasks create --body @samples/private-task.json
```

**Or inline with overrides:**

```bash
awork tasks create \
  --body @samples/private-task.json \
  --set entityId="$(awork users list | jq -r '.response[0].id')"
```

---

## Output Contract

Every command returns a consistent JSON envelope:

```json
{
  "statusCode": 200,
  "traceId": "00-abc123...",
  "response": { ... }
}
```

| Field | Description |
|-------|-------------|
| `statusCode` | HTTP status code from the API |
| `traceId` | Correlation ID from response headers (best effort) |
| `response` | Parsed JSON body, or raw text if not JSON |

This makes `awork` trivial to integrate with `jq`, scripts, and AI agents.

---

## Development

### Building from Source

```bash
git clone https://github.com/awork-io/awork-cli.git
cd awork-cli
dotnet build
dotnet run --project src/Awk.Cli -- --help
```

### Build Release Binary

```bash
# macOS Apple Silicon
dotnet publish src/Awk.Cli -c Release -r osx-arm64

# macOS Intel
dotnet publish src/Awk.Cli -c Release -r osx-x64

# Linux
dotnet publish src/Awk.Cli -c Release -r linux-x64

# Windows
dotnet publish src/Awk.Cli -c Release -r win-x64
```

Output: `src/Awk.Cli/bin/Release/net10.0/<rid>/publish/awork`

### Run Tests

```bash
# All tests
dotnet test

# Individual test suites
./scripts/test-build.sh       # Build verification
./scripts/test-cli-names.sh   # Command naming consistency
./scripts/test-example.sh     # Example command validation
./scripts/test-params.sh      # Parameter handling
./scripts/test-auth.sh        # Auth flows (token)
./scripts/test-unit.sh        # Unit tests
```

### Package the Source Generator

```bash
dotnet pack src/Awk.CodeGen -c Release
```

Output: `src/Awk.CodeGen/bin/Release/Awk.CodeGen.*.nupkg`

### Create a Release

Push a version tag to trigger the release workflow:

```bash
git tag v0.1.0
git push origin v0.1.0
```

This will:
1. Build binaries for macOS (ARM64/x64), Linux, and Windows
2. Create a GitHub release with all artifacts
3. Update the Homebrew formula (requires `HOMEBREW_TAP_TOKEN` secret)

---

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                        swagger.json                         │
└─────────────────────────┬───────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────────┐
│                    Awk.CodeGen                              │
│              (Roslyn Source Generator)                      │
│                                                             │
│   • Parses OpenAPI 3.0 spec                                 │
│   • Emits DTOs to Awk.Generated namespace                   │
│   • Generates typed API client methods                      │
│   • Creates CLI commands grouped by Swagger tags            │
└─────────────────────────┬───────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────────┐
│                       Awk.Cli                               │
│                  (Spectre.Console.Cli)                      │
│                                                             │
│   • Token + OAuth authentication (DCR)                      │
│   • Parameter validation                                    │
│   • JSON envelope output                                    │
└─────────────────────────────────────────────────────────────┘
```

**When the Swagger spec changes, just rebuild.** No manual updates required.

---

## Project Structure

```
awork-cli/
├── .github/
│   └── workflows/
│       ├── ci.yml            # Build & test on PRs
│       └── release.yml       # Multi-platform release on tags
├── src/
│   ├── Awk.CodeGen/          # Source generator (NuGet-packageable)
│   └── Awk.Cli/              # CLI application
├── tests/
│   ├── Awk.CodeGen.Tests/    # Generator unit tests
│   └── Awk.Cli.Tests/        # CLI integration tests
├── homebrew/                 # Homebrew formula template
├── scripts/                  # Test helpers
├── samples/                  # Example JSON payloads
└── swagger.json              # awork OpenAPI spec
```

---

## License

MIT

---

<p align="center">
  <sub>Built with Spectre.Console • Powered by Roslyn Source Generators</sub>
</p>

