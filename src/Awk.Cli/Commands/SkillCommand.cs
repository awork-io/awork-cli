using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Awk.Commands;

internal sealed class SkillInstallSettings : CommandSettings
{
    [CommandOption("--claude")]
    [Description("Install skill for Claude Code (~/.claude/skills/).")]
    public bool Claude { get; set; }

    [CommandOption("--codex")]
    [Description("Install skill for Codex (~/.codex/skills/).")]
    public bool Codex { get; set; }
}

internal sealed class SkillInstallCommand : AsyncCommand<SkillInstallSettings>
{
    private const string SkillDirName = "awork-cli";
    private const string SkillFileName = "SKILL.md";

    private static readonly Dictionary<string, string> Targets = new()
    {
        ["Claude Code"] = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "skills", SkillDirName, SkillFileName),
        ["Codex"] = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex", "skills", SkillDirName, SkillFileName),
    };

    protected override Task<int> ExecuteAsync(
        CommandContext context, SkillInstallSettings settings, CancellationToken cancellationToken)
    {
        var selected = ResolveTargets(settings);
        if (selected.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No targets selected. Nothing to do.[/]");
            return Task.FromResult(0);
        }

        foreach (var name in selected)
        {
            var path = Targets[name];
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, SkillContent.Text);
            AnsiConsole.MarkupLine($"[green]✓[/] Installed skill for [bold]{name}[/] to {path}");
        }

        return Task.FromResult(0);
    }

    private static List<string> ResolveTargets(SkillInstallSettings settings)
    {
        if (settings.Claude || settings.Codex)
        {
            var result = new List<string>();
            if (settings.Claude) result.Add("Claude Code");
            if (settings.Codex) result.Add("Codex");
            return result;
        }

        if (Console.IsInputRedirected)
        {
            return [.. Targets.Keys];
        }

        var prompt = new MultiSelectionPrompt<string>()
            .Title("Install skill for which agents?")
            .AddChoices(Targets.Keys)
            .InstructionsText("[grey](Press [blue]<space>[/] to toggle, [green]<enter>[/] to confirm)[/]");

        foreach (var key in Targets.Keys)
            prompt.Select(key);

        return AnsiConsole.Prompt(prompt);
    }
}

internal sealed class SkillShowCommand : AsyncCommand<SkillShowCommand.Settings>
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
---
name: awork-cli
description: Manage projects, users, time entries and more in awork directly from the command line with the awork-cli.
---

# awork CLI Guide

## Overview

`awork` is a CLI for the awork API.

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

## Tips for AI Agents

1. **Always check `statusCode`** — don't assume success
2. **Use `--select`** to reduce response size when you only need specific fields
3. **Use `--page-size`** for large lists to avoid timeouts
4. **Use `jq -r`** for raw string output (no quotes)
5. **Use `jq -e`** for exit code based on expression result
6. **Discover with `--help`** — commands match the API spec exactly
""";
}
