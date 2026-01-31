using System.Net;
using System.Text;

namespace Awk.Services;

internal sealed record OAuthCallbackResult(string Code, string State);

internal sealed class OAuthRedirectServer : IAsyncDisposable
{
    private readonly HttpListener _listener;
    private readonly string _callbackPath;

    internal OAuthRedirectServer(Uri redirectUri)
    {
        if (redirectUri.Scheme != Uri.UriSchemeHttp)
        {
            throw new InvalidOperationException("Redirect URI must use http scheme.");
        }

        _callbackPath = redirectUri.AbsolutePath;
        var prefix = $"{redirectUri.Scheme}://{redirectUri.Host}:{redirectUri.Port}{redirectUri.AbsolutePath}";
        if (!prefix.EndsWith('/'))
        {
            prefix += "/";
        }

        _listener = new HttpListener();
        _listener.Prefixes.Add(prefix);
    }

    internal void Start()
    {
        _listener.Start();
    }

    internal async Task<OAuthCallbackResult> WaitForCallback(string expectedState, CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                _listener.Stop();
            }
            catch
            {
                // ignore
            }
        });

        try
        {
            var context = await _listener.GetContextAsync();
            var request = context.Request;

            var code = request.QueryString["code"];
            var state = request.QueryString["state"];
            var error = request.QueryString["error"];

            await WriteResponse(context.Response, code, error);

            if (!string.IsNullOrWhiteSpace(error))
            {
                throw new InvalidOperationException($"OAuth error: {error}");
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                throw new InvalidOperationException("OAuth callback missing code.");
            }

            if (!string.Equals(state, expectedState, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("OAuth callback state mismatch.");
            }

            return new OAuthCallbackResult(code, state ?? string.Empty);
        }
        catch (HttpListenerException ex) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("OAuth callback canceled.", ex, cancellationToken);
        }
    }

    private async Task WriteResponse(HttpListenerResponse response, string? code, string? error)
    {
        var message = string.IsNullOrWhiteSpace(error)
            ? "Login complete. You can close this window."
            : $"Login failed: {error}";

        var payload = $"<html><body><h3>{WebUtility.HtmlEncode(message)}</h3></body></html>";
        var buffer = Encoding.UTF8.GetBytes(payload);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        response.OutputStream.Close();
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            _listener.Close();
        }
        catch
        {
            // ignore
        }
        return ValueTask.CompletedTask;
    }
}
