using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Awk.CodeGen;

[Generator]
public sealed class SwaggerClientGenerator : ISourceGenerator
{
    private static readonly HashSet<string> BaseSettingsPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "EnvFile",
        "Token",
        "AuthMode",
        "ConfigPath",
        "Body",
        "Set",
        "SetJson"
    };

    private static readonly HashSet<string> BaseOptionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "env",
        "token",
        "auth-mode",
        "config",
        "body",
        "set",
        "set-json"
    };

    private static readonly string[] DomainOrder =
    {
        "users",
        "tasks",
        "projects",
        "times",
        "workspace",
        "documents",
        "files",
        "search",
        "integrations",
        "automation"
    };

    private static readonly Dictionary<string, string> DomainDescriptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["users"] = "Users",
        ["tasks"] = "Tasks",
        ["projects"] = "Projects",
        ["times"] = "Times",
        ["workspace"] = "Workspace",
        ["documents"] = "Documents",
        ["files"] = "Files",
        ["search"] = "Search",
        ["integrations"] = "Integrations",
        ["automation"] = "Automation",
        ["auth"] = "Auth"
    };

    private static readonly Dictionary<string, string> TagDomainMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Accounts"] = "auth",
        ["ClientApplications"] = "auth",

        ["Users"] = "users",
        ["ApiUsers"] = "users",
        ["Invitations"] = "users",
        ["UserTags"] = "users",
        ["UserFiles"] = "users",
        ["UserCapacities"] = "users",

        ["Tasks"] = "tasks",
        ["PrivateTasks"] = "tasks",
        ["AssignedTasks"] = "tasks",
        ["TaskComments"] = "tasks",
        ["TaskFiles"] = "tasks",
        ["TaskTags"] = "tasks",
        ["TaskLists"] = "tasks",
        ["TaskSchedules"] = "tasks",
        ["TaskStatuses"] = "tasks",
        ["TaskViews"] = "tasks",
        ["TaskBundles"] = "tasks",
        ["TaskDependencies"] = "tasks",
        ["TaskDependencyTemplates"] = "tasks",
        ["TaskTemplates"] = "tasks",
        ["TaskTemplateFiles"] = "tasks",
        ["ChecklistItems"] = "tasks",

        ["Projects"] = "projects",
        ["ProjectTasks"] = "projects",
        ["ProjectMembers"] = "projects",
        ["ProjectComments"] = "projects",
        ["ProjectFiles"] = "projects",
        ["ProjectTags"] = "projects",
        ["ProjectStatuses"] = "projects",
        ["ProjectRoles"] = "projects",
        ["ProjectTypes"] = "projects",
        ["ProjectMilestones"] = "projects",
        ["ProjectMilestoneTemplates"] = "projects",
        ["ProjectTemplates"] = "projects",
        ["ProjectTemplateFiles"] = "projects",
        ["ProjectTemplateTags"] = "projects",
        ["Project Automations"] = "projects",
        ["Project Template Automations"] = "projects",
        ["Retainers"] = "projects",

        ["TimeEntries"] = "times",
        ["TimeBookings"] = "times",
        ["TimeReports"] = "times",
        ["TimeTracking"] = "times",
        ["Workload"] = "times",
        ["Absences"] = "times",

        ["Workspaces"] = "workspace",
        ["WorkspaceFiles"] = "workspace",
        ["WorkspaceAbsences"] = "workspace",
        ["Teams"] = "workspace",
        ["Roles"] = "workspace",
        ["Permissions"] = "workspace",
        ["CustomFields"] = "workspace",
        ["TypeOfWork"] = "workspace",
        ["Companies"] = "workspace",
        ["CompanyFiles"] = "workspace",
        ["CompanyTags"] = "workspace",
        ["Dashboards"] = "workspace",
        ["Activities"] = "workspace",
        ["AbsenceRegions"] = "workspace",

        ["Documents"] = "documents",
        ["DocumentFiles"] = "documents",
        ["DocumentComments"] = "documents",
        ["DocumentSpaces"] = "documents",

        ["Files"] = "files",
        ["FileUpload"] = "files",
        ["TemporaryFiles"] = "files",
        ["SharedFiles"] = "files",
        ["Images"] = "files",
        ["CommentFiles"] = "files",

        ["Search"] = "search",

        ["Webhooks"] = "integrations",

        ["Autopilot"] = "automation"
    };

    private static readonly Dictionary<string, string> TagSubOverrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ApiUsers"] = "api-users",
        ["ChecklistItems"] = "checklist-items",
        ["CompanyFiles"] = "company-files",
        ["CompanyTags"] = "company-tags",
        ["CommentFiles"] = "comment-files",
        ["FileUpload"] = "upload"
    };

    private static readonly HashSet<string> RootTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "Users",
        "Tasks",
        "Projects",
        "Workspaces",
        "Documents",
        "Files",
        "Search"
    };

    private static readonly Dictionary<string, string> DomainPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["users"] = "User",
        ["tasks"] = "Task",
        ["projects"] = "Project",
        ["times"] = "Time",
        ["workspace"] = "Workspace",
        ["documents"] = "Document",
        ["files"] = "File"
    };

    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var swagger = FindSwagger(context);
        if (swagger is null) return;

        var swaggerText = swagger.GetText(context.CancellationToken);
        if (swaggerText is null) return;

        var swaggerJson = swaggerText.ToString();
        if (string.IsNullOrWhiteSpace(swaggerJson)) return;

        using var doc = JsonDocument.Parse(swaggerJson);
        var root = doc.RootElement;

        if (!root.TryGetProperty("components", out var components) || !components.TryGetProperty("schemas", out var schemas))
        {
            return;
        }

        if (!root.TryGetProperty("paths", out var paths))
        {
            return;
        }

        var schemaMap = schemas.EnumerateObject().ToDictionary(k => k.Name, v => v.Value, StringComparer.Ordinal);

        var dtoSource = GenerateDtos(schemas);
        context.AddSource("AworkDtos.g.cs", SourceText.From(dtoSource, Encoding.UTF8));

        var clientSource = GenerateClient(paths, schemaMap);
        context.AddSource("AworkClient.Operations.g.cs", SourceText.From(clientSource, Encoding.UTF8));

        var cliSource = GenerateCli(paths, schemaMap);
        context.AddSource("AworkCli.g.cs", SourceText.From(cliSource, Encoding.UTF8));
    }

    private static AdditionalText? FindSwagger(GeneratorExecutionContext context)
    {
        foreach (var file in context.AdditionalFiles)
        {
            var name = Path.GetFileName(file.Path);
            if (string.Equals(name, "swagger.json", StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        return null;
    }

    private static string GenerateClient(JsonElement paths, Dictionary<string, JsonElement> schemaMap)
    {
        var operations = CollectOperationInfos(paths, schemaMap);


        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Threading;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();
        sb.AppendLine("namespace Awk.Generated;");
        sb.AppendLine();
        sb.AppendLine("public sealed partial class AworkClient");
        sb.AppendLine("{");

        foreach (var op in operations)
        {
            var pathParams = OrderPathParams(op.Path, op.Parameters.Where(p => p.Location == "path").ToList());
            var queryParams = op.Parameters.Where(p => p.Location == "query").ToList();
            var hasQuery = queryParams.Count > 0;

            sb.Append("    public Task<Awk.Models.ResponseEnvelope<object?>> ").Append(op.MethodName).Append('(');

            var first = true;
            foreach (var param in pathParams)
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append("string ").Append(param.Identifier);
            }

            if (op.HasBody)
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append("object?").Append(op.BodyRequired ? " " : " ").Append("body");
                if (!op.BodyRequired)
                {
                    sb.Append(" = null");
                }
            }

            if (hasQuery)
            {
                if (!first) sb.Append(", ");
                first = false;
                sb.Append("Dictionary<string, object?>? query = null");
            }

            if (!first) sb.Append(", ");
            sb.Append("CancellationToken cancellationToken = default");

            sb.AppendLine(")");
            sb.AppendLine("    {");

            var pathExpr = BuildPathExpression(op.Path, pathParams);
            var queryArg = hasQuery ? "query" : "null";
            var bodyArg = op.HasBody ? "body" : "null";
            var contentArg = string.IsNullOrWhiteSpace(op.ContentType) ? "null" : "\"" + op.ContentType + "\"";

            sb.Append("        return Call(\"").Append(op.Method).Append("\", ")
                .Append(pathExpr)
                .Append(", ")
                .Append(queryArg)
                .Append(", ")
                .Append(bodyArg)
                .Append(", ")
                .Append(contentArg)
                .Append(", cancellationToken);");
            sb.AppendLine();
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string GenerateCli(JsonElement paths, Dictionary<string, JsonElement> schemaMap)
    {
        var operations = CollectOperationInfos(paths, schemaMap);
        var commandInfos = new List<CommandInfo>();

        var classNameUsed = new HashSet<string>(StringComparer.Ordinal);
        var collectionPathsWithItem = BuildCollectionPathsWithItem(operations);

        foreach (var group in operations.GroupBy(o => o.Tag).OrderBy(g => g.Key))
        {
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var baseNames = group.ToDictionary(op => op, op => BuildCommandName(op, collectionPathsWithItem));
            var duplicates = new HashSet<string>(
                baseNames.Values
                    .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1)
                    .Select(g => g.Key),
                StringComparer.OrdinalIgnoreCase);

            foreach (var op in group)
            {
                var commandName = baseNames[op];
                if (duplicates.Contains(commandName))
                {
                    var literalSegments = GetPathSegments(op.Path).Where(s => !IsParamSegment(s)).ToList();
                    if (literalSegments.Count >= 2)
                    {
                        commandName = ToKebabCase(literalSegments[literalSegments.Count - 2]) + "-" + commandName;
                    }
                }

                commandName = EnsureUniqueCommandName(commandName, op, usedNames);

                var className = SanitizeIdentifier(op.OperationId);
                if (IsGenericOperationId(className))
                {
                    className = className + PathToName(op.Path);
                }

                if (!classNameUsed.Add(className))
                {
                    className = className + PathToName(op.Path) + op.Method;
                    if (!classNameUsed.Add(className))
                    {
                        var suffix = 2;
                        while (!classNameUsed.Add(className + suffix)) suffix++;
                        className = className + suffix;
                    }
                }

                commandInfos.Add(new CommandInfo(op, commandName, className));
            }
        }
        var tagInfos = commandInfos
            .Select(c => c.Operation.Tag)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(tag => tag, ResolveTagGroupInfo, StringComparer.OrdinalIgnoreCase);

        var domainGroups = commandInfos
            .GroupBy(c => tagInfos[c.Operation.Tag].Domain, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using Spectre.Console.Cli;");
        sb.AppendLine("using Awk.Commands;");
        sb.AppendLine();
        sb.AppendLine("namespace Awk.Generated;");
        sb.AppendLine();
        sb.AppendLine("internal static class GeneratedCli");
        sb.AppendLine("{");
        sb.AppendLine("    internal static void Register(IConfigurator config)");
        sb.AppendLine("    {");

        var orderedDomains = domainGroups.Keys
            .OrderBy(DomainSortKey)
            .ThenBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var domain in orderedDomains)
        {
            if (string.Equals(domain, "auth", StringComparison.OrdinalIgnoreCase)) continue;
            if (!domainGroups.TryGetValue(domain, out var domainCommands)) continue;

            sb.AppendLine($"        config.AddBranch(\"{domain}\", branch =>");
            sb.AppendLine("        {");
            sb.AppendLine($"            branch.SetDescription(\"{EscapeString(GetDomainDescription(domain))}\");");

            var tagGroups = domainCommands
                .GroupBy(c => c.Operation.Tag, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => tagInfos[g.Key].SubTag ?? tagInfos[g.Key].Tag, StringComparer.OrdinalIgnoreCase);

            foreach (var tagGroup in tagGroups)
            {
                var tagInfo = tagInfos[tagGroup.Key];
                if (tagInfo.SubTag is null)
                {
                    foreach (var command in tagGroup.OrderBy(c => c.CommandName))
                    {
                        if (!string.IsNullOrWhiteSpace(command.Operation.Summary))
                        {
                            sb.AppendLine($"            branch.AddCommand<{command.ClassName}>(\"{command.CommandName}\")");
                            sb.AppendLine($"                .WithDescription(\"{EscapeString(command.Operation.Summary!)}\");");
                        }
                        else
                        {
                            sb.AppendLine($"            branch.AddCommand<{command.ClassName}>(\"{command.CommandName}\");");
                        }
                    }
                }
                else
                {
                    sb.AppendLine($"            branch.AddBranch(\"{tagInfo.SubTag}\", sub =>");
                    sb.AppendLine("            {");
                    sb.AppendLine($"                sub.SetDescription(\"{EscapeString(tagInfo.Tag)}\");");

                    foreach (var command in tagGroup.OrderBy(c => c.CommandName))
                    {
                        if (!string.IsNullOrWhiteSpace(command.Operation.Summary))
                        {
                            sb.AppendLine($"                sub.AddCommand<{command.ClassName}>(\"{command.CommandName}\")");
                            sb.AppendLine($"                    .WithDescription(\"{EscapeString(command.Operation.Summary!)}\");");
                        }
                        else
                        {
                            sb.AppendLine($"                sub.AddCommand<{command.ClassName}>(\"{command.CommandName}\");");
                        }
                    }

                    sb.AppendLine("            });");
                }
            }

            sb.AppendLine("        });");
        }

        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    internal static void RegisterAuth(IConfigurator<CommandSettings> branch)");
        sb.AppendLine("    {");

        if (domainGroups.TryGetValue("auth", out var authCommands))
        {
            var authTagGroups = authCommands
                .GroupBy(c => c.Operation.Tag, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => tagInfos[g.Key].SubTag ?? tagInfos[g.Key].Tag, StringComparer.OrdinalIgnoreCase);

            foreach (var tagGroup in authTagGroups)
            {
                var tagInfo = tagInfos[tagGroup.Key];
                if (tagInfo.SubTag is null)
                {
                    foreach (var command in tagGroup.OrderBy(c => c.CommandName))
                    {
                        if (!string.IsNullOrWhiteSpace(command.Operation.Summary))
                        {
                            sb.AppendLine($"        branch.AddCommand<{command.ClassName}>(\"{command.CommandName}\")");
                            sb.AppendLine($"            .WithDescription(\"{EscapeString(command.Operation.Summary!)}\");");
                        }
                        else
                        {
                            sb.AppendLine($"        branch.AddCommand<{command.ClassName}>(\"{command.CommandName}\");");
                        }
                    }
                }
                else
                {
                    sb.AppendLine($"        branch.AddBranch(\"{tagInfo.SubTag}\", sub =>");
                    sb.AppendLine("        {");
                    sb.AppendLine($"            sub.SetDescription(\"{EscapeString(tagInfo.Tag)}\");");

                    foreach (var command in tagGroup.OrderBy(c => c.CommandName))
                    {
                        if (!string.IsNullOrWhiteSpace(command.Operation.Summary))
                        {
                            sb.AppendLine($"            sub.AddCommand<{command.ClassName}>(\"{command.CommandName}\")");
                            sb.AppendLine($"                .WithDescription(\"{EscapeString(command.Operation.Summary!)}\");");
                        }
                        else
                        {
                            sb.AppendLine($"            sub.AddCommand<{command.ClassName}>(\"{command.CommandName}\");");
                        }
                    }

                    sb.AppendLine("        });");
                }
            }
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();

        foreach (var command in commandInfos)
        {
            var op = command.Operation;
            var className = command.ClassName;
            var settingsName = "Settings";
            var pathParams = OrderPathParams(op.Path, op.Parameters.Where(p => p.Location == "path").ToList());
            var queryParams = op.Parameters.Where(p => p.Location == "query").ToList();
            var hasQuery = queryParams.Count > 0;

            sb.AppendLine($"internal sealed class {className} : CommandBase<{className}.{settingsName}>");
            sb.AppendLine("{");
            sb.AppendLine($"    internal sealed class {settingsName} : BaseSettings");
            sb.AppendLine("    {");

            var argIndex = 0;
            foreach (var param in pathParams)
            {
                var argLabel = param.Required ? $"<{param.OptionName}>" : $"[{param.OptionName}]";
                var type = param.IsArray ? "string[]?" : (param.Required ? "string" : "string?");
                sb.AppendLine($"        [CommandArgument({argIndex}, \"{argLabel}\")]");
                if (param.Required && !param.IsArray)
                {
                    sb.AppendLine($"        public {type} {param.PropertyName} {{ get; init; }} = string.Empty;");
                }
                else
                {
                    sb.AppendLine($"        public {type} {param.PropertyName} {{ get; init; }}");
                }
                sb.AppendLine();
                argIndex++;
            }

            foreach (var param in queryParams)
            {
                var option = $"--{param.OptionName} <VALUE>";
                var type = param.IsArray ? "string[]?" : "string?";
                sb.AppendLine($"        [CommandOption(\"{option}\")]");
                sb.AppendLine($"        public {type} {param.PropertyName} {{ get; init; }}");
                sb.AppendLine();
            }

            if (op.HasBody)
            {
                sb.AppendLine("        [CommandOption(\"--body <JSON_OR_@FILE>\")]");
                sb.AppendLine("        public string? Body { get; init; }");
                sb.AppendLine();

                sb.AppendLine("        [CommandOption(\"--set <KEY=VALUE>\")]");
                sb.AppendLine("        public string[]? Set { get; init; }");
                sb.AppendLine();

                sb.AppendLine("        [CommandOption(\"--set-json <KEY=JSON_OR_@FILE>\")]");
                sb.AppendLine("        public string[]? SetJson { get; init; }");
                sb.AppendLine();

                var reserved = new HashSet<string>(pathParams.Concat(queryParams).Select(p => p.PropertyName), StringComparer.OrdinalIgnoreCase);
                reserved.UnionWith(BaseSettingsPropertyNames);
                if (op.BodyProperties is not null)
                {
                    foreach (var bodyProp in op.BodyProperties.Where(p => p.Kind is BodyPropertyKind.Scalar or BodyPropertyKind.Array))
                    {
                        var propertyName = bodyProp.PropertyName;
                        var optionName = bodyProp.OptionName;
                        if (reserved.Contains(propertyName))
                        {
                            propertyName = propertyName + "Body";
                            optionName = "body-" + optionName;
                        }

                        var type = bodyProp.Kind == BodyPropertyKind.Array ? "string[]?" : "string?";
                        sb.AppendLine($"        [CommandOption(\"--{optionName} <VALUE>\")]");
                        sb.AppendLine($"        public {type} {propertyName} {{ get; init; }}");
                        sb.AppendLine();
                    }
                }
            }

            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine($"    protected override async Task<int> ExecuteAsync(CommandContext context, {settingsName} settings, CancellationToken cancellationToken)");
            sb.AppendLine("    {");
            sb.AppendLine("        try");
            sb.AppendLine("        {");
            sb.AppendLine("            var client = await CreateClient(settings, cancellationToken);");

            foreach (var param in pathParams)
            {
                if (param.Required)
                {
                    sb.AppendLine($"            if (CommandHelpers.IsMissing(settings.{param.PropertyName})) throw new InvalidOperationException(\"Missing <{param.OptionName}>.\");");
                }
            }
            foreach (var param in queryParams)
            {
                if (param.Required)
                {
                    sb.AppendLine($"            if (CommandHelpers.IsMissing(settings.{param.PropertyName})) throw new InvalidOperationException(\"Missing --{param.OptionName}.\");");
                }
            }

            if (hasQuery)
            {
                sb.AppendLine("            var query = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);");
                foreach (var param in queryParams)
                {
                    var property = $"settings.{param.PropertyName}";
                    if (param.IsArray)
                    {
                        sb.AppendLine($"            if (settings.{param.PropertyName} is {{ Length: > 0 }}) query[\"{param.Name}\"] = {property};");
                    }
                    else
                    {
                        sb.AppendLine($"            if (!string.IsNullOrWhiteSpace({property})) query[\"{param.Name}\"] = {property};");
                    }
                }
            }

            if (op.HasBody)
            {
                sb.AppendLine("            var setPairs = new List<string>();");
                sb.AppendLine("            var setJsonPairs = new List<string>();");

                var reserved = new HashSet<string>(pathParams.Concat(queryParams).Select(p => p.PropertyName), StringComparer.OrdinalIgnoreCase);
                reserved.UnionWith(BaseSettingsPropertyNames);
                if (op.BodyProperties is not null)
                {
                    foreach (var bodyProp in op.BodyProperties.Where(p => p.Kind is BodyPropertyKind.Scalar or BodyPropertyKind.Array))
                    {
                        var propertyName = bodyProp.PropertyName;
                        var optionName = bodyProp.OptionName;
                        if (reserved.Contains(propertyName))
                        {
                            propertyName = propertyName + "Body";
                            optionName = "body-" + optionName;
                        }

                        if (bodyProp.Kind == BodyPropertyKind.Array)
                        {
                            sb.AppendLine($"            if (settings.{propertyName} is {{ Length: > 0 }}) setJsonPairs.Add(\"{bodyProp.Name}=\" + System.Text.Json.JsonSerializer.Serialize(settings.{propertyName}));");
                        }
                        else
                        {
                            sb.AppendLine($"            if (!string.IsNullOrWhiteSpace(settings.{propertyName})) setPairs.Add(\"{bodyProp.Name}=\" + settings.{propertyName});");
                        }
                    }
                }

                sb.AppendLine("            var mergedSet = CommandHelpers.MergePairs(settings.Set, setPairs);");
                sb.AppendLine("            var mergedSetJson = CommandHelpers.MergePairs(settings.SetJson, setJsonPairs);");

                if (op.BodyProperties is { Count: > 0 })
                {
                    sb.AppendLine("            ValidateBodyKeys(mergedSet, mergedSetJson, AllowedBodyKeys);");
                }

                sb.AppendLine("            var body = CommandHelpers.BuildBody(settings.Body, mergedSet, mergedSetJson);");
                if (op.BodyRequired)
                {
                    sb.AppendLine("            if (body is null) throw new InvalidOperationException(\"Body is required.\");");
                }
            }

            var methodCall = new StringBuilder();
            methodCall.Append($"            var result = await client.{op.MethodName}(");
            var argFirst = true;
            foreach (var param in pathParams)
            {
                if (!argFirst) methodCall.Append(", ");
                argFirst = false;
                methodCall.Append($"settings.{param.PropertyName}");
            }
            if (op.HasBody)
            {
                if (!argFirst) methodCall.Append(", ");
                argFirst = false;
                methodCall.Append("body");
            }
            if (hasQuery)
            {
                if (!argFirst) methodCall.Append(", ");
                argFirst = false;
                methodCall.Append("query");
            }
            if (!argFirst) methodCall.Append(", ");
            methodCall.Append("cancellationToken");
            methodCall.Append(");");
            sb.AppendLine(methodCall.ToString());
            sb.AppendLine("            return Output(result);");
            sb.AppendLine("        }");
            sb.AppendLine("        catch (Exception ex)");
            sb.AppendLine("        {");
            sb.AppendLine("            return OutputError(ex);");
            sb.AppendLine("        }");
            sb.AppendLine("    }");

            if (op.BodyProperties is { Count: > 0 })
            {
                sb.AppendLine();
                sb.Append("    private static readonly HashSet<string> AllowedBodyKeys = new(StringComparer.OrdinalIgnoreCase) { ");
                sb.Append(string.Join(", ", op.BodyProperties.Select(p => $"\"{p.Name}\"")));
                sb.AppendLine(" };");

                sb.AppendLine();
                sb.AppendLine("    private static void ValidateBodyKeys(IEnumerable<string>? setPairs, IEnumerable<string>? setJsonPairs, HashSet<string> allowed)");
                sb.AppendLine("    {");
                sb.AppendLine("        if ((setPairs is null || !setPairs.Any()) && (setJsonPairs is null || !setJsonPairs.Any())) return;");
                sb.AppendLine("        var all = new List<string>();");
                sb.AppendLine("        if (setPairs is not null) all.AddRange(setPairs);");
                sb.AppendLine("        if (setJsonPairs is not null) all.AddRange(setJsonPairs);");
                sb.AppendLine("        foreach (var pair in all)");
                sb.AppendLine("        {");
                sb.AppendLine("            if (string.IsNullOrWhiteSpace(pair)) continue;");
                sb.AppendLine("            var idx = pair.IndexOf('=');");
                sb.AppendLine("            if (idx <= 0) throw new InvalidOperationException($\"Invalid key/value '{pair}'. Use KEY=VALUE.\");");
                sb.AppendLine("            var key = pair.Substring(0, idx).Trim();");
                sb.AppendLine("            if (string.IsNullOrWhiteSpace(key)) continue;");
                sb.AppendLine("            var top = key.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)[0];");
                sb.AppendLine("            if (!allowed.Contains(top))");
                sb.AppendLine("            {");
                sb.AppendLine("                var allowedList = string.Join(\", \", allowed.OrderBy(x => x));");
                sb.AppendLine("                throw new InvalidOperationException($\"Unknown body field '{top}'. Allowed: {allowedList}.\");");
                sb.AppendLine("            }");
                sb.AppendLine("        }");
                sb.AppendLine("    }");
            }

            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static List<OperationInfo> CollectOperationInfos(JsonElement paths, Dictionary<string, JsonElement> schemaMap)
    {
        var list = new List<OperationInfo>();
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var pathSet = new HashSet<string>(paths.EnumerateObject().Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var pathEntry in paths.EnumerateObject())
        {
            var path = pathEntry.Name;
            if (string.Equals(path, "/users/me", StringComparison.OrdinalIgnoreCase) && pathSet.Contains("/me"))
            {
                continue;
            }
            var pathItem = pathEntry.Value;
            foreach (var opEntry in pathItem.EnumerateObject())
            {
                var method = opEntry.Name;
                if (method.StartsWith("x-", StringComparison.OrdinalIgnoreCase)) continue;

                var op = opEntry.Value;
                var opId = op.TryGetProperty("operationId", out var opIdProp) ? opIdProp.GetString() : null;
                if (string.IsNullOrWhiteSpace(opId)) continue;

                var summary = op.TryGetProperty("summary", out var summaryProp) ? summaryProp.GetString() : null;
                var tags = new List<string>();
                if (op.TryGetProperty("tags", out var tagProp) && tagProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tag in tagProp.EnumerateArray())
                    {
                        if (tag.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(tag.GetString()))
                        {
                            tags.Add(tag.GetString()!);
                        }
                    }
                }

                var tagName = tags.Count > 0 ? tags[0] : "Core";
                var parameters = CollectParameters(pathItem, op);
                EnsurePathParameters(path, parameters);
                var (hasBody, bodyRequired, contentType) = GetRequestBody(op);
                var bodyProperties = GetBodyPropertyInfos(op, schemaMap);

                var methodName = SanitizeIdentifier(opId!);
                if (IsGenericOperationId(methodName))
                {
                    methodName = methodName + PathToName(path);
                }

                if (!usedNames.Add(methodName))
                {
                    methodName = methodName + PathToName(path) + method.ToUpperInvariant();
                    if (!usedNames.Add(methodName))
                    {
                        var suffix = 2;
                        while (!usedNames.Add(methodName + suffix)) suffix++;
                        methodName = methodName + suffix;
                    }
                }

                list.Add(new OperationInfo(
                    tagName,
                    method.ToUpperInvariant(),
                    path,
                    opId!,
                    methodName,
                    summary,
                    parameters,
                    hasBody,
                    bodyRequired,
                    contentType,
                    bodyProperties));
            }
        }

        return list;
    }

    private static string BuildCommandName(OperationInfo op, HashSet<string> collectionPathsWithItem)
    {
        var segments = GetPathSegments(op.Path);
        if (segments.Count == 0) return ToKebabCase(op.OperationId);

        if (string.Equals(segments[segments.Count - 1], "me", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Count == 1) return "me";
            return ToKebabCase(segments[0]) + "-me";
        }

        var lastSegment = segments[segments.Count - 1];
        var lastIsParam = IsParamSegment(lastSegment);
        var paramName = lastIsParam ? lastSegment.Substring(1, lastSegment.Length - 2) : string.Empty;
        var literalSegments = segments.Where(s => !IsParamSegment(s)).ToList();
        var lastLiteral = literalSegments.Count > 0 ? literalSegments[literalSegments.Count - 1] : string.Empty;
        var normalizedLastLiteral = NormalizeSegment(lastLiteral);
        var lastLiteralKebab = ToKebabCase(normalizedLastLiteral);
        var isNestedResource = literalSegments.Count > 1;
        var resourceName = lastLiteralKebab;
        var tagName = ToKebabCase(NormalizeSegment(op.Tag));
        var tagMatchesResource = string.Equals(resourceName, tagName, StringComparison.OrdinalIgnoreCase);
        var isTopLevelResource = literalSegments.Count == 1;
        var itemResourceName = IsPluralResource(resourceName) ? SingularizeResource(resourceName) : resourceName;
        var hasItemPath = collectionPathsWithItem.Contains(op.Path);
        var isActionSegment = IsActionSegment(normalizedLastLiteral);
        var hasParamBeforeLastLiteral = segments.Take(segments.Count - 1).Any(IsParamSegment);
        var parentLiteral = literalSegments.Count > 1 ? literalSegments[literalSegments.Count - 2] : string.Empty;
        var parentName = SingularizeResource(ToKebabCase(NormalizeSegment(parentLiteral)));
        var actionParentName = GetActionParentName(segments);
        var actionName = isActionSegment ? BuildActionNameWithParent(resourceName, actionParentName) : resourceName;
        var isNameParam = lastIsParam && !string.IsNullOrWhiteSpace(paramName) &&
            paramName.EndsWith("name", StringComparison.OrdinalIgnoreCase) &&
            !paramName.EndsWith("id", StringComparison.OrdinalIgnoreCase);

        if (op.Method == "GET" && isNameParam)
        {
            return "get-by-" + ToKebabCase(paramName);
        }

        var baseName = op.Method switch
        {
            "GET" => lastIsParam
                ? (isTopLevelResource && !tagMatchesResource ? "get-" + itemResourceName : (isNestedResource ? "get-" + itemResourceName : "get"))
                : (segments.Count == 1
                    ? (IsPluralResource(lastLiteralKebab)
                        ? (tagMatchesResource ? "list" : "list-" + resourceName)
                        : (tagMatchesResource ? "get-" + ToKebabCase(lastLiteral) : "get-" + resourceName))
                    : (isNestedResource && IsPluralResource(resourceName)
                        ? (hasParamBeforeLastLiteral && !string.IsNullOrWhiteSpace(parentName)
                            ? "list-" + parentName + "-" + resourceName
                            : "list-" + resourceName)
                        : (hasItemPath && isNestedResource ? "list-" + resourceName : "get-" + ToKebabCase(lastLiteral)))),
            "POST" => segments.Count == 1
                ? (tagMatchesResource ? "create" : "create-" + resourceName)
                : (isActionSegment ? actionName : (isNestedResource ? "create-" + resourceName : (IsPluralResource(lastLiteralKebab) ? "create" : lastLiteralKebab))),
            "PUT" => (lastIsParam || segments.Count == 1)
                ? (isActionSegment
                    ? actionName
                    : (isTopLevelResource && !tagMatchesResource ? "update-" + itemResourceName : (isNestedResource ? "update-" + itemResourceName : "update")))
                : (isActionSegment ? actionName : (isNestedResource ? "update-" + resourceName : ToKebabCase(lastLiteral))),
            "PATCH" => lastIsParam
                ? (isActionSegment ? actionName : (isTopLevelResource && !tagMatchesResource ? "patch-" + itemResourceName : (isNestedResource ? "patch-" + itemResourceName : "patch")))
                : (isActionSegment ? actionName : (isNestedResource ? "patch-" + resourceName : ToKebabCase(lastLiteral))),
            "DELETE" => lastIsParam
                ? (isTopLevelResource && !tagMatchesResource ? "delete-" + itemResourceName : (isNestedResource ? "delete-" + itemResourceName : "delete"))
                : (isNestedResource
                    ? (hasParamBeforeLastLiteral && !string.IsNullOrWhiteSpace(parentName)
                        ? "delete-" + parentName + "-" + resourceName
                        : "delete-" + resourceName)
                    : (segments.Count == 1 && !tagMatchesResource ? "delete-" + resourceName : "delete-" + ToKebabCase(lastLiteral))),
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = OpIdToCommand(op.OperationId, op.Tag);
        }

        return baseName;
    }

    private static bool IsPluralResource(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return false;
        if (segment.StartsWith("add-") || segment.StartsWith("remove-") || segment.StartsWith("update-")) return false;
        if (segment.StartsWith("set-") || segment.StartsWith("accept") || segment.StartsWith("assign")) return false;
        return segment.EndsWith("s", StringComparison.OrdinalIgnoreCase) || segment.EndsWith("items", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsActionSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return false;
        var lower = segment.ToLowerInvariant();
        if (lower.StartsWith("add-") || lower.StartsWith("remove-") || lower.StartsWith("update-")) return true;
        if (lower.StartsWith("set-") || lower.StartsWith("assign") || lower.StartsWith("unassign")) return true;
        if (lower.StartsWith("move-") || lower.StartsWith("delete")) return true;
        if (lower.StartsWith("change")) return true;
        if (lower.StartsWith("accept")) return true;
        if (lower.StartsWith("activate") || lower.StartsWith("deactivate")) return true;
        if (lower.StartsWith("start") || lower.StartsWith("stop") || lower.StartsWith("pause") || lower.StartsWith("resume")) return true;
        if (lower.StartsWith("archive") || lower.StartsWith("unarchive") || lower.StartsWith("setarchived")) return true;
        return false;
    }

    private static string NormalizeSegment(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return segment;
        var lower = segment.ToLowerInvariant();
        if (SegmentAliases.TryGetValue(lower, out var alias))
        {
            return alias;
        }
        foreach (var prefix in new[] { "add", "remove", "update", "set", "assign", "unassign", "move" })
        {
            if (!lower.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rest = lower.Substring(prefix.Length);
            if (KnownResources.Contains(rest))
            {
                return prefix + "-" + rest;
            }
        }

        return segment;
    }

    private static readonly Dictionary<string, string> SegmentAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["absenceregions"] = "absence-regions",
        ["addprojectmember"] = "add-project-member",
        ["addprojects"] = "add-projects",
        ["addtaskbundle"] = "add-task-bundle",
        ["addtasktemplates"] = "add-task-templates",
        ["allavailabletasks"] = "all-available-tasks",
        ["assignedtasks"] = "assigned-tasks",
        ["assignuserbyemail"] = "assign-user-by-email",
        ["changebasetypes"] = "change-base-types",
        ["changelists"] = "change-lists",
        ["changestatuses"] = "change-statuses",
        ["changesubtasks"] = "change-subtasks",
        ["changesubtaskstoparent"] = "change-subtasks-to-parent",
        ["changetypeofwork"] = "change-type-of-work",
        ["checklistitems"] = "checklist-items",
        ["checklistitemtemplates"] = "checklist-item-templates",
        ["clientapplications"] = "client-applications",
        ["contactinfo"] = "contact-info",
        ["contactpersons"] = "contact-persons",
        ["customfielddefinitions"] = "custom-field-definitions",
        ["deactivatedmenuitems"] = "deactivated-menu-items",
        ["deleterecurrency"] = "delete-recurrency",
        ["deletetags"] = "delete-tags",
        ["documentspaces"] = "document-spaces",
        ["eventtypes"] = "event-types",
        ["externalfiles"] = "external-files",
        ["generateapikey"] = "generate-api-key",
        ["generatesecret"] = "generate-secret",
        ["generateuploadurl"] = "generate-upload-url",
        ["otherprivatetasks"] = "other-private-tasks",
        ["privatedocuments"] = "private-documents",
        ["privatetasks"] = "private-tasks",
        ["projectfeatures"] = "project-features",
        ["projectmilestones"] = "project-milestones",
        ["projectroles"] = "project-roles",
        ["projectstatuses"] = "project-statuses",
        ["projecttasks"] = "project-tasks",
        ["projecttemplates"] = "project-templates",
        ["projecttimebookings"] = "project-time-bookings",
        ["projecttypes"] = "project-types",
        ["removebreaks"] = "remove-breaks",
        ["removeprojectmember"] = "remove-project-member",
        ["removeprojects"] = "remove-projects",
        ["removetasks"] = "remove-tasks",
        ["removetasktemplates"] = "remove-task-templates",
        ["removeusers"] = "remove-users",
        ["setarchived"] = "set-archived",
        ["setassignees"] = "set-assignees",
        ["setbillable"] = "set-billable",
        ["setbilled"] = "set-billed",
        ["setcustomfields"] = "set-custom-fields",
        ["setentity"] = "set-entity",
        ["setplannedefforts"] = "set-planned-efforts",
        ["setprojectkey"] = "set-project-key",
        ["setrecurrency"] = "set-recurrency",
        ["setresolved"] = "set-resolved",
        ["settaskpriority"] = "set-task-priority",
        ["settypeofwork"] = "set-type-of-work",
        ["setunbillable"] = "set-unbillable",
        ["setunbilled"] = "set-unbilled",
        ["shareddocuments"] = "shared-documents",
        ["sharedfiles"] = "shared-files",
        ["taskbundle"] = "task-bundle",
        ["taskbundles"] = "task-bundles",
        ["taskdependencies"] = "task-dependencies",
        ["taskdependencytemplates"] = "task-dependency-templates",
        ["tasklists"] = "task-lists",
        ["tasklist"] = "task-list",
        ["tasklisttemplates"] = "task-list-templates",
        ["taskstatuses"] = "task-statuses",
        ["taskschedules"] = "task-schedules",
        ["tasktemplates"] = "task-templates",
        ["taskviews"] = "task-views",
        ["temporaryfiles"] = "temporary-files",
        ["timebookings"] = "time-bookings",
        ["timeentries"] = "time-entries",
        ["timereports"] = "time-reports",
        ["timetracking"] = "time-tracking",
        ["typeofwork"] = "type-of-work",
        ["unlinkcustomfielddefinition"] = "unlink-custom-field-definition",
        ["updateorder"] = "update-order",
        ["updateprojectmember"] = "update-project-member",
        ["updateprojectstatusorder"] = "update-project-status-order",
        ["updatetags"] = "update-tags",
        ["workspaceabsences"] = "workspace-absences"
    };

    private static string SingularizeResource(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var parts = value.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count == 0) return value;
        parts[parts.Count - 1] = SingularizeToken(parts[parts.Count - 1]);
        return string.Join("-", parts);
    }

    private static string SingularizeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return token;
        if (token.EndsWith("ies", StringComparison.OrdinalIgnoreCase) && token.Length > 3)
        {
            return token.Substring(0, token.Length - 3) + "y";
        }
        if (token.EndsWith("ses", StringComparison.OrdinalIgnoreCase) && token.Length > 3)
        {
            return token.Substring(0, token.Length - 2);
        }
        if (token.EndsWith("s", StringComparison.OrdinalIgnoreCase) && !token.EndsWith("ss", StringComparison.OrdinalIgnoreCase) && token.Length > 1)
        {
            return token.Substring(0, token.Length - 1);
        }
        return token;
    }

    private static string GetActionParentName(List<string> segments)
    {
        for (var i = segments.Count - 2; i >= 0; i--)
        {
            if (!IsParamSegment(segments[i])) continue;
            if (i - 1 < 0) break;
            var literal = segments[i - 1];
            if (IsParamSegment(literal)) break;
            return SingularizeResource(ToKebabCase(NormalizeSegment(literal)));
        }

        return string.Empty;
    }

    private static string BuildActionNameWithParent(string actionName, string parentName)
    {
        if (string.IsNullOrWhiteSpace(actionName) || string.IsNullOrWhiteSpace(parentName)) return actionName;
        var parts = actionName.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return actionName;
        var verb = parts[0];
        var obj = string.Join("-", parts.Skip(1));
        if (!obj.EndsWith("tags", StringComparison.OrdinalIgnoreCase)) return actionName;
        if (obj.StartsWith(parentName + "-", StringComparison.OrdinalIgnoreCase)) return actionName;
        return $"{verb}-{parentName}-{obj}";
    }

    private static readonly HashSet<string> KnownResources = new(StringComparer.OrdinalIgnoreCase)
    {
        "user",
        "users",
        "project",
        "projects",
        "tags",
        "team",
        "teams",
        "role",
        "roles",
        "member",
        "members",
        "task",
        "tasks",
        "tasklist",
        "tasklists",
        "taskbundle",
        "taskbundles",
        "checklistitem",
        "checklistitems",
        "checklist",
        "comment",
        "comments"
    };

    private static string EnsureUniqueCommandName(string name, OperationInfo op, HashSet<string> used)
    {
        if (used.Add(name)) return name;

        var literalSegments = GetPathSegments(op.Path).Where(s => !IsParamSegment(s)).ToList();
        if (literalSegments.Count >= 2)
        {
            var alt = ToKebabCase(literalSegments[literalSegments.Count - 2]) + "-" + name;
            if (used.Add(alt)) return alt;
        }

        var methodSuffix = name + "-" + op.Method.ToLowerInvariant();
        if (used.Add(methodSuffix)) return methodSuffix;

        var opSuffix = name + "-" + ToKebabCase(StripVerb(op.OperationId));
        if (used.Add(opSuffix)) return opSuffix;

        var counter = 2;
        while (!used.Add($"{name}-{counter}")) counter++;
        return $"{name}-{counter}";
    }

    private static string OpIdToCommand(string opId, string tag)
    {
        var trimmed = StripVerb(opId);
        trimmed = StripTagPrefix(trimmed, tag);
        if (string.IsNullOrWhiteSpace(trimmed)) return ToKebabCase(opId);
        return ToKebabCase(trimmed);
    }

    private static string StripVerb(string opId)
    {
        foreach (var verb in new[] { "Get", "Post", "Put", "Delete", "Patch" })
        {
            if (opId.StartsWith(verb, StringComparison.Ordinal))
            {
                return opId.Substring(verb.Length);
            }
        }
        return opId;
    }

    private static string StripTagPrefix(string opId, string tag)
    {
        if (string.IsNullOrWhiteSpace(opId) || string.IsNullOrWhiteSpace(tag)) return opId;
        var tagPascal = ToPascalCase(tag);
        if (opId.StartsWith(tagPascal, StringComparison.Ordinal))
        {
            return opId.Substring(tagPascal.Length);
        }
        var tagSingular = tagPascal.EndsWith("s", StringComparison.Ordinal) ? tagPascal.Substring(0, tagPascal.Length - 1) : tagPascal;
        if (opId.StartsWith(tagSingular, StringComparison.Ordinal))
        {
            return opId.Substring(tagSingular.Length);
        }
        return opId;
    }

    private static List<string> GetPathSegments(string path) =>
        path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();

    private static List<string> GetPathParamNames(string path)
    {
        var names = new List<string>();
        foreach (var segment in GetPathSegments(path))
        {
            if (IsParamSegment(segment))
            {
                names.Add(segment.Substring(1, segment.Length - 2));
            }
        }
        return names;
    }

    private static bool IsParamSegment(string segment) =>
        segment.StartsWith("{", StringComparison.Ordinal) && segment.EndsWith("}", StringComparison.Ordinal);

    private static string EscapeString(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\t", " ");
    }

    private static List<ParameterInfo> OrderPathParams(string path, List<ParameterInfo> pathParams)
    {
        if (pathParams.Count <= 1) return pathParams;

        var ordered = new List<ParameterInfo>();
        var used = new HashSet<ParameterInfo>();
        foreach (var name in GetPathParamNames(path))
        {
            var match = pathParams.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match is null || used.Contains(match)) continue;
            ordered.Add(match);
            used.Add(match);
        }

        foreach (var param in pathParams)
        {
            if (!used.Contains(param)) ordered.Add(param);
        }

        return ordered;
    }

    private static HashSet<string> BuildCollectionPathsWithItem(List<OperationInfo> operations)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var op in operations)
        {
            var segments = GetPathSegments(op.Path);
            if (segments.Count == 0) continue;
            if (!IsParamSegment(segments[segments.Count - 1])) continue;
            var collection = "/" + string.Join("/", segments.Take(segments.Count - 1));
            set.Add(collection);
        }
        return set;
    }

    private sealed record OperationInfo(
        string Tag,
        string Method,
        string Path,
        string OperationId,
        string MethodName,
        string? Summary,
        List<ParameterInfo> Parameters,
        bool HasBody,
        bool BodyRequired,
        string? ContentType,
        List<BodyPropertyInfo>? BodyProperties
    );

    private enum BodyPropertyKind
    {
        Scalar,
        Array,
        Object,
        Unknown
    }

    private sealed record BodyPropertyInfo(
        string Name,
        string PropertyName,
        string OptionName,
        BodyPropertyKind Kind,
        bool Required
    );

    private sealed record CommandInfo(OperationInfo Operation, string CommandName, string ClassName);

    private static string GenerateDtos(JsonElement schemas)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Text.Json.Serialization;");
        sb.AppendLine();
        sb.AppendLine("namespace Awk.Generated;");
        sb.AppendLine();

        var schemaMap = schemas.EnumerateObject().ToDictionary(k => k.Name, v => v.Value, StringComparer.Ordinal);

        foreach (var schemaEntry in schemas.EnumerateObject())
        {
            var name = SanitizeIdentifier(schemaEntry.Name);
            var schema = schemaEntry.Value;

            if (schema.TryGetProperty("allOf", out var allOf))
            {
                schema = MergeAllOf(schema, allOf, schemaMap);
            }

            var isObject = schema.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "object";
            var hasProperties = schema.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object;

            if (isObject && hasProperties)
            {
                var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (schema.TryGetProperty("required", out var requiredProp) && requiredProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in requiredProp.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String) required.Add(item.GetString() ?? string.Empty);
                    }
                }

                sb.AppendLine("public sealed partial record " + name);
                sb.AppendLine("{");

                foreach (var prop in props.EnumerateObject())
                {
                    var propName = prop.Name;
                    var propSchema = prop.Value;
                    var isRequired = required.Contains(propName);
                    var typeInfo = ResolveType(propSchema, isRequired);
                    var propertyName = ToPascalCase(propName);
                    var identifier = SanitizeIdentifier(propertyName);

                    sb.AppendLine("    [JsonPropertyName(\"" + propName + "\")]");
                    sb.Append("    public ").Append(typeInfo.TypeName).Append(' ').Append(identifier).Append(" { get; init; }");
                    if (typeInfo.NeedsDefault)
                    {
                        sb.Append(" = default!;");
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("}");
                sb.AppendLine();
                continue;
            }

            var valueType = ResolveType(schema, true);
            sb.AppendLine("public sealed partial record " + name);
            sb.AppendLine("{");
            sb.Append("    public ").Append(valueType.TypeName).Append(" Value { get; init; }");
            if (valueType.NeedsDefault)
            {
                sb.Append(" = default!;");
            }
            sb.AppendLine();
            sb.AppendLine("}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static JsonElement MergeAllOf(JsonElement schema, JsonElement allOf, Dictionary<string, JsonElement> schemaMap)
    {
        var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in allOf.EnumerateArray())
        {
            var resolved = item;
            if (item.TryGetProperty("$ref", out var refProp))
            {
                var refName = ExtractRefName(refProp.GetString() ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(refName) && schemaMap.TryGetValue(refName, out var refSchema))
                {
                    resolved = refSchema;
                }
            }

            if (resolved.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in props.EnumerateObject())
                {
                    merged[prop.Name] = prop.Value;
                }
            }

            if (resolved.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in req.EnumerateArray())
                {
                    if (r.ValueKind == JsonValueKind.String) required.Add(r.GetString() ?? string.Empty);
                }
            }
        }

        using var doc = JsonDocument.Parse("{}");
        var baseObj = doc.RootElement;

        var builder = new StringBuilder();
        builder.Append("{\"type\":\"object\",\"properties\":{");
        var first = true;
        foreach (var kvp in merged)
        {
            if (!first) builder.Append(',');
            first = false;
            builder.Append("\"").Append(kvp.Key).Append("\":").Append(kvp.Value.GetRawText());
        }
        builder.Append("}");
        if (required.Count > 0)
        {
            builder.Append(",\"required\":[");
            var firstReq = true;
            foreach (var r in required)
            {
                if (!firstReq) builder.Append(',');
                firstReq = false;
                builder.Append("\"").Append(r).Append("\"");
            }
            builder.Append(']');
        }
        builder.Append('}');

        using var mergedDoc = JsonDocument.Parse(builder.ToString());
        return mergedDoc.RootElement.Clone();
    }

    private static (bool HasBody, bool Required, string? ContentType) GetRequestBody(JsonElement op)
    {
        if (!op.TryGetProperty("requestBody", out var body)) return (false, false, null);

        var required = body.TryGetProperty("required", out var req) && req.ValueKind == JsonValueKind.True;
        if (!body.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object)
        {
            return (true, required, "application/json");
        }

        foreach (var item in content.EnumerateObject())
        {
            return (true, required, item.Name);
        }

        return (true, required, "application/json");
    }

    private static List<BodyPropertyInfo>? GetBodyPropertyInfos(JsonElement op, Dictionary<string, JsonElement> schemaMap)
    {
        if (!op.TryGetProperty("requestBody", out var body)) return null;
        if (!body.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object) return null;

        foreach (var item in content.EnumerateObject())
        {
            if (!item.Value.TryGetProperty("schema", out var schema)) continue;
            return ResolveBodyProperties(schema, schemaMap);
        }

        return null;
    }

    private static List<BodyPropertyInfo>? ResolveBodyProperties(JsonElement schema, Dictionary<string, JsonElement> schemaMap)
    {
        if (schema.ValueKind == JsonValueKind.Null) return null;

        if (schema.TryGetProperty("$ref", out var refProp))
        {
            var name = ExtractRefName(refProp.GetString() ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(name) && schemaMap.TryGetValue(name, out var refSchema))
            {
                return ResolveBodyProperties(refSchema, schemaMap);
            }
        }

        if (schema.TryGetProperty("allOf", out var allOf) && allOf.ValueKind == JsonValueKind.Array)
        {
            var merged = new Dictionary<string, BodyPropertyInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in allOf.EnumerateArray())
            {
                var partProps = ResolveBodyProperties(part, schemaMap);
                if (partProps is null) continue;
                foreach (var p in partProps) merged[p.Name] = p;
            }
            return merged.Count > 0 ? merged.Values.ToList() : null;
        }

        if (schema.TryGetProperty("type", out var typeProp))
        {
            var type = typeProp.GetString();
            if (!string.Equals(type, "object", StringComparison.OrdinalIgnoreCase)) return null;
        }

        if (!schema.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (schema.TryGetProperty("required", out var requiredProp) && requiredProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in requiredProp.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String) required.Add(item.GetString() ?? string.Empty);
            }
        }

        var result = new List<BodyPropertyInfo>();
        foreach (var prop in props.EnumerateObject())
        {
            var propName = prop.Name;
            var kind = ResolveBodyPropertyKind(prop.Value, schemaMap);
            result.Add(new BodyPropertyInfo(
                propName,
                SanitizeIdentifier(ToPascalCase(propName)),
                ToKebabCase(propName),
                kind,
                required.Contains(propName)));
        }
        return result.Count > 0 ? result : null;
    }

    private static BodyPropertyKind ResolveBodyPropertyKind(JsonElement schema, Dictionary<string, JsonElement> schemaMap)
    {
        if (schema.ValueKind == JsonValueKind.Null) return BodyPropertyKind.Unknown;

        if (schema.TryGetProperty("$ref", out var refProp))
        {
            var name = ExtractRefName(refProp.GetString() ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(name) && schemaMap.TryGetValue(name, out var refSchema))
            {
                return ResolveBodyPropertyKind(refSchema, schemaMap);
            }
        }

        if (schema.TryGetProperty("allOf", out var allOf) && allOf.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in allOf.EnumerateArray())
            {
                var kind = ResolveBodyPropertyKind(part, schemaMap);
                if (kind != BodyPropertyKind.Unknown) return kind;
            }
        }

        if (schema.TryGetProperty("type", out var typeProp))
        {
            var type = typeProp.GetString();
            return type switch
            {
                "array" => BodyPropertyKind.Array,
                "object" => BodyPropertyKind.Object,
                "string" => BodyPropertyKind.Scalar,
                "integer" => BodyPropertyKind.Scalar,
                "number" => BodyPropertyKind.Scalar,
                "boolean" => BodyPropertyKind.Scalar,
                _ => BodyPropertyKind.Unknown
            };
        }

        if (schema.TryGetProperty("enum", out _))
        {
            return BodyPropertyKind.Scalar;
        }

        return BodyPropertyKind.Unknown;
    }

    private static string BuildPathExpression(string path, List<ParameterInfo> pathParams)
    {
        if (pathParams.Count == 0) return "\"" + path + "\"";

        var nameToIdentifier = pathParams.ToDictionary(p => p.Name, p => p.Identifier, StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        sb.Append("$\"");
        var i = 0;
        while (i < path.Length)
        {
            var ch = path[i];
            if (ch == '{')
            {
                var end = path.IndexOf('}', i + 1);
                if (end == -1) break;
                var name = path.Substring(i + 1, end - i - 1);
                var identifier = nameToIdentifier.TryGetValue(name, out var mapped) ? mapped : SanitizeIdentifier(name);
                sb.Append("{Escape(").Append(identifier).Append(")}");
                i = end + 1;
                continue;
            }
            if (ch == '"') sb.Append("\\\"");
            else sb.Append(ch);
            i++;
        }
        sb.Append("\"");
        return sb.ToString();
    }

    private static List<ParameterInfo> CollectParameters(JsonElement pathItem, JsonElement op)
    {
        var list = new List<ParameterInfo>();
        AddParameters(list, pathItem);
        AddParameters(list, op);
        return list;
    }

    private static void AddParameters(List<ParameterInfo> list, JsonElement element)
    {
        if (!element.TryGetProperty("parameters", out var parameters) || parameters.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var param in parameters.EnumerateArray())
        {
            if (!param.TryGetProperty("name", out var nameProp)) continue;
            if (!param.TryGetProperty("in", out var inProp)) continue;

            var name = nameProp.GetString();
            var location = inProp.GetString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(location)) continue;

            var required = param.TryGetProperty("required", out var requiredProp) && requiredProp.ValueKind == JsonValueKind.True;
            var isArray = false;
            if (param.TryGetProperty("schema", out var schema) && schema.TryGetProperty("type", out var typeProp))
            {
                isArray = string.Equals(typeProp.GetString(), "array", StringComparison.OrdinalIgnoreCase);
            }

            var identifier = SanitizeIdentifier(name!);
            var propertyName = AvoidBaseSettingsConflict(SanitizeIdentifier(ToPascalCase(name!)));
            var optionName = ToKebabCase(name!);
            if (string.Equals(location, "query", StringComparison.OrdinalIgnoreCase))
            {
                optionName = AvoidBaseOptionConflict(optionName);
            }
            list.Add(new ParameterInfo(name!, location!, identifier, propertyName, required, isArray, optionName));
        }
    }

    private static void EnsurePathParameters(string path, List<ParameterInfo> list)
    {
        var existing = new HashSet<string>(
            list.Where(p => p.Location == "path").Select(p => p.Name),
            StringComparer.OrdinalIgnoreCase);

        foreach (var name in GetPathParamNames(path))
        {
            if (existing.Contains(name)) continue;
            var identifier = SanitizeIdentifier(name);
            var propertyName = AvoidBaseSettingsConflict(SanitizeIdentifier(ToPascalCase(name)));
            var optionName = ToKebabCase(name);
            list.Add(new ParameterInfo(name, "path", identifier, propertyName, Required: true, IsArray: false, optionName));
        }
    }

    private static TypeInfo ResolveType(JsonElement schema, bool isRequired)
    {
        if (schema.ValueKind == JsonValueKind.Null)
        {
            return new TypeInfo("object?", false, false);
        }

        if (schema.TryGetProperty("$ref", out var refProp))
        {
            var name = ExtractRefName(refProp.GetString() ?? string.Empty);
            var typeName = SanitizeIdentifier(name);
            var nullable = !isRequired;
            return new TypeInfo(typeName + (nullable ? "?" : string.Empty), false, !nullable);
        }

        if (schema.TryGetProperty("enum", out _))
        {
            return new TypeInfo(isRequired ? "string" : "string?", false, isRequired);
        }

        if (schema.TryGetProperty("type", out var typeProp))
        {
            var type = typeProp.GetString();
            var nullable = schema.TryGetProperty("nullable", out var nullableProp) && nullableProp.ValueKind == JsonValueKind.True;
            var allowNull = !isRequired || nullable;

            switch (type)
            {
                case "string":
                    return new TypeInfo(ResolveStringType(schema, allowNull), false, !allowNull);
                case "integer":
                    return new TypeInfo(ResolveNumberType(schema, allowNull, integer: true), true, false);
                case "number":
                    return new TypeInfo(ResolveNumberType(schema, allowNull, integer: false), true, false);
                case "boolean":
                    return new TypeInfo(allowNull ? "bool?" : "bool", true, false);
                case "array":
                    if (schema.TryGetProperty("items", out var items))
                    {
                        var itemType = ResolveType(items, true).TypeName.TrimEnd('?');
                        return new TypeInfo("List<" + itemType + ">" + (allowNull ? "?" : string.Empty), false, !allowNull);
                    }
                    return new TypeInfo(allowNull ? "List<object?>?" : "List<object?>", false, !allowNull);
                case "object":
                    if (schema.TryGetProperty("additionalProperties", out _))
                    {
                        return new TypeInfo(allowNull ? "Dictionary<string, object?>?" : "Dictionary<string, object?>", false, !allowNull);
                    }
                    return new TypeInfo(allowNull ? "object?" : "object", false, !allowNull);
            }
        }

        return new TypeInfo(isRequired ? "object" : "object?", false, isRequired);
    }

    private static string ResolveStringType(JsonElement schema, bool allowNull)
    {
        if (schema.TryGetProperty("format", out var formatProp))
        {
            var format = formatProp.GetString();
            switch (format)
            {
                case "date-time":
                    return allowNull ? "DateTimeOffset?" : "DateTimeOffset";
                case "date":
                    return allowNull ? "DateOnly?" : "DateOnly";
                case "uuid":
                    return allowNull ? "Guid?" : "Guid";
                case "byte":
                case "binary":
                    return allowNull ? "byte[]?" : "byte[]";
            }
        }

        return allowNull ? "string?" : "string";
    }

    private static string ResolveNumberType(JsonElement schema, bool allowNull, bool integer)
    {
        if (schema.TryGetProperty("format", out var formatProp))
        {
            var format = formatProp.GetString();
            if (integer)
            {
                return format switch
                {
                    "int64" => allowNull ? "long?" : "long",
                    "int32" => allowNull ? "int?" : "int",
                    "int16" => allowNull ? "short?" : "short",
                    _ => allowNull ? "int?" : "int"
                };
            }

            return format switch
            {
                "float" => allowNull ? "float?" : "float",
                "double" => allowNull ? "double?" : "double",
                "decimal" => allowNull ? "decimal?" : "decimal",
                _ => allowNull ? "double?" : "double"
            };
        }

        return integer ? (allowNull ? "int?" : "int") : (allowNull ? "double?" : "double");
    }

    private static string ExtractRefName(string raw)
    {
        var idx = raw.LastIndexOf('/');
        return idx >= 0 ? raw.Substring(idx + 1) : raw;
    }

    private static string SanitizeIdentifier(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "_";
        var sb = new StringBuilder();
        var first = true;
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
            {
                if (first && char.IsDigit(ch)) sb.Append('_');
                sb.Append(ch);
                first = false;
                continue;
            }
        }

        var id = sb.Length == 0 ? "_" : sb.ToString();
        return IsKeyword(id) ? "@" + id : id;
    }

    private static string ToPascalCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        var parts = SplitWords(input);
        if (parts.Count == 0) return input;
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            if (part.Length == 0) continue;
            sb.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1) sb.Append(part.Substring(1));
        }
        return sb.ToString();
    }

    // Known word segments for splitting all-lowercase compound words
    // Order matters: longer/more specific words first, plurals before singulars
    private static readonly string[] KnownWordSegments = new[]
    {
        // Compound nouns that should stay together
        "subtasks", "subtask", "contactinfo",
        // Plurals and conjugations before base forms
        "assignees", "statuses", "types", "lists", "tags", "members", "templates",
        "projects", "tasks", "users", "teams", "companies", "archived", "recurrency",
        // Verbs
        "change", "set", "get", "list", "create", "update", "delete", "remove", "add",
        "assign", "unassign", "activate", "deactivate", "archive", "unarchive",
        "start", "stop", "enable", "disable", "accept", "reject", "approve", "deny",
        // Nouns (singulars)
        "project", "task", "user", "team", "company", "contact", "status", "type",
        "base", "work", "info", "tag", "parent", "order", "member", "template",
        // Short words (careful - can cause unwanted splits)
        "top", "to"
    };

    private static string ToKebabCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        var parts = SplitWords(input);
        if (parts.Count == 0) return input.ToLowerInvariant();

        // Post-process: split all-lowercase words by known segments
        var expanded = new List<string>();
        foreach (var part in parts)
        {
            expanded.AddRange(SplitByKnownSegments(part));
        }

        return string.Join("-", expanded).ToLowerInvariant();
    }

    private static List<string> SplitByKnownSegments(string word)
    {
        // Only split if the word is all lowercase (no camelCase boundaries were found)
        if (word.Any(char.IsUpper))
        {
            return new List<string> { word };
        }

        var lower = word.ToLowerInvariant();
        if (lower == "workspace" || lower == "workspaces")
        {
            return new List<string> { lower };
        }

        var result = new List<string>();
        var remaining = lower;

        while (remaining.Length > 0)
        {
            var matched = false;
            foreach (var segment in KnownWordSegments)
            {
                if (remaining.StartsWith(segment, StringComparison.Ordinal))
                {
                    result.Add(segment);
                    remaining = remaining.Substring(segment.Length);
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                // No known segment found, keep the rest as-is
                if (remaining.Length > 0)
                {
                    result.Add(remaining);
                }
                break;
            }
        }

        return result;
    }

    private static List<string> SplitWords(string input)
    {
        input = input.Replace("WorkSpace", "Workspace");
        var result = new List<string>();
        var current = new StringBuilder();
        foreach (var ch in input)
        {
            if (!char.IsLetterOrDigit(ch))
            {
                if (current.Length > 0)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            if (current.Length == 0)
            {
                current.Append(ch);
                continue;
            }

            if (char.IsUpper(ch) && char.IsLower(current[current.Length - 1]))
            {
                result.Add(current.ToString());
                current.Clear();
                current.Append(ch);
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    private static string PathToName(string path)
    {
        var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var segment in segments)
        {
            if (segment.StartsWith("{", StringComparison.Ordinal) && segment.EndsWith("}", StringComparison.Ordinal))
            {
                var param = segment.Substring(1, segment.Length - 2);
                sb.Append("By").Append(ToPascalCase(param));
            }
            else
            {
                sb.Append(ToPascalCase(segment));
            }
        }
        return sb.ToString();
    }

    private static bool IsGenericOperationId(string opId) =>
        opId is "Get" or "Post" or "Put" or "Delete" or "Patch";

    private static bool IsKeyword(string name)
    {
        return name is "class" or "namespace" or "public" or "private" or "internal" or "protected" or
            "string" or "int" or "double" or "long" or "short" or "float" or "decimal" or "bool" or "object" or
            "return" or "event" or "new" or "default" or "base" or "params" or "out" or "ref" or "in" or
            "void" or "partial" or "record" or "interface" or "struct" or "enum" or "using" or "static";
    }

    private static string AvoidBaseSettingsConflict(string propertyName)
    {
        if (!BaseSettingsPropertyNames.Contains(propertyName)) return propertyName;
        return propertyName + "Param";
    }

    private static string AvoidBaseOptionConflict(string optionName)
    {
        if (!BaseOptionNames.Contains(optionName)) return optionName;
        return "param-" + optionName;
    }

    private static string ResolveDomain(string tag)
    {
        if (TagDomainMap.TryGetValue(tag, out var domain)) return domain;
        return "misc";
    }

    private static TagGroupInfo ResolveTagGroupInfo(string tag)
    {
        var domain = ResolveDomain(tag);
        var subTag = ResolveSubTag(tag, domain);
        return new TagGroupInfo(tag, domain, subTag);
    }

    private static string? ResolveSubTag(string tag, string domain)
    {
        if (RootTags.Contains(tag)) return null;
        if (TagSubOverrides.TryGetValue(tag, out var overrideValue)) return overrideValue;

        var trimmed = tag;
        if (DomainPrefixes.TryGetValue(domain, out var prefix))
        {
            trimmed = TrimPrefix(trimmed, prefix);
        }

        if (domain.Equals("tasks", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith("Tasks", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 5);
        }

        if (domain.Equals("files", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith("Files", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 5);
        }

        if (domain.Equals("users", StringComparison.OrdinalIgnoreCase) && trimmed.EndsWith("Users", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 5);
        }

        trimmed = trimmed.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return null;
        return ToKebabCase(trimmed);
    }

    private static string TrimPrefix(string value, string prefix)
    {
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return value;
        var trimmed = value.Substring(prefix.Length);
        return trimmed.StartsWith(" ", StringComparison.Ordinal) ? trimmed.Substring(1) : trimmed;
    }

    private static string GetDomainDescription(string domain)
    {
        return DomainDescriptions.TryGetValue(domain, out var desc) ? desc : domain;
    }

    private static int DomainSortKey(string domain)
    {
        for (var i = 0; i < DomainOrder.Length; i++)
        {
            if (string.Equals(DomainOrder[i], domain, StringComparison.OrdinalIgnoreCase)) return i;
        }

        return int.MaxValue;
    }

    private sealed record TagGroupInfo(string Tag, string Domain, string? SubTag);

    private sealed record ParameterInfo(
        string Name,
        string Location,
        string Identifier,
        string PropertyName,
        bool Required,
        bool IsArray,
        string OptionName
    );

    private sealed record TypeInfo(string TypeName, bool IsValueType, bool NeedsDefault);
}
