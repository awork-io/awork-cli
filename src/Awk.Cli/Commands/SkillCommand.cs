using Spectre.Console.Cli;

namespace Awk.Commands;

internal sealed class SkillCommand : AsyncCommand<SkillCommand.Settings>
{
    internal sealed class Settings : CommandSettings;

    protected override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        Console.WriteLine(SkillContent.Text);
        return Task.FromResult(0);
    }
}

internal static class SkillContent
{
    internal const string Text = """
# awork CLI Guide

> AI-friendly reference for the awork CLI. Pipe to your agent: `awork skill`

## Overview

`awork` is a CLI for the awork API. Commands are auto-generated from `swagger.json` — always in sync with the API.

## Output Contract

Every command returns a JSON envelope:

```json
{
  "statusCode": 200,
  "traceId": "00-abc123...",
  "response": { ... }
}
```

- `statusCode`: HTTP status (200-299 = success)
- `traceId`: Correlation ID for debugging
- `response`: API response body (array or object)

## Command Structure

```
awork <domain> [resource] <action> [positional-args] [--options]
```

**Domains**: `users`, `tasks`, `projects`, `times`, `workspace`, `documents`, `files`, `search`, `integrations`, `automation`

**Common actions**: `list`, `get`, `create`, `update`, `delete`

## Global Options

| Option | Description |
|--------|-------------|
| `--select <FIELDS>` | Filter response fields (client-side). Example: `--select "id,name"` |
| `--output <FORMAT>` | Output format: `json` (default) or `table` |
| `--page <N>` | Page number (default: 1) |
| `--page-size <N>` | Items per page |
| `--env <PATH>` | Custom `.env` file |
| `--token <TOKEN>` | Override API token |
| `--auth-mode <MODE>` | `auto`, `token`, or `oauth` |

## Authentication

```bash
# Check auth status
awork auth status

# Login with OAuth (opens browser)
awork auth login

# Login with API token
awork auth login --token "$AWORK_TOKEN"

# Or set env var
export AWORK_TOKEN=your-token
```

## Common Patterns

### List with pagination and field selection

```bash
awork users list --page-size 10 --select "id,firstName,lastName,email"

# Table output for quick inspection
awork users list --output table --select "firstName,lastName"
```

### Get by ID (positional argument)

```bash
awork users get <user-id>
awork tasks get <task-id>
awork projects get <project-id>
```

### Create with inline options

```bash
awork tasks create \
  --name "Task name" \
  --base-type private \
  --entity-id <user-id>
```

### Create from JSON file

```bash
awork tasks create --body @payload.json
```

### Create with JSON + overrides

```bash
awork tasks create --body @payload.json --set name="Override"
```

### Set nested properties

```bash
awork tasks tags tasks-update-tags --set newTag.name=Priority
```

### Set JSON arrays

```bash
awork workspace absence-regions users-assign \
  --set regionId=<region-id> \
  --set-json userIds='["user-1","user-2"]'
```

## jq Integration

```bash
# Get first user ID
awork users list --page-size 1 | jq -r '.response[0].id'

# List all project names
awork projects list | jq -r '.response[].name'

# Check success
awork users me | jq -e '.statusCode == 200' > /dev/null && echo "OK"

# Chain: create task for first user
USER_ID=$(awork users list --page-size 1 | jq -r '.response[0].id')
awork tasks create --name "Welcome" --base-type private --entity-id "$USER_ID"
```

## Discovering Commands

```bash
# List domains
awork --help

# List actions in domain
awork users --help
awork tasks --help

# Get help for specific command
awork users list --help
awork tasks create --help
```

## Key Endpoints Reference

### Users

```bash
awork users list                      # List all users
awork users get <id>                  # Get user by ID
awork users me                        # Get current user
awork users update <id> --position X  # Update user
awork users delete <id>               # Delete user
```

### Tasks

```bash
awork tasks list                      # List tasks
awork tasks get <id>                  # Get task
awork tasks create --name X --base-type private --entity-id <user-id>
awork tasks update <id> --name "New name"
awork tasks delete <id>
```

### Projects

```bash
awork projects list                   # List projects
awork projects get <id>               # Get project
awork projects create --name X        # Create project
```

### Time Entries

```bash
awork times list                      # List time entries
awork times create --task-id X --duration 3600
```

### Search

```bash
awork search get-search \
  --search-term "query" \
  --search-types "user,task,project" \
  --top 10
```

### Workspace

```bash
awork workspace teams list
awork workspace roles list
awork workspace absence-regions list
```

## Error Handling

Non-2xx responses still return the envelope:

```json
{
  "statusCode": 400,
  "traceId": "...",
  "response": {"error": "Bad Request", "message": "..."}
}
```

Check `statusCode` to determine success:

```bash
result=$(awork users get invalid-id)
status=$(echo "$result" | jq '.statusCode')
if [[ "$status" -ge 200 && "$status" -lt 300 ]]; then
  echo "Success"
else
  echo "Error: $status"
fi
```

## Tips for AI Agents

1. **Always check `statusCode`** — don't assume success
2. **Use `--select`** to reduce response size when you only need specific fields
3. **Use `--page-size`** for large lists to avoid timeouts
4. **Use `jq -r`** for raw string output (no quotes)
5. **Use `jq -e`** for exit code based on expression result
6. **Discover with `--help`** — commands match the API spec exactly
""";
}
