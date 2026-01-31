namespace Awk.Services;

internal sealed record OAuthAuthorizationRequest(Uri Url, string State, string CodeVerifier);

internal static class OAuthAuthorizationRequestFactory
{
    internal static OAuthAuthorizationRequest CreateAwork(string clientId, string redirectUri, string scopes, Uri authorizationEndpoint)
    {
        var pkce = OAuthPkce.Generate();
        var state = Guid.NewGuid().ToString("N");

        var query = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = scopes,
            ["code_challenge_method"] = "S256",
            ["code_challenge"] = pkce.Challenge,
            ["state"] = state
        };

        var url = BuildUrl(authorizationEndpoint, query);
        return new OAuthAuthorizationRequest(url, state, pkce.Verifier);
    }

    private static Uri BuildUrl(Uri baseUri, Dictionary<string, string?> query)
    {
        var parts = new List<string>();
        foreach (var (key, value) in query)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            parts.Add(Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value));
        }

        var separator = baseUri.Query.Length == 0 ? "?" : "&";
        var combined = baseUri + separator + string.Join("&", parts);
        return new Uri(combined);
    }
}
