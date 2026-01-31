using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Awk.Config;

namespace Awk.Services;

internal static class AworkOAuthDefaults
{
    internal const string RedirectUri = "http://localhost:8400/oauth/callback";
    internal const string Scopes = "full_access offline_access";

    internal static Uri AuthorizationEndpoint(string baseUrl) => new($"{Normalize(baseUrl)}/accounts/authorize");

    internal static Uri TokenEndpoint(string baseUrl) => new($"{Normalize(baseUrl)}/accounts/token");

    internal static Uri RegistrationEndpoint(string baseUrl) => new($"{Normalize(baseUrl)}/clientapplications/register");

    private static string Normalize(string baseUrl) => baseUrl.TrimEnd('/');
}

internal sealed record AworkClientRegistration(string ClientId, string? ClientSecret);

internal sealed record AworkTokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string? RefreshToken,
    [property: JsonPropertyName("token_type")] string? TokenType,
    [property: JsonPropertyName("expires_in")] long? ExpiresIn,
    [property: JsonPropertyName("scope")] string? Scope);

internal static class AworkOAuthService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    internal static async Task<AworkClientRegistration> RegisterClient(
        string baseUrl,
        string softwareId,
        string clientName,
        string? softwareVersion,
        string redirectUri,
        string scopes,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["redirect_uris"] = new[] { redirectUri },
            ["client_name"] = clientName,
            ["token_endpoint_auth_method"] = "none",
            ["grant_types"] = new[] { "authorization_code", "refresh_token" },
            ["response_types"] = new[] { "code" },
            ["scope"] = scopes,
            ["application_type"] = "native",
            ["software_id"] = softwareId,
            ["software_version"] = softwareVersion,
        };

        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, AworkOAuthDefaults.RegistrationEndpoint(baseUrl))
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), System.Text.Encoding.UTF8, "application/json"),
        };

        var token = Environment.GetEnvironmentVariable("AWORK_DCR_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"awork DCR failed ({(int)response.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        if (!doc.RootElement.TryGetProperty("client_id", out var clientIdElement))
        {
            throw new InvalidOperationException("awork DCR response missing client_id.");
        }

        return new AworkClientRegistration(
            clientIdElement.GetString() ?? string.Empty,
            doc.RootElement.TryGetProperty("client_secret", out var secret) ? secret.GetString() : null);
    }

    internal static async Task<OAuthToken> ExchangeCode(
        string baseUrl,
        string clientId,
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        var payload = new Dictionary<string, string?>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, AworkOAuthDefaults.TokenEndpoint(baseUrl))
        {
            Content = new FormUrlEncodedContent(payload!),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"awork token exchange failed ({(int)response.StatusCode}): {body}");
        }

        var token = JsonSerializer.Deserialize<AworkTokenResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("awork token response invalid.");

        return ToOAuthToken(token);
    }

    internal static async Task<OAuthToken> Refresh(
        string baseUrl,
        string clientId,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        using var http = new HttpClient();
        var payload = new Dictionary<string, string?>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, AworkOAuthDefaults.TokenEndpoint(baseUrl))
        {
            Content = new FormUrlEncodedContent(payload!),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await http.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"awork token refresh failed ({(int)response.StatusCode}): {body}");
        }

        var token = JsonSerializer.Deserialize<AworkTokenResponse>(body, JsonOptions)
            ?? throw new InvalidOperationException("awork token response invalid.");

        return ToOAuthToken(token);
    }

    private static OAuthToken ToOAuthToken(AworkTokenResponse token)
    {
        var expiresAt = token.ExpiresIn.HasValue
            ? DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn.Value)
            : (DateTimeOffset?)null;

        return new OAuthToken(
            token.AccessToken,
            token.RefreshToken,
            expiresAt,
            token.TokenType ?? "Bearer");
    }
}
