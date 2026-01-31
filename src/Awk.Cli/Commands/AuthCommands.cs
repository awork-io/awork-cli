using System.Diagnostics;
using Awk.Cli;
using Awk.Config;
using Awk.Models;
using Awk.Services;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Awk.Commands;

internal sealed class AuthLoginSettings : BaseSettings
{
    [CommandOption("--token-stdin")]
    public bool TokenStdin { get; init; }

    [CommandOption("--redirect-uri <URI>")]
    public string? RedirectUri { get; init; }

    [CommandOption("--scopes <SCOPES>")]
    public string? Scopes { get; init; }

    [CommandOption("--client-name <NAME>")]
    public string? ClientName { get; init; }

    [CommandOption("--no-open")]
    public bool NoOpen { get; init; }

    public override ValidationResult Validate()
    {
        var baseResult = base.Validate();
        if (!baseResult.Successful) return baseResult;

        if (TokenStdin && !string.IsNullOrWhiteSpace(Token))
        {
            return ValidationResult.Error("use --token or --token-stdin, not both");
        }

        return ValidationResult.Success();
    }
}

internal sealed class AuthLoginCommand : CommandBase<AuthLoginSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, AuthLoginSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await ConfigLoader.Load(
                settings.EnvFile,
                settings.BaseUrl,
                null,
                settings.ConfigPath,
                cancellationToken);

            var baseConfig = loaded.BaseConfig;
            var effective = loaded.EffectiveConfig;

            if (settings.TokenStdin || !string.IsNullOrWhiteSpace(settings.Token))
            {
                var apiToken = await ReadToken(settings, cancellationToken);
                if (string.IsNullOrWhiteSpace(apiToken))
                {
                    throw new InvalidOperationException("API token missing.");
                }

                var updated = baseConfig with
                {
                    ApiBaseUrl = effective.ApiBaseUrl,
                    ApiToken = apiToken,
                    OAuth = (baseConfig.OAuth ?? OAuthConfig.Default) with { Token = null }
                };

                await ConfigLoader.SaveUserConfig(updated, loaded.ConfigPath, cancellationToken);
                return Output(ResponseEnvelope.Ok(0, null, new { status = "token-saved", baseUrl = updated.ApiBaseUrl }));
            }

            var redirectUri = FirstNonEmpty(settings.RedirectUri, effective.OAuth?.RedirectUri, AworkOAuthDefaults.RedirectUri);
            var scopes = FirstNonEmpty(settings.Scopes, effective.OAuth?.Scopes, AworkOAuthDefaults.Scopes);
            var clientName = string.IsNullOrWhiteSpace(settings.ClientName) ? "awork-cli" : settings.ClientName.Trim();
            var clientId = effective.OAuth?.RegisteredClientId;

            if (string.IsNullOrWhiteSpace(clientId))
            {
                var version = typeof(AuthLoginCommand).Assembly.GetName().Version?.ToString();
                var registration = await AworkOAuthService.RegisterClient(
                    effective.ApiBaseUrl,
                    Guid.NewGuid().ToString("N"),
                    clientName,
                    version,
                    redirectUri,
                    scopes,
                    cancellationToken);
                clientId = registration.ClientId;
            }

            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new InvalidOperationException("OAuth client id missing.");
            }

            if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var redirectUriParsed))
            {
                throw new InvalidOperationException("OAuth redirect URI invalid.");
            }

            if (redirectUriParsed.Scheme != Uri.UriSchemeHttp ||
                (redirectUriParsed.Host != "localhost" && redirectUriParsed.Host != "127.0.0.1"))
            {
                throw new InvalidOperationException("OAuth redirect URI must be http://localhost or http://127.0.0.1.");
            }

            var authRequest = OAuthAuthorizationRequestFactory.CreateAwork(
                clientId,
                redirectUri,
                scopes,
                AworkOAuthDefaults.AuthorizationEndpoint(effective.ApiBaseUrl));

            if (!settings.NoOpen && TryOpenBrowser(authRequest.Url))
            {
                Console.Error.WriteLine("auth: opened browser for login.");
            }
            else
            {
                Console.Error.WriteLine($"auth: open this URL to continue: {authRequest.Url}");
            }

            await using var server = new OAuthRedirectServer(redirectUriParsed);
            server.Start();

            var callback = await server.WaitForCallback(authRequest.State, cancellationToken);
            var oauthToken = await AworkOAuthService.ExchangeCode(
                effective.ApiBaseUrl,
                clientId,
                callback.Code,
                authRequest.CodeVerifier,
                redirectUri,
                cancellationToken);

            var updatedConfig = baseConfig with
            {
                ApiBaseUrl = effective.ApiBaseUrl,
                ApiToken = null,
                OAuth = new OAuthConfig(clientId, redirectUri, scopes, oauthToken)
            };

            await ConfigLoader.SaveUserConfig(updatedConfig, loaded.ConfigPath, cancellationToken);
            return Output(ResponseEnvelope.Ok(0, null, new
            {
                status = "oauth-saved",
                baseUrl = updatedConfig.ApiBaseUrl,
                clientId,
                oauthToken.ExpiresAt
            }));
        }
        catch (Exception ex)
        {
            return OutputError(ex);
        }
    }

    private static async Task<string?> ReadToken(AuthLoginSettings settings, CancellationToken cancellationToken)
    {
        if (!settings.TokenStdin) return settings.Token?.Trim();

        using var reader = new StreamReader(Console.OpenStandardInput());
        var token = await reader.ReadToEndAsync(cancellationToken);
        return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
    }

    private static bool TryOpenBrowser(Uri url)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = url.ToString(),
                UseShellExecute = true
            };
            Process.Start(info);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }

        return string.Empty;
    }
}

