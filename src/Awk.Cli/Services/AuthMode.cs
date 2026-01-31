namespace Awk.Services;

internal enum AuthMode
{
    Auto,
    Token,
    OAuth
}

internal static class AuthModeParser
{
    internal static AuthMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            value = Environment.GetEnvironmentVariable("AWK_AUTH_MODE")
                ?? Environment.GetEnvironmentVariable("AWORK_AUTH_MODE");
        }

        if (string.IsNullOrWhiteSpace(value)) return AuthMode.Auto;

        return value.Trim().ToLowerInvariant() switch
        {
            "auto" => AuthMode.Auto,
            "token" => AuthMode.Token,
            "oauth" => AuthMode.OAuth,
            _ => throw new InvalidOperationException("auth-mode must be auto|token|oauth")
        };
    }

    internal static bool IsValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return true;
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "auto" or "token" or "oauth";
    }
}
