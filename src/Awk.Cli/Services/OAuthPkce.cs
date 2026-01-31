using System.Security.Cryptography;
using System.Text;

namespace Awk.Services;

internal sealed record OAuthPkcePair(string Verifier, string Challenge);

internal static class OAuthPkce
{
    internal static OAuthPkcePair Generate()
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return new OAuthPkcePair(verifier, challenge);
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
