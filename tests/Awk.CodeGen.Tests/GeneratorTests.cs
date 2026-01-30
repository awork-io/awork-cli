using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Awk.CodeGen;

namespace Awk.CodeGen.Tests;

public sealed class GeneratorTests
{
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
    public void CliCommandNames_HaveExpectedSamples()
    {
        var cli = GeneratedSources.Value.Cli;
        Assert.Contains("branch.AddCommand<GetUsers>(\"list\")", cli);
        Assert.Contains("branch.AddCommand<GetMe>(\"me\")", cli);
        Assert.Contains("branch.AddCommand<PostProjectDeleteTagsByProjectId>(\"delete-project-tags\")", cli);
        Assert.Contains("branch.AddCommand<PostUsersDeleteTags>(\"delete-tags\")", cli);
        Assert.Contains("branch.AddCommand<PostTasksChangeBaseTypes>(\"change-base-types\")", cli);
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
        return new GeneratedSourceSet(cli, client);
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

    private sealed record GeneratedSourceSet(string Cli, string Client);
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
