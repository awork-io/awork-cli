using Awk.Config;

namespace Awk.Services;

internal sealed record AuthResolution(string Token, AppConfig? UpdatedConfig, string Source);

internal static class AuthResolver
{
    internal static async Task<AuthResolution> Resolve(
        AppConfig baseConfig,
        AppConfig effectiveConfig,
        AuthMode mode,
        CancellationToken cancellationToken)
    {
        var token = effectiveConfig.ApiToken;
        var oauth = effectiveConfig.OAuth?.Token;
        var hasToken = !string.IsNullOrWhiteSpace(token);
        var hasOAuth = oauth is not null && !string.IsNullOrWhiteSpace(oauth.AccessToken);

        if (mode == AuthMode.Token)
        {
            if (!hasToken)
            {
                throw new InvalidOperationException("No API token found. Provide --token or AWORK_TOKEN.");
            }

            return new AuthResolution(token!, null, "token");
        }

        if (mode == AuthMode.OAuth)
        {
            return await ResolveOAuth(baseConfig, effectiveConfig, cancellationToken);
        }

        if (hasToken)
        {
            if (hasOAuth)
            {
                Console.Error.WriteLine("auth: both API token and OAuth available; using API token (override with --auth-mode oauth).");
            }

            return new AuthResolution(token!, null, "token");
        }

        if (hasOAuth)
        {
            return await ResolveOAuth(baseConfig, effectiveConfig, cancellationToken);
        }

        throw new InvalidOperationException("No auth token found. Use --token/--auth-mode token or run `awork auth login`.");
    }

    private static async Task<AuthResolution> ResolveOAuth(
        AppConfig baseConfig,
        AppConfig effectiveConfig,
        CancellationToken cancellationToken)
    {
        var oauth = effectiveConfig.OAuth?.Token;
        if (oauth is null || string.IsNullOrWhiteSpace(oauth.AccessToken))
        {
            throw new InvalidOperationException("OAuth token missing. Run `awork auth login` or provide --token.");
        }

        if (oauth.ExpiresAt is null || oauth.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return new AuthResolution(oauth.AccessToken, null, "oauth");
        }

        if (string.IsNullOrWhiteSpace(oauth.RefreshToken) || string.IsNullOrWhiteSpace(effectiveConfig.OAuth?.RegisteredClientId))
        {
            return new AuthResolution(oauth.AccessToken, null, "oauth");
        }

        var refreshed = await AworkOAuthService.Refresh(
            effectiveConfig.OAuth.RegisteredClientId!,
            oauth.RefreshToken,
            cancellationToken);

        var updated = baseConfig with
        {
            OAuth = (baseConfig.OAuth ?? OAuthConfig.Default) with { Token = refreshed }
        };

        return new AuthResolution(refreshed.AccessToken, updated, "oauth");
    }
}
