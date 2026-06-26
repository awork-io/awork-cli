using System.ComponentModel;
using Awk.Models;
using Spectre.Console.Cli;

namespace Awk.Commands;

internal sealed class LinkGetSettings : BaseSettings
{
    [CommandArgument(0, "<TYPE>")]
    [Description("Entity or app destination to link.")]
    public string Type { get; set; } = string.Empty;

    [CommandArgument(1, "[ID]")]
    [Description("Entity id or key. Not required for static app destinations.")]
    public string? Id { get; set; }

    [CommandOption("--project <KEY_OR_ID>")]
    [Description("Parent project key or id for task-list links.")]
    public string? Project { get; set; }

    [CommandOption("--project-id <KEY_OR_ID>")]
    [Description("Alias for --project.")]
    public string? ProjectId { get; set; }

    [CommandOption("--task <KEY_OR_ID>")]
    [Description("Parent task key or id for comment links.")]
    public string? Task { get; set; }

    [CommandOption("--task-id <KEY_OR_ID>")]
    [Description("Alias for --task.")]
    public string? TaskId { get; set; }
}

internal sealed class LinkResolveSettings : BaseSettings
{
    [CommandArgument(0, "<KEY_OR_ID>")]
    [Description("Task key or entity id to resolve.")]
    public string KeyOrId { get; set; } = string.Empty;
}

internal sealed class LinkGetCommand : CommandBase<LinkGetSettings>
{
    protected override Task<int> ExecuteAsync(CommandContext context, LinkGetSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var target = LinkTarget.FromSettings(settings.Type, settings.Id, settings.Project ?? settings.ProjectId, settings.Task ?? settings.TaskId);
            return Task.FromResult(Output(LinkResponse.Create(target, target.TraceId)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OutputError(ex));
        }
    }
}

internal sealed class LinkResolveCommand : CommandBase<LinkResolveSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, LinkResolveSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var target = Guid.TryParse(settings.KeyOrId, out _)
                ? await LinkResolver.ResolveId(await CreateClient(settings, cancellationToken), settings.KeyOrId, cancellationToken)
                : LinkTarget.FromTypeAndId("task", settings.KeyOrId, key: settings.KeyOrId);

            return Output(LinkResponse.Create(target, target.TraceId));
        }
        catch (Exception ex)
        {
            return OutputError(ex);
        }
    }
}

internal static class LinkResolver
{
    private static readonly LinkProbe[] IdProbes =
    [
        new("task", "/tasks/{0}"),
        new("project", "/projects/{0}"),
        new("user", "/users/{0}"),
        new("company", "/companies/{0}"),
        new("document", "/documents/{0}")
    ];

    internal static async Task<LinkTarget> ResolveId(Awk.Generated.AworkClient client, string id, CancellationToken cancellationToken)
    {
        var matches = new List<LinkTarget>();

        foreach (var probe in IdProbes)
        {
            var result = await client.Call("GET", string.Format(probe.ApiPathFormat, Uri.EscapeDataString(id)), null, null, null, cancellationToken);
            if (result.StatusCode == 404) continue;
            if (result.StatusCode is < 200 or >= 300)
            {
                throw new InvalidOperationException($"Could not resolve id as {probe.Type}. API returned HTTP {result.StatusCode}.");
            }

            matches.Add(LinkTarget.FromTypeAndId(probe.Type, id, result.Response, result.TraceId));
        }

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"Could not resolve '{id}' as a task, project, user, company, or document id."),
            _ => throw new InvalidOperationException($"'{id}' matched multiple entity types. Use 'awork links get <type> <id>'.")
        };
    }

    private sealed record LinkProbe(string Type, string ApiPathFormat);
}

