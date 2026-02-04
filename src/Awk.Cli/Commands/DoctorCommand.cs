using System.Text.Json;
using Awk.Cli;
using Awk.Generated;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Awk.Commands;

internal sealed class DoctorCommand : CommandBase<BaseSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, BaseSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var client = await CreateClient(settings, cancellationToken);
            var result = await client.GetMe(cancellationToken: cancellationToken);

            if (result.StatusCode != 200 || result.Response is not JsonElement json)
            {
                AnsiConsole.MarkupLine("[red]✗[/] Authentication failed");
                AnsiConsole.MarkupLine("  Run [bold]awork auth login[/] or provide a valid [bold]--token[/]");
                return 1;
            }

            var user = json.Deserialize<UserAndWorkspace>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var name = $"{user?.FirstName} {user?.LastName}".Trim();
            var email = user?.UserContactInfos?
                .FirstOrDefault(c => c.Type?.Equals("email", StringComparison.OrdinalIgnoreCase) == true)
                ?.Value ?? "—";
            var workspace = user?.Workspace?.Name ?? "—";

            AnsiConsole.MarkupLine($"[green]✓[/] Logged in as [bold]{name}[/] ({email})");
            AnsiConsole.MarkupLine($"  Workspace: [bold]{workspace}[/]");
            return 0;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("token", StringComparison.OrdinalIgnoreCase))
        {
            AnsiConsole.MarkupLine("[red]✗[/] Not logged in");
            AnsiConsole.MarkupLine("  Run [bold]awork auth login[/] or provide [bold]--token[/]");
            return 1;
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {ex.Message}");
            return 1;
        }
    }
}
