using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Awk.CodeGen;

namespace Awk.CodeGen.Tests;

public sealed class GeneratorTests
{
    // ========================================================================
    // REGRESSION TESTS - Known mistakes to catch early
    // ========================================================================

    [Fact]
    public void CliCommandNames_NoWordSplitMistakes()
    {
        // Catch word-splitting bugs where known words get broken up incorrectly
        var cli = GeneratedSources.Value.Cli;
        var names = ExtractCommandNames(cli).ToList();

        // These patterns indicate a word was incorrectly split
        // Pattern must match as a suffix or be followed by end/hyphen
        var knownBadPatterns = new[]
        {
            @"-type-s$",      // "types" split into "type" + "s"
            @"-list-s$",      // "lists" split into "list" + "s"
            @"-status-es$",   // "statuses" split incorrectly
            @"-assign-ees$",  // "assignees" split into "assign" + "ees"
            @"-archive-d$",   // "archived" split into "archive" + "d"
            @"-to-p$",        // "top" split into "to" + "p"
            @"-to-p-",        // "top" in middle of name
        };

        var regexes = knownBadPatterns.Select(p => new Regex(p)).ToList();
        var mistakes = names.Where(n => regexes.Any(r => r.IsMatch(n))).ToList();
        Assert.True(mistakes.Count == 0, $"Word split mistakes found: {string.Join(", ", mistakes.Take(10))}");
    }

    [Fact]
    public void CliCommandNames_NoMissingKebabSplits()
    {
        // Catch all-lowercase compound words that weren't split into kebab-case
        var cli = GeneratedSources.Value.Cli;
        var names = ExtractCommandNames(cli).ToList();

        var shouldNotExist = new[]
        {
            "changeprojecttype",   // should be change-project-type
            "changestatus",        // should be change-status
            "changebasetypes",     // should be change-base-types
            "setassignees",        // should be set-assignees
            "deletecontactinfo",   // should be delete-contact-info
            "listcontactinfo",     // should be list-contact-info
        };

        var mistakes = names.Where(n => shouldNotExist.Contains(n)).ToList();
        Assert.True(mistakes.Count == 0, $"Missing kebab splits: {string.Join(", ", mistakes)}");
    }


    [Fact]
    public void CliNames_WorkspaceNotSplit()
    {
        var cli = GeneratedSources.Value.Cli;
        Assert.DoesNotContain("work-space", cli);
        Assert.Contains("--workspace-id", cli);
    }

    [Fact]
    public void CliOptionNames_NoWordSplitMistakes()
    {
        // Catch option names with word-splitting bugs
        var cli = GeneratedSources.Value.Cli;

        var knownBadOptions = new[]
        {
            "--to-p",       // should be --top
            "--assign-ees", // should be --assignees
        };

        foreach (var bad in knownBadOptions)
        {
            Assert.DoesNotContain(bad, cli);
        }
    }

    // ========================================================================
    // COMMAND NAME TESTS
    // ========================================================================