internal sealed record LinkTarget(
    string Type,
    string? Id,
    string Path,
    object? Source = null,
    string? TraceId = null,
    string? Key = null,
    string? Project = null,
    string? Task = null)
{
    internal static LinkTarget FromTypeAndId(string type, string id, object? source = null, string? traceId = null, string? key = null)
    {
        var target = FromSettings(type, id, null, null);
        return target with { Source = source, TraceId = traceId, Key = key };
    }

    internal static LinkTarget FromSettings(string type, string? id, string? project, string? task)
    {
        var normalizedType = NormalizeType(type);
        var normalizedId = NormalizeOptional(id);
        var normalizedProject = NormalizeOptional(project);
        var normalizedTask = NormalizeOptional(task);
        var path = BuildPath(normalizedType, normalizedId, normalizedProject, normalizedTask);

        return new LinkTarget(
            normalizedType,
            normalizedId,
            path,
            Key: IsTaskKey(normalizedType, normalizedId) ? normalizedId : null,
            Project: normalizedProject,
            Task: normalizedTask);
    }

    private static string NormalizeType(string type)
    {
        var normalized = type.Trim().ToLowerInvariant();
        return normalized switch
        {
            "home" or "dashboard" => "dashboard",
            "account" or "account-settings" or "profile" or "settings" or "my-settings" => "account-settings",
            "planner" or "timeline" => "planner",
            "whats-new" or "what-is-new" or "whatsnew" => "whats-new",
            "task" or "tasks" => "task",
            "project" or "projects" => "project",
            "user" or "users" => "user",
            "company" or "companies" or "client" or "clients" => "company",
            "comment" or "comments" => "comment",
            "task-list" or "task-lists" or "tasklist" or "tasklists" => "task-list",
            "document" or "documents" or "doc" or "docs" => "document",
            "time-report" or "time-reports" or "timereport" or "timereports" => "time-report",
            "task-view" or "task-views" or "taskview" or "taskviews" => "task-view",
            _ => throw new InvalidOperationException("Unsupported link type. Supported types: dashboard, account-settings, planner, whats-new, task, project, user, company, comment, task-list, document, time-report, task-view.")
        };
    }

    private static string BuildPath(string type, string? id, string? project, string? task)
    {
        return type switch
        {
            "dashboard" => "/my/dashboard",
            "account-settings" => "/my/profile/mysettings",
            "planner" => "/planner/timeline/users",
            "whats-new" => "/whats-new",
            "task" => $"/tasks/{EscapeRequired(id, "task key or id")}",
            "project" => $"/projects/{EscapeRequired(id, "project key or id")}",
            "user" => $"/users/{EscapeRequired(id, "user id")}",
            "company" => $"/companies/{EscapeRequired(id, "company id")}",
            "document" => $"/docs/{EscapeRequired(id, "document id")}",
            "time-report" => $"/time-tracking/reports/{EscapeRequired(id, "time report id")}",
            "task-view" => $"/tasks/views/{EscapeRequired(id, "task view id")}",
            "comment" => $"/tasks/{EscapeRequired(task, "task key or id")}?comment={EscapeRequired(id, "comment id")}",
            "task-list" => $"/projects/{EscapeRequired(project, "project key or id")}/tasks/list?list={EscapeRequired(id, "task list id")}",
            _ => throw new InvalidOperationException($"Unsupported link type '{type}'.")
        };
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string EscapeRequired(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} is required for this link type.");
        }

        return Uri.EscapeDataString(value.Trim());
    }

    private static bool IsTaskKey(string type, string? id) =>
        type == "task" && id is not null && !Guid.TryParse(id, out _);
}

internal static class LinkResponse
{
    private const string BaseUrl = "https://app.awork.com";

    internal static ResponseEnvelope<object?> Create(LinkTarget target, string? traceId)
    {
        var response = new
        {
            type = target.Type,
            id = target.Id,
            key = target.Key,
            project = target.Project,
            task = target.Task,
            path = target.Path,
            url = BaseUrl + target.Path,
            baseUrl = BaseUrl
        };

        return new ResponseEnvelope<object?>(200, traceId, response);
    }
}
