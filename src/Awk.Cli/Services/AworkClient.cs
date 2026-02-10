using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Awk.Models;

namespace Awk.Generated;

public sealed partial class AworkClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;

    public AworkClient(HttpClient http, string baseUrl)
    {
        _http = http;
        _baseUrl = baseUrl.TrimEnd('/');
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
    }

    public async Task<ResponseEnvelope<object?>> Call(
        string method,
        string path,
        Dictionary<string, object?>? query,
        object? body,
        string? contentType,
        CancellationToken cancellationToken)
    {
        var url = BuildUrl(path, query);
        var httpMethod = new HttpMethod(method);
        var contentFactory = await BuildContentFactory(body, contentType, cancellationToken);
        var select = GetSelectQuery(query);
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            using var request = new HttpRequestMessage(httpMethod, url);
            var content = contentFactory?.Invoke();
            if (content is not null)
            {
                request.Content = content;
            }

            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt == maxAttempts)
            {
                return await BuildEnvelopeAsync(response, select, cancellationToken);
            }

            var delay = GetRetryDelay(response.Headers, attempt);
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        throw new InvalidOperationException("Rate limit retry loop exhausted.");
    }

    private async Task<Func<HttpContent?>?> BuildContentFactory(object? body, string? contentType, CancellationToken cancellationToken)
    {
        if (body is null) return null;

        if (body is HttpContent content)
        {
            var bytes = await content.ReadAsByteArrayAsync(cancellationToken);
            var headers = content.Headers.ToList();
            return () =>
            {
                var clone = new ByteArrayContent(bytes);
                foreach (var header in headers)
                {
                    clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
                return clone;
            };
        }

        var json = JsonSerializer.Serialize(body, _jsonOptions);

        if (string.Equals(contentType, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
        {
            var jsonBytes = Encoding.UTF8.GetBytes(json);
            return () => JsonToMultipart(jsonBytes);
        }

        var mediaType = contentType ?? "application/json";
        return () => new StringContent(json, Encoding.UTF8, mediaType);
    }

    private async Task<ResponseEnvelope<object?>> BuildEnvelopeAsync(HttpResponseMessage response, string? select, CancellationToken cancellationToken)
    {
        var statusCode = (int)response.StatusCode;
        var traceId = ExtractTraceId(response.Headers);
        object? payload = null;

        if (response.Content is not null)
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(raw))
            {
                payload = TryParseJson(raw) ?? raw;
            }
        }

        if (payload is not null && !string.IsNullOrWhiteSpace(select) && statusCode is >= 200 and <= 299)
        {
            payload = ApplySelectFilter(payload, select);
        }

        return new ResponseEnvelope<object?>(statusCode, traceId, payload);
    }

    private static TimeSpan GetRetryDelay(HttpResponseHeaders headers, int attempt)
    {
        var retryAfter = headers.RetryAfter;
        if (retryAfter is not null)
        {
            if (retryAfter.Delta.HasValue)
            {
                return retryAfter.Delta.Value < TimeSpan.Zero ? TimeSpan.Zero : retryAfter.Delta.Value;
            }

            if (retryAfter.Date.HasValue)
            {
                var delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
            }
        }

        var seconds = Math.Min(30, Math.Pow(2, attempt - 1));
        return TimeSpan.FromSeconds(seconds);
    }

    private static string? GetSelectQuery(Dictionary<string, object?>? query)
    {
        if (query is null || query.Count == 0) return null;

        foreach (var (key, value) in query)
        {
            if (!string.Equals(key, "select", StringComparison.OrdinalIgnoreCase)) continue;
            if (value is null) return null;
            if (value is string s) return s.Trim();
            if (value is System.Collections.IEnumerable enumerable)
            {
                var parts = new List<string>();
                foreach (var item in enumerable)
                {
                    if (item is null) continue;
                    parts.Add(item.ToString() ?? string.Empty);
                }
                return string.Join(",", parts);
            }
            return value.ToString()?.Trim() ?? string.Empty;
        }

        return null;
    }

    private static object? ApplySelectFilter(object payload, string select)
    {
        if (payload is not JsonElement element) return payload;
        var fields = ParseSelect(select);
        if (fields.Count == 0) return payload;
        return FilterElement(element, fields);
    }

    private static HashSet<string> ParseSelect(string select)
    {
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in select.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = token.Trim();
            if (string.IsNullOrWhiteSpace(trimmed)) continue;
            fields.Add(trimmed);
        }
        return fields;
    }

    private static JsonNode? FilterElement(JsonElement element, HashSet<string> fields)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => FilterObject(element, fields),
            JsonValueKind.Array => FilterArray(element, fields),
            _ => JsonNode.Parse(element.GetRawText())
        };
    }

    private static JsonNode FilterObject(JsonElement element, HashSet<string> fields)
    {
        var node = new JsonObject();
        foreach (var property in element.EnumerateObject())
        {
            if (!fields.Contains(property.Name)) continue;
            node[property.Name] = JsonNode.Parse(property.Value.GetRawText());
        }
        return node;
    }

    private static JsonNode FilterArray(JsonElement element, HashSet<string> fields)
    {
        var array = new JsonArray();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                array.Add(FilterObject(item, fields));
            }
            else
            {
                array.Add(JsonNode.Parse(item.GetRawText()));
            }
        }
        return array;
    }

    private string BuildUrl(string path, Dictionary<string, object?>? query)
    {
        var url = path.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? path
            : _baseUrl + path;

        if (query is null || query.Count == 0) return url;

        var parts = new List<string>();
        foreach (var (key, value) in query)
        {
            if (value is null) continue;
            if (string.Equals(key, "select", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (value is string s)
            {
                parts.Add(Encode(key, s));
                continue;
            }

            if (value is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item is null) continue;
                    parts.Add(Encode(key, FormatQueryValue(item)));
                }
                continue;
            }

            parts.Add(Encode(key, FormatQueryValue(value)));
        }

        if (parts.Count == 0) return url;
        return url + "?" + string.Join("&", parts);
    }

    private static string Encode(string key, string value) =>
        Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value);

    private static string FormatQueryValue(object value)
    {
        return value switch
        {
            bool b => b ? "true" : "false",
            DateTimeOffset dto => dto.ToString("o"),
            DateOnly date => date.ToString("O"),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static object? TryParseJson(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractTraceId(HttpResponseHeaders? headers)
    {
        if (headers is null) return null;
        if (headers.TryGetValues("trace-id", out var traceId)) return traceId.FirstOrDefault();
        if (headers.TryGetValues("traceparent", out var traceParent)) return traceParent.FirstOrDefault();
        if (headers.TryGetValues("x-correlation-id", out var correlation)) return correlation.FirstOrDefault();
        if (headers.TryGetValues("request-id", out var requestId)) return requestId.FirstOrDefault();
        return null;
    }

    private static HttpContent JsonToMultipart(byte[] jsonBytes)
    {
        using var doc = JsonDocument.Parse(jsonBytes);
        var root = doc.RootElement;
        var form = new MultipartFormDataContent();

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                var value = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                    JsonValueKind.Null => string.Empty,
                    _ => property.Value.GetRawText()
                };
                form.Add(new StringContent(value, Encoding.UTF8), property.Name);
            }
        }

        return form;
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);
}