    [Fact]
    public void CliCommandNames_AvoidUglyPatterns()
    {
        var cli = GeneratedSources.Value.Cli;
        var names = ExtractCommandNames(cli);
        var patterns = new[]
        {
            "list-get",
            "get-get",
            "create-create",
            "list-list",
            "users-users",
            "roles-roles",
            "teams-teams",
            "projects-projects",
            "projecttemplates-projecttemplates",
            "tasks-tasks",
            "companies-companies"
        };

        var bad = names.Where(name => patterns.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.True(bad.Count == 0, $"Bad command names: {string.Join(", ", bad)}");
    }

    [Fact]
    public void CliHierarchy_UsesDomains()
    {
        var cli = GeneratedSources.Value.Cli;
        Assert.Contains("config.AddBranch(\"users\"", cli);
        Assert.Contains("config.AddBranch(\"tasks\"", cli);
        Assert.Contains("config.AddBranch(\"projects\"", cli);
        Assert.Contains("config.AddBranch(\"times\"", cli);
        Assert.Contains("config.AddBranch(\"workspace\"", cli);
        Assert.Contains("config.AddBranch(\"documents\"", cli);
        Assert.Contains("config.AddBranch(\"files\"", cli);
        Assert.Contains("config.AddBranch(\"search\"", cli);
        Assert.Contains("config.AddBranch(\"integrations\"", cli);
        Assert.Contains("config.AddBranch(\"automation\"", cli);
    }

    [Fact]
    public void CliHierarchy_DomainOrderMatchesConfig()
    {
        var cli = GeneratedSources.Value.Cli;
        var domains = ExtractRegisterDomains(cli);
        var expected = GetDomainOrder()
            .Where(domains.Contains)
            .ToList();

        Assert.Equal(expected, domains);
    }

    [Fact]
    public void SwaggerTags_AllMappedToKnownDomains()
    {
        var tags = GetSwaggerTags();
        var allowed = new HashSet<string>(GetDomainOrder(), StringComparer.OrdinalIgnoreCase)
        {
            "auth"
        };

        var missing = new List<string>();
        var unknown = new List<string>();

        foreach (var tag in tags)
        {
            var domain = ResolveDomain(tag);
            if (string.Equals(domain, "misc", StringComparison.OrdinalIgnoreCase))
            {
                missing.Add(tag);
                continue;
            }

            if (!allowed.Contains(domain))
            {
                unknown.Add($"{tag}->{domain}");
            }
        }

        Assert.True(missing.Count == 0, $"Unmapped tags: {string.Join(", ", missing)}");
        Assert.True(unknown.Count == 0, $"Unexpected domains: {string.Join(", ", unknown)}");
    }

    [Fact]
    public void SwaggerTags_RootTagsHaveNoSubBranch()
    {
        var rootTags = GetRootTags();
        foreach (var tag in rootTags)
        {
            var info = ResolveTagInfo(tag);
            Assert.Null(info.SubTag);
        }
    }

    [Fact]
    public void SwaggerTags_NonRootTagsHaveSubBranch()
    {
        var tags = GetSwaggerTags();
        var rootTags = GetRootTags();

        foreach (var tag in tags)
        {
            if (rootTags.Contains(tag)) continue;
            var info = ResolveTagInfo(tag);
            Assert.False(string.IsNullOrWhiteSpace(info.SubTag), $"Missing sub-branch for {tag}");
        }
    }

    [Fact]
    public void SwaggerTags_OverridesHaveExpectedSubBranches()
    {
        var overrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ApiUsers"] = "api-users",
            ["ChecklistItems"] = "checklist-items",
            ["CompanyFiles"] = "company-files",
            ["CompanyTags"] = "company-tags",
            ["CommentFiles"] = "comment-files",
            ["FileUpload"] = "upload",
            ["AbsenceRegions"] = "absence-regions"
        };

        foreach (var pair in overrides)
        {
            var info = ResolveTagInfo(pair.Key);
            Assert.Equal(pair.Value, info.SubTag);
        }
    }

    [Fact]
    public void CliRegisterAuth_SeparatedFromMainRegister()
    {
        var cli = GeneratedSources.Value.Cli;
        var register = ExtractRegisterBody(cli);
        var auth = ExtractRegisterAuthBody(cli);

        Assert.DoesNotContain("Accounts", register);
        Assert.DoesNotContain("ClientApplications", register);
        Assert.True(auth.Contains("Accounts") || auth.Contains("ClientApplications"));
    }

    [Fact]
    public void CliSubBranches_ExistForSwaggerTags()
    {
        var cli = GeneratedSources.Value.Cli;
        var tags = GetSwaggerTags();
        var rootTags = GetRootTags();
        var subTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tag in tags)
        {
            if (rootTags.Contains(tag)) continue;
            var info = ResolveTagInfo(tag);
            if (!string.IsNullOrWhiteSpace(info.SubTag)) subTags.Add(info.SubTag!);
        }

