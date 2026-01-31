using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Awk.Cli.Tests;

public sealed class CliIntegrationTests
{
    [Fact]
    public async Task DoctorCommand_OutputsEnvelopeAndTraceId()
    {
        using var server = new TestServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers["trace-id"] = "test-trace";
            await HttpListenerExtensions.RespondJsonAsync(ctx.Response, "{\"ok\":true}");
        });

        var result = await RunCliAsync(server.BaseUri, "doctor");
        Assert.Equal(0, result.ExitCode);

        var output = JsonDocument.Parse(result.StdOut);
        Assert.Equal(200, output.RootElement.GetProperty("statusCode").GetInt32());
        Assert.Equal("test-trace", output.RootElement.GetProperty("traceId").GetString());
        Assert.True(output.RootElement.GetProperty("response").GetProperty("ok").GetBoolean());

        var request = server.Requests.Single();
        Assert.Equal("GET", request.Method);
        Assert.Equal("/me", request.Path);
        Assert.Equal("Bearer test-token", request.Headers["Authorization"]);
    }

    [Fact]
    public async Task SearchCommand_SendsExpectedQueryParameters()
    {
        using var server = new TestServer(async ctx =>
        {
            ctx.Response.StatusCode = 200;
            await HttpListenerExtensions.RespondJsonAsync(ctx.Response, "{\"ok\":true}");
        });

        var result = await RunCliAsync(
            server.BaseUri,
            "search",
            "get-search",
            "--search-term",
            "agent",
            "--search-types",
            "user",
            "--top",
            "3",
            "--include-closed-and-stuck",
            "true");

        Assert.Equal(0, result.ExitCode);
        var request = server.Requests.Single();
        Assert.Equal("/search", request.Path);
        Assert.Equal("agent", request.Query["searchTerm"]);
        Assert.Equal("user", request.Query["searchTypes"]);
        Assert.Equal("3", request.Query["top"]);
        Assert.Equal("true", request.Query["includeClosedAndStuck"]);
    }

    [Fact]
    public async Task UsersAssign_BuildsBodyFromSetAndSetJson()
    {
        using var server = new TestServer(async ctx =>
        {
            ctx.Response.StatusCode = 204;
            await ctx.Response.OutputStream.FlushAsync();
        });

        var result = await RunCliAsync(
            server.BaseUri,
            "absence-regions",
            "users-assign",
            "--set",
            "regionId=region-1",
            "--set-json",
            "userIds=[\"user-1\",\"user-2\"]");

        Assert.Equal(0, result.ExitCode);

        var request = server.Requests.Single();
        Assert.Equal("PUT", request.Method);
        Assert.Equal("/absenceregions/users/assign", request.Path);

        var body = JsonDocument.Parse(request.Body ?? "{}");
        Assert.Equal("region-1", body.RootElement.GetProperty("regionId").GetString());
        var ids = body.RootElement.GetProperty("userIds").EnumerateArray().Select(x => x.GetString()).ToList();
        Assert.Equal(new[] { "user-1", "user-2" }, ids);
    }

    private static async Task<CliResult> RunCliAsync(Uri baseUri, params string[] args)
    {
        var cliDll = FindCliDll();
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        psi.ArgumentList.Add(cliDll);
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        psi.Environment["AWORK_TOKEN"] = "test-token";
        psi.Environment["AWORK_BASE_URL"] = baseUri.ToString().TrimEnd('/');

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start CLI process.");
        var stdOutTask = process.StandardOutput.ReadToEndAsync();
        var stdErrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return new CliResult(process.ExitCode, await stdOutTask, await stdErrTask);
    }

    private static string FindCliDll()
    {
        var root = FindRepoRoot();
        var path = Path.Combine(root, "src", "Awk.Cli", "bin", "Debug", "net10.0", "awork.dll");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("CLI build output not found. Build the CLI before running tests.", path);
        }

        return path;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var marker = Path.Combine(dir.FullName, "awk-cli.slnx");
            if (File.Exists(marker))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repo root (awk-cli.slnx).");
    }

    private sealed record CliResult(int ExitCode, string StdOut, string StdErr);
}

internal sealed class TestServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loop;
    private readonly Func<HttpListenerContext, Task> _handler;
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();
    private readonly TaskCompletionSource<bool> _firstRequest = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal TestServer(Func<HttpListenerContext, Task> handler)
    {
        _handler = handler;
        var port = GetFreePort();
        BaseUri = new Uri($"http://127.0.0.1:{port}/");
        _listener = new HttpListener();
        _listener.Prefixes.Add(BaseUri.ToString());
        _listener.Start();
        _loop = Task.Run(LoopAsync);
    }

    internal Uri BaseUri { get; }

    internal IReadOnlyList<RecordedRequest> Requests => _requests.ToList();

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Close();
        try { _loop.Wait(TimeSpan.FromSeconds(2)); } catch { }
    }

    private async Task LoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext? ctx = null;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch
            {
                break;
            }

            if (ctx is null) continue;
            var request = await CaptureRequestAsync(ctx.Request);
            _requests.Enqueue(request);
            _firstRequest.TrySetResult(true);
            await _handler(ctx);
            ctx.Response.OutputStream.Close();
        }
    }

    private static async Task<RecordedRequest> CaptureRequestAsync(HttpListenerRequest request)
    {
        string? body = null;
        if (request.HasEntityBody)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            body = await reader.ReadToEndAsync();
        }

        return new RecordedRequest(
            request.HttpMethod,
            request.Url?.AbsolutePath ?? string.Empty,
            request.Url?.Query ?? string.Empty,
            request.Headers,
            body);
    }

    private static int GetFreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

internal sealed record RecordedRequest(
    string Method,
    string Path,
    string RawQuery,
    System.Collections.Specialized.NameValueCollection Headers,
    string? Body)
{
    internal IReadOnlyDictionary<string, string> Query
    {
        get
        {
            if (string.IsNullOrWhiteSpace(RawQuery)) return new Dictionary<string, string>();
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var query = RawQuery.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in query)
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 0) continue;
                var key = WebUtility.UrlDecode(parts[0]);
                var value = parts.Length > 1 ? WebUtility.UrlDecode(parts[1]) : string.Empty;
                dict[key] = value;
            }
            return dict;
        }
    }
}

internal static class HttpListenerExtensions
{
    internal static async Task RespondJsonAsync(HttpListenerResponse response, string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
    }
}
