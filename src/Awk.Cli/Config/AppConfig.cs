using System.Text.Json.Serialization;

namespace Awk.Config;

internal sealed record AppConfig(
    string ApiBaseUrl,
    string? ApiToken,
    OAuthConfig? OAuth)
{
    internal const string DefaultBaseUrl = "https://api.awork.com/api/v1";

    [JsonIgnore]
    internal string EnvFile { get; init; } = ".env";

    internal static AppConfig Default(string envFile) => new(
        ApiBaseUrl: DefaultBaseUrl,
        ApiToken: null,
        OAuth: OAuthConfig.Default)
    {
        EnvFile = envFile
    };
}
