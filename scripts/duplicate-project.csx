#!/usr/bin/env dotnet-script
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

DuplicateProject.RunAsync(args).GetAwaiter().GetResult();

static class DuplicateProject
{
    private const string DefaultCli = "awork";

    public static async Task RunAsync(string[] args)
    {
        var options = Options.Parse(args);
        if (options.ShowHelp)
        {
            PrintHelp();
            return;
        }

        if (string.IsNullOrWhiteSpace(options.SourceProjectId))
        {
            throw new InvalidOperationException("Missing --source-project-id.");
        }

        var runner = new CliRunner(options.CliPath, options.CliArgs);

        var sourceProject = await runner.RunAsync(
            "projects", "get", options.SourceProjectId!, "--output", "json");
        var sourceProjectObj = AsObject(sourceProject, "projects get");
        var sourceName = sourceProjectObj["name"]?.GetValue<string>()?.Trim();

        var targetName = string.IsNullOrWhiteSpace(options.TargetName)
            ? $"{sourceName ?? "Project"} (Copy)"
            : options.TargetName!;

        var createBody = new JsonObject
        {
            ["name"] = targetName
        };
        CopyIfPresent(createBody, sourceProjectObj, "isPrivate");
        CopyIfPresent(createBody, sourceProjectObj, "description");
        CopyIfPresent(createBody, sourceProjectObj, "startDate");
        CopyIfPresent(createBody, sourceProjectObj, "dueDate");
        CopyIfPresent(createBody, sourceProjectObj, "companyId");
        CopyIfPresent(createBody, sourceProjectObj, "timeBudget");
        CopyIfPresent(createBody, sourceProjectObj, "isBillableByDefault");
        CopyIfPresent(createBody, sourceProjectObj, "projectTypeId");
        CopyIfPresent(createBody, sourceProjectObj, "color");
        CopyIfPresent(createBody, sourceProjectObj, "deductNonBillableHours");
        CopyIfPresent(createBody, sourceProjectObj, "isProjectKeyVisible");
        CopyIfPresent(createBody, sourceProjectObj, "workflowId");
        CopyIfPresent(createBody, sourceProjectObj, "referenceDateType");
        CopyIfPresent(createBody, sourceProjectObj, "referenceDateValue");

        if (options.DryRun)
        {
            Console.WriteLine(JsonSerializer.Serialize(createBody, JsonOptions));
            return;
        }

        using var createBodyFile = TempFile.FromJson(createBody);
        var createdProject = await runner.RunAsync(
            "projects", "create", "--body", $"@{createBodyFile.Path}", "--output", "json");
        var createdProjectObj = AsObject(createdProject, "projects create");
        var targetProjectId = createdProjectObj["id"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(targetProjectId))
        {
            throw new InvalidOperationException("Project creation did not return an id.");
        }

        var sourceStatuses = await runner.RunAsync(
            "tasks", "statuses", "list-project-task-statuses", options.SourceProjectId!, "--select", "id,name,type", "--output", "json");
        var targetStatuses = await runner.RunAsync(
            "tasks", "statuses", "list-project-task-statuses", targetProjectId, "--select", "id,name,type", "--output", "json");

        var mapping = BuildStatusMapping(
            AsArray(sourceStatuses, "source task statuses"),
            AsArray(targetStatuses, "target task statuses"));

        using var changeProjectBodyFile = TempFile.FromJson(new JsonObject
        {
            ["projectId"] = targetProjectId,
            ["taskStatusMapping"] = mapping
        });

        var taskLists = await runner.RunAsync(
            "tasks", "lists", "list-project-task-lists", options.SourceProjectId!, "--select", "id,name", "--output", "json");
        var taskListArray = AsArray(taskLists, "task lists");

        foreach (var listNode in taskListArray)
        {
            var listObj = listNode?.AsObject();
            var listId = listObj?["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(listId))
            {
                continue;
            }

            var copiedList = await runner.RunAsync(
                "tasks", "lists", "create-copy", options.SourceProjectId!, listId, "--output", "json");
            var copiedListObj = AsObject(copiedList, "task list copy");
            var copiedListId = copiedListObj["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(copiedListId))
            {
                throw new InvalidOperationException($"Task list copy for {listId} returned no id.");
            }

            await runner.RunAsync(
                "tasks", "lists", "change-project", options.SourceProjectId!, copiedListId,
                "--body", $"@{changeProjectBodyFile.Path}", "--output", "json");

            Console.WriteLine($"Moved list {listId} -> {copiedListId}");
        }
    }

    private static JsonArray BuildStatusMapping(JsonArray sourceStatuses, JsonArray targetStatuses)
    {
        if (targetStatuses.Count == 0)
        {
            throw new InvalidOperationException("Target project has no task statuses.");
        }

        var targetByName = targetStatuses
            .Select(node => node?.AsObject())
            .Where(node => node?["name"] is not null)
            .GroupBy(node => node?["name"]?.GetValue<string>() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var targetByType = targetStatuses
            .Select(node => node?.AsObject())
            .Where(node => node?["type"] is not null)
            .GroupBy(node => node?["type"]?.GetValue<string>() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var fallback = targetStatuses.First()?.AsObject();
        var mapping = new JsonArray();

        foreach (var sourceNode in sourceStatuses)
        {
            var sourceObj = sourceNode?.AsObject();
            var oldId = sourceObj?["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(oldId))
            {
                continue;
            }

            JsonObject? match = null;
            var name = sourceObj?["name"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(name) && targetByName.TryGetValue(name!, out var nameMatch))
            {
                match = nameMatch;
            }

            if (match is null)
            {
                var type = sourceObj?["type"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(type) && targetByType.TryGetValue(type!, out var typeMatch))
                {
                    match = typeMatch;
                }
            }

            match ??= fallback;
            var newId = match?["id"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(newId))
            {
                throw new InvalidOperationException("Failed to map task statuses.");
            }

            mapping.Add(new JsonObject
            {
                ["oldStatusId"] = oldId,
                ["newStatusId"] = newId
            });
        }

        return mapping;
    }

    private static JsonObject AsObject(JsonNode? node, string label)
    {
        if (node is JsonObject obj)
        {
            return obj;
        }

        throw new InvalidOperationException($"Expected object response for {label}.");
    }

    private static JsonArray AsArray(JsonNode? node, string label)
    {
        if (node is JsonArray array)
        {
            return array;
        }

        throw new InvalidOperationException($"Expected array response for {label}.");
    }

    private static void CopyIfPresent(JsonObject target, JsonObject source, string property)
    {
        if (!source.TryGetPropertyValue(property, out var value))
        {
            return;
        }

        if (IsNull(value))
        {
            return;
        }

        target[property] = value!.DeepClone();
    }

    private static bool IsNull(JsonNode? node)
    {
        if (node is null)
        {
            return true;
        }

        if (node is JsonValue value && value.TryGetValue<object?>(out var boxed))
        {
            return boxed is null;
        }

        return false;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private sealed class Options
    {
        public string? SourceProjectId { get; private set; }
        public string? TargetName { get; private set; }
        public bool DryRun { get; private set; }
        public bool ShowHelp { get; private set; }
        public string CliPath { get; private set; } = DefaultCli;
        public string? CliArgs { get; private set; }

        public static Options Parse(string[] args)
        {
            var options = new Options
            {
                CliPath = Environment.GetEnvironmentVariable("AWORK_CLI") ?? DefaultCli,
                CliArgs = Environment.GetEnvironmentVariable("AWORK_CLI_ARGS")
            };

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg)
                {
                    case "--source-project-id":
                    case "-s":
                        options.SourceProjectId = NextValue(args, ref i, arg);
                        break;
                    case "--target-name":
                    case "-n":
                        options.TargetName = NextValue(args, ref i, arg);
                        break;
                    case "--cli":
                        options.CliPath = NextValue(args, ref i, arg);
                        break;
                    case "--cli-args":
                        options.CliArgs = NextValue(args, ref i, arg);
                        break;
                    case "--dry-run":
                        options.DryRun = true;
                        break;
                    case "--help":
                    case "-h":
                        options.ShowHelp = true;
                        break;
                }
            }

            return options;
        }

        private static string NextValue(string[] args, ref int index, string arg)
        {
            if (index + 1 >= args.Length)
            {
                throw new InvalidOperationException($"Missing value for {arg}.");
            }

            index++;
            return args[index];
        }
    }

    private sealed class CliRunner
    {
        private readonly string _cliPath;
        private readonly IReadOnlyList<string> _cliArgs;

        public CliRunner(string cliPath, string? cliArgs)
        {
            _cliPath = cliPath;
            _cliArgs = SplitArgs(cliArgs);
        }

        public async Task<JsonNode?> RunAsync(params string[] args)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _cliPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            foreach (var cliArg in _cliArgs)
            {
                startInfo.ArgumentList.Add(cliArg);
            }

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException($"Failed to start {_cliPath}.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"CLI failed ({process.ExitCode}): {stderr.Trim()}");
            }

            if (string.IsNullOrWhiteSpace(stdout))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            var statusCode = root.TryGetProperty("statusCode", out var statusElement)
                ? statusElement.GetInt32()
                : 0;
            if (statusCode is < 200 or >= 300)
            {
                var raw = root.TryGetProperty("response", out var responseElement)
                    ? responseElement.GetRawText()
                    : string.Empty;
                throw new InvalidOperationException($"API error ({statusCode}): {raw}");
            }

            if (!root.TryGetProperty("response", out var response))
            {
                return null;
            }

            return JsonNode.Parse(response.GetRawText());
        }
    }

    private sealed class TempFile : IDisposable
    {
        public string Path { get; }

        private TempFile(string path)
        {
            Path = path;
        }

        public static TempFile FromJson(JsonNode node)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"awork-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(node, JsonOptions));
            return new TempFile(path);
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(Path))
                {
                    File.Delete(Path);
                }
            }
            catch
            {
            }
        }
    }

    private static IReadOnlyList<string> SplitArgs(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var quoteChar = '\0';

        foreach (var ch in input)
        {
            if (inQuotes)
            {
                if (ch == quoteChar)
                {
                    inQuotes = false;
                    continue;
                }

                current.Append(ch);
                continue;
            }

            if (ch == '"' || ch == '\'')
            {
                inQuotes = true;
                quoteChar = ch;
                continue;
            }

            if (char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(@"Duplicate a project by copying task lists (and tasks inside them).

Usage:
  dotnet script scripts/duplicate-project.csx --source-project-id <id> --target-name ""New Project""

Options:
  -s, --source-project-id   Source project id (required)
  -n, --target-name         Target project name (defaults to '<source> (Copy)')
      --cli                 CLI executable (default: awork)
      --cli-args            Extra args for the CLI (useful for 'dotnet run ...')
      --dry-run             Print create-project payload and exit
  -h, --help                Show help

Env:
  AWORK_CLI       CLI executable override
  AWORK_CLI_ARGS  CLI args override
");
    }
}