internal sealed class AuthStatusCommand : CommandBase<BaseSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, BaseSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await ConfigLoader.Load(
                settings.EnvFile,
                settings.BaseUrl,
                settings.Token,
                settings.ConfigPath,
                cancellationToken);

            var config = loaded.EffectiveConfig;
            var oauth = config.OAuth?.Token;

            var response = new
            {
                baseUrl = config.ApiBaseUrl,
                configPath = loaded.ConfigPath,
                apiToken = Mask(config.ApiToken),
                oauth = new
                {
                    clientId = config.OAuth?.RegisteredClientId,
                    redirectUri = config.OAuth?.RedirectUri,
                    scopes = config.OAuth?.Scopes,
                    accessToken = Mask(oauth?.AccessToken),
                    expiresAt = oauth?.ExpiresAt
                }
            };

            return Output(ResponseEnvelope.Ok(0, null, response));
        }
        catch (Exception ex)
        {
            return OutputError(ex);
        }
    }

    private static string? Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length <= 6) return "***";
        return $"{trimmed[..2]}***{trimmed[^2..]}";
    }
}

internal sealed class AuthLogoutSettings : BaseSettings
{
    [CommandOption("--clear-token")]
    public bool ClearToken { get; init; }

    [CommandOption("--clear-oauth")]
    public bool ClearOAuth { get; init; }
}

internal sealed class AuthLogoutCommand : CommandBase<AuthLogoutSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, AuthLogoutSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await ConfigLoader.Load(
                settings.EnvFile,
                settings.BaseUrl,
                null,
                settings.ConfigPath,
                cancellationToken);

            var clearToken = settings.ClearToken || (!settings.ClearToken && !settings.ClearOAuth);
            var clearOAuth = settings.ClearOAuth || (!settings.ClearToken && !settings.ClearOAuth);

            var updated = loaded.BaseConfig with
            {
                ApiToken = clearToken ? null : loaded.BaseConfig.ApiToken,
                OAuth = clearOAuth
                    ? (loaded.BaseConfig.OAuth ?? OAuthConfig.Default) with { Token = null }
                    : loaded.BaseConfig.OAuth
            };

            await ConfigLoader.SaveUserConfig(updated, loaded.ConfigPath, cancellationToken);
            return Output(ResponseEnvelope.Ok(0, null, new { status = "cleared", token = clearToken, oauth = clearOAuth }));
        }
        catch (Exception ex)
        {
            return OutputError(ex);
        }
    }
}
