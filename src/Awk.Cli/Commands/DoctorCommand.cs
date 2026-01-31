using Awk.Cli;
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
            return Output(result);
        }
        catch (Exception ex)
        {
            return OutputError(ex);
        }
    }
}
