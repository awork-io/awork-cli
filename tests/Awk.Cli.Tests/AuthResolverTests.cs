using Awk.Config;
using Awk.Services;

namespace Awk.Cli.Tests;

public sealed class AuthResolverTests
{
    [Fact]
    public async Task AutoMode_PrefersTokenOverOAuth()
    {
        var baseConfig = BuildConfig(apiToken: "api-token", oauthToken: BuildOAuth("oauth-token"));
        var result = await AuthResolver.Resolve(baseConfig, baseConfig, AuthMode.Auto, CancellationToken.None);

        Assert.Equal("api-token", result.Token);
        Assert.Equal("token", result.Source);
        Assert.Null(result.UpdatedConfig);
    }

    [Fact]
    public async Task TokenMode_RequiresToken()
    {
        var config = BuildConfig(apiToken: null, oauthToken: BuildOAuth("oauth-token"));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AuthResolver.Resolve(config, config, AuthMode.Token, CancellationToken.None));

        Assert.Contains("No API token", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OAuthMode_RequiresOAuth()
    {
        var config = BuildConfig(apiToken: "api-token", oauthToken: null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            AuthResolver.Resolve(config, config, AuthMode.OAuth, CancellationToken.None));

        Assert.Contains("OAuth token missing", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OAuthMode_UsesAccessTokenWhenValid()
    {
        var oauth = BuildOAuth("oauth-token");
        var config = BuildConfig(apiToken: null, oauthToken: oauth);

        var result = await AuthResolver.Resolve(config, config, AuthMode.OAuth, CancellationToken.None);
        Assert.Equal("oauth-token", result.Token);
        Assert.Equal("oauth", result.Source);
    }

    [Fact]
    public async Task OAuthMode_ExpiredWithoutRefresh_UsesExistingToken()
    {
        var oauth = new OAuthToken("oauth-token", null, DateTimeOffset.UtcNow.AddMinutes(-5), "Bearer");
        var config = BuildConfig(apiToken: null, oauthToken: oauth);

        var result = await AuthResolver.Resolve(config, config, AuthMode.OAuth, CancellationToken.None);
        Assert.Equal("oauth-token", result.Token);
        Assert.Null(result.UpdatedConfig);
    }

    private static AppConfig BuildConfig(string? apiToken, OAuthToken? oauthToken)
    {
        var baseConfig = AppConfig.Default(".env");
        return baseConfig with
        {
            ApiToken = apiToken,
            OAuth = new OAuthConfig("client-id", AworkOAuthDefaults.RedirectUri, AworkOAuthDefaults.Scopes, oauthToken)
        };
    }

    private static OAuthToken BuildOAuth(string accessToken)
    {
        return new OAuthToken(accessToken, "refresh", DateTimeOffset.UtcNow.AddHours(1), "Bearer");
    }
}
