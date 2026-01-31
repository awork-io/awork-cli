namespace Awk.Config;

internal sealed record OAuthConfig(
    string? RegisteredClientId,
    string? RedirectUri,
    string? Scopes,
    OAuthToken? Token)
{
    internal static OAuthConfig Default => new(
        RegisteredClientId: null,
        RedirectUri: null,
        Scopes: null,
        Token: null);
}

internal sealed record OAuthToken(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset? ExpiresAt,
    string TokenType);
