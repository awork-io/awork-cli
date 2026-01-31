using Awk.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Awk.Commands;

internal class BaseSettings : CommandSettings
{
    [CommandOption("--env <PATH>")]
    public string? EnvFile { get; init; }

    [CommandOption("--base-url <URL>")]
    public string? BaseUrl { get; init; }

    [CommandOption("--token <TOKEN>")]
    public string? Token { get; init; }

    [CommandOption("--auth-mode <MODE>")]
    public string? AuthMode { get; init; }

    [CommandOption("--config <PATH>")]
    public string? ConfigPath { get; init; }

    public override ValidationResult Validate()
    {
        if (!AuthModeParser.IsValid(AuthMode))
        {
            return ValidationResult.Error("auth-mode must be auto|token|oauth");
        }

        return ValidationResult.Success();
    }
}