        foreach (var subTag in subTags)
        {
            Assert.Contains($"AddBranch(\"{subTag}\"", cli);
        }
    }

    [Fact]
    public void CliCommandNames_HaveExpectedSamples()
    {
        var cli = GeneratedSources.Value.Cli;
        Assert.Contains("branch.AddCommand<GetUsers>(\"list\")", cli);
        Assert.Contains("branch.AddCommand<GetMe>(\"me\")", cli);
        Assert.Contains("branch.AddBranch(\"invitations\"", cli);
        Assert.Contains("branch.AddBranch(\"absence-regions\"", cli);
        Assert.Contains("branch.AddBranch(\"tags\"", cli);
    }

    [Fact]
    public void CliCommandNames_AreKebabCase()
    {
        var cli = GeneratedSources.Value.Cli;
        var names = ExtractCommandNames(cli).ToList();

        // All command names should be lowercase
        var withUppercase = names.Where(n => n.Any(char.IsUpper)).ToList();
        Assert.True(withUppercase.Count == 0, $"Commands with uppercase: {string.Join(", ", withUppercase.Take(10))}");

        // No double hyphens
        var withDoubleHyphens = names.Where(n => n.Contains("--")).ToList();
        Assert.True(withDoubleHyphens.Count == 0, $"Commands with double hyphens: {string.Join(", ", withDoubleHyphens.Take(10))}");

        // No leading/trailing hyphens
        var badHyphens = names.Where(n => n.StartsWith("-") || n.EndsWith("-")).ToList();
        Assert.True(badHyphens.Count == 0, $"Commands with leading/trailing hyphens: {string.Join(", ", badHyphens.Take(10))}");
    }

    [Fact]
    public void CliCommandNames_ConsistentKebabCase()
    {
        var cli = GeneratedSources.Value.Cli;
        var names = ExtractCommandNames(cli).ToList();

        // These swagger paths are all-lowercase, should still become kebab-case
        var expectedKebab = new[]
        {
            "change-project-type",   // was: changeprojecttype
            "change-status",         // was: changestatus
            "change-base-types",
            "change-lists",
            "change-statuses",
            "set-assignees",
            "set-recurrency",
            "delete-contact-info",
            "create-contact-info",
            "list-contact-info",
            "update-contact-info"
        };

        foreach (var expected in expectedKebab)
        {
            Assert.Contains(expected, names);
        }
    }

    [Fact]
    public void ClientMethodNames_NoAsyncSuffix()
    {
        var client = GeneratedSources.Value.Client;
        var asyncMethod = new Regex(@"public\s+Task<[^>]+>\s+[A-Za-z0-9_]+Async\s*\(", RegexOptions.Compiled);
        Assert.DoesNotMatch(asyncMethod, client);
    }

    [Fact]
    public void ClientMethodNames_AreUnique()
    {
        var methods = ExtractClientMethodNames(GeneratedSources.Value.Client).ToList();
        var duplicates = methods
            .GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(duplicates.Count == 0, $"Duplicate client methods: {string.Join(", ", duplicates)}");
    }

    [Fact]
    public void Dtos_ContainCommonTypes()
    {
        var dtos = GeneratedSources.Value.Dtos;
        Assert.Contains("JsonPropertyName", dtos);
        Assert.Contains("List<", dtos);
        Assert.Contains("Dictionary<string, object?>", dtos);
        Assert.Contains("DateTimeOffset", dtos);
        Assert.Contains("Guid", dtos);
    }


    [Fact]
    public void ClientMethodNames_AvoidUglyPatterns()
    {
        var methods = ExtractClientMethodNames(GeneratedSources.Value.Client).ToList();
        var patterns = new[]
        {
            "GetGet",
            "ListList",
            "CreateCreate",
            "UsersUsers",
            "TeamsTeams",
            "RolesRoles",
            "ProjectsProjects"
        };

        var bad = methods.Where(name => patterns.Any(p => name.Contains(p, StringComparison.Ordinal))).ToList();
        Assert.True(bad.Count == 0, $"Bad client method names: {string.Join(", ", bad)}");
    }

    private static IEnumerable<string> ExtractCommandNames(string cliSource)
    {
        var matches = Regex.Matches(cliSource, "AddCommand<[^>]+>\\(\\\"([a-z0-9\\-]+)\\\"\\)");
        foreach (Match match in matches)
        {
            yield return match.Groups[1].Value;
        }
    }

    private static IEnumerable<string> ExtractClientMethodNames(string clientSource)
    {
        var matches = Regex.Matches(clientSource, "public\\s+Task<[^>]+>\\s+([A-Za-z0-9_]+)\\s*\\(");
        foreach (Match match in matches)
        {
            yield return match.Groups[1].Value;
        }
    }

    private static IReadOnlyList<string> GetDomainOrder()
    {
        return GetPrivateStaticField<string[]>("DomainOrder");
    }

    private static HashSet<string> GetRootTags()
    {
        return GetPrivateStaticField<HashSet<string>>("RootTags");
    }

    private static string ResolveDomain(string tag)
    {
        var method = typeof(SwaggerClientGenerator).GetMethod("ResolveDomain", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (string)method!.Invoke(null, new object[] { tag })!;
    }

    private static (string Domain, string? SubTag) ResolveTagInfo(string tag)
    {
        var method = typeof(SwaggerClientGenerator).GetMethod("ResolveTagGroupInfo", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var info = method!.Invoke(null, new object[] { tag })!;
        var infoType = info.GetType();
        var domain = (string)infoType.GetProperty("Domain")!.GetValue(info)!;
        var subTag = (string?)infoType.GetProperty("SubTag")!.GetValue(info);
        return (domain, subTag);
    }

    private static T GetPrivateStaticField<T>(string name)
    {
        var field = typeof(SwaggerClientGenerator).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        return (T)field!.GetValue(null)!;
    }

    private static HashSet<string> GetSwaggerTags()
    {
        var swaggerPath = FindFileUpwards("swagger.json");
        var swaggerText = File.ReadAllText(swaggerPath);
        using var doc = JsonDocument.Parse(swaggerText);

        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!doc.RootElement.TryGetProperty("paths", out var paths)) return tags;

        foreach (var path in paths.EnumerateObject())
        {
            foreach (var op in path.Value.EnumerateObject())
            {
                if (!op.Value.TryGetProperty("tags", out var opTags) || opTags.ValueKind != JsonValueKind.Array) continue;
                foreach (var tag in opTags.EnumerateArray())
                {
                    if (tag.ValueKind == JsonValueKind.String)
                    {
                        var value = tag.GetString();
                        if (!string.IsNullOrWhiteSpace(value)) tags.Add(value);
                    }
                }
            }
        }

        return tags;
    }

    private static List<string> ExtractRegisterDomains(string cli)
    {
        var register = ExtractRegisterBody(cli);
        var matches = Regex.Matches(register, @"config.AddBranch\(""([^""]+)""");
        var domains = new List<string>();
        foreach (Match match in matches)
        {
            domains.Add(match.Groups[1].Value);
        }

        return domains;
    }

    private static string ExtractRegisterBody(string cli)
    {
        var start = cli.IndexOf("internal static void Register(IConfigurator config)", StringComparison.Ordinal);
        var end = cli.IndexOf("internal static void RegisterAuth", StringComparison.Ordinal);
        if (start < 0 || end < 0 || end <= start) return cli;
        return cli.Substring(start, end - start);
    }

    private static string ExtractRegisterAuthBody(string cli)
    {
        var start = cli.IndexOf("internal static void RegisterAuth", StringComparison.Ordinal);
        if (start < 0) return cli;
        return cli.Substring(start);
    }

    private static readonly Lazy<GeneratedSourceSet> GeneratedSources = new(GenerateSources);

    private static GeneratedSourceSet GenerateSources()
    {
        var swaggerPath = FindFileUpwards("swagger.json");
        var swaggerText = File.ReadAllText(swaggerPath);

        var additionalText = new InMemoryAdditionalText(swaggerPath, SourceText.From(swaggerText, Encoding.UTF8));
        var compilation = CSharpCompilation.Create(
            "Awk.CodeGen.Tests",
            new[] { CSharpSyntaxTree.ParseText("namespace Dummy { public sealed class Placeholder {} }") },
            GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new SwaggerClientGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: new ISourceGenerator[] { generator },
            additionalTexts: new[] { additionalText });

        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult();
        if (result.Results.Length == 0)
        {
            throw new InvalidOperationException("Source generator produced no results.");
        }

        var sources = result.Results[0].GeneratedSources;
        var cli = GetSource(sources, "AworkCli.g.cs");
        var client = GetSource(sources, "AworkClient.Operations.g.cs");
        var dtos = GetSource(sources, "AworkDtos.g.cs");
        return new GeneratedSourceSet(cli, client, dtos);
    }

    private static string GetSource(ImmutableArray<GeneratedSourceResult> sources, string hintName)
    {
        foreach (var source in sources)
        {
            if (string.Equals(source.HintName, hintName, StringComparison.OrdinalIgnoreCase))
            {
                return source.SourceText.ToString();
            }
        }

        throw new InvalidOperationException($"Generated source '{hintName}' not found.");
    }

    private static IEnumerable<MetadataReference> GetReferences()
    {
        var assemblies = new[]
        {
            typeof(object).Assembly,
            typeof(Enumerable).Assembly,
            typeof(GeneratorAttribute).Assembly,
            typeof(System.Runtime.GCSettings).Assembly
        };

        return assemblies
            .Select(a => a.Location)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
    }

    private static string FindFileUpwards(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException($"Could not find {fileName} above {AppContext.BaseDirectory}");
    }

    private sealed record GeneratedSourceSet(string Cli, string Client, string Dtos);
}

internal sealed class InMemoryAdditionalText : AdditionalText
{
    private readonly SourceText _text;

    public InMemoryAdditionalText(string path, SourceText text)
    {
        Path = path;
        _text = text;
    }

    public override string Path { get; }

    public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default) => _text;
}
