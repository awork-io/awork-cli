using System.Text.Json;

namespace Awk.Config;

internal sealed record ConfigLoadResult(
    AppConfig BaseConfig,
    AppConfig EffectiveConfig,
    string ConfigPath);

internal static class ConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    internal static async Task<ConfigLoadResult> Load(
        string? envFileOverride,
        string? baseUrlOverride,
        string? tokenOverride,
        string? configPathOverride,
        CancellationToken cancellationToken)
    {
        var envFile = string.IsNullOrWhiteSpace(envFileOverride) ? ".env" : envFileOverride.Trim();
        var configPath = ResolveConfigPath(configPathOverride);
        var baseConfig = await LoadFromFile(configPath, cancellationToken) ?? AppConfig.Default(envFile);
        baseConfig = Normalize(baseConfig, envFile);

        var env = EnvFile.Load(envFile);

        var baseUrl = FirstNonEmpty(
            baseUrlOverride,
            Env("AWORK_BASE_URL"),
            Env("AWK_BASE_URL"),
            env.GetValueOrDefault("AWORK_BASE_URL"),
            env.GetValueOrDefault("AWK_BASE_URL"),
            baseConfig.ApiBaseUrl
        ) ?? AppConfig.DefaultBaseUrl;

        var token = FirstNonEmpty(
            tokenOverride,
            Env("AWORK_TOKEN"),
            Env("AWK_TOKEN"),
            Env("AWORK_BEARER_TOKEN"),
            Env("BEARER_TOKEN"),
            env.GetValueOrDefault("AWORK_TOKEN"),
            env.GetValueOrDefault("AWK_TOKEN"),
            env.GetValueOrDefault("AWORK_BEARER_TOKEN"),
            env.GetValueOrDefault("BEARER_TOKEN"),
            baseConfig.ApiToken
        );

        var oauthClientId = FirstNonEmpty(
            Env("AWORK_OAUTH_CLIENT_ID"),
            Env("AWK_OAUTH_CLIENT_ID"),
            env.GetValueOrDefault("AWORK_OAUTH_CLIENT_ID"),
            env.GetValueOrDefault("AWK_OAUTH_CLIENT_ID"),
            baseConfig.OAuth?.RegisteredClientId
        );

        var oauthRedirect = FirstNonEmpty(
            Env("AWORK_OAUTH_REDIRECT_URI"),
            Env("AWK_OAUTH_REDIRECT_URI"),
            env.GetValueOrDefault("AWORK_OAUTH_REDIRECT_URI"),
            env.GetValueOrDefault("AWK_OAUTH_REDIRECT_URI"),
            baseConfig.OAuth?.RedirectUri
        );

        var oauthScopes = FirstNonEmpty(
            Env("AWORK_OAUTH_SCOPES"),
            Env("AWK_OAUTH_SCOPES"),
            env.GetValueOrDefault("AWORK_OAUTH_SCOPES"),
            env.GetValueOrDefault("AWK_OAUTH_SCOPES"),
            baseConfig.OAuth?.Scopes
        );

        var effective = baseConfig with
        {
            ApiBaseUrl = baseUrl,
            ApiToken = token,
            OAuth = (baseConfig.OAuth ?? OAuthConfig.Default) with
            {
                RegisteredClientId = oauthClientId,
                RedirectUri = oauthRedirect,
                Scopes = oauthScopes
            },
            EnvFile = envFile
        };

        return new ConfigLoadResult(baseConfig, effective, configPath);
    }

    internal static async Task<AppConfig?> LoadFromFile(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return null;
        var text = await File.ReadAllTextAsync(path, cancellationToken);
        if (string.IsNullOrWhiteSpace(text)) return null;
        var loaded = JsonSerializer.Deserialize<AppConfig>(text, JsonOptions);
        return loaded is null ? null : Normalize(loaded, ".env");
    }

    internal static async Task SaveUserConfig(AppConfig config, string configPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        var payload = JsonSerializer.Serialize(config, JsonOptions);
        await File.WriteAllTextAsync(configPath, payload, cancellationToken);

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            File.SetUnixFileMode(configPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    internal static string ResolveConfigPath(string? overridePath)
    {
        var env = Env("AWK_CONFIG") ?? Env("AWORK_CONFIG");
        var value = FirstNonEmpty(overridePath, env);
        return string.IsNullOrWhiteSpace(value) ? ConfigPaths.UserConfigFile : value.Trim();
    }

    private static AppConfig Normalize(AppConfig config, string envFile)
    {
        var baseUrl = string.IsNullOrWhiteSpace(config.ApiBaseUrl)
            ? AppConfig.DefaultBaseUrl
            : config.ApiBaseUrl.Trim();

        var oauth = config.OAuth ?? OAuthConfig.Default;
        return new AppConfig(baseUrl, config.ApiToken, oauth)
        {
            EnvFile = envFile
        };
    }

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name);

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }

        return null;
    }
}
