using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq;
using Awk.Models;
using AworkClient = Awk.Generated.AworkClient;

namespace Awk.Commands;

internal static class CommandHelpers
{
    internal static Task<ResponseEnvelope<object?>> GetTaskByIdOrIdentifier(
        AworkClient client,
        string taskIdOrIdentifier,
        Dictionary<string, object?>? query,
        CancellationToken cancellationToken)
    {
        return GetByIdOrKeyEndpoint(
            taskIdOrIdentifier,
            (id, finalQuery, ct) => client.GetTaskById(id, finalQuery, ct),
            (identifier, finalQuery, ct) => client.GetTasksKeyByTaskIdentifier(identifier, finalQuery, ct),
            query,
            cancellationToken);
    }

    internal static Task<ResponseEnvelope<object?>> GetProjectByIdOrKey(
        AworkClient client,
        string projectIdOrKey,
        Dictionary<string, object?>? query,
        CancellationToken cancellationToken)
    {
        return GetByIdOrKeyEndpoint(
            projectIdOrKey,
            (id, finalQuery, ct) => client.GetProjectById(id, finalQuery, ct),
            (key, finalQuery, ct) => client.GetProjectsKeyByProjectKey(key, finalQuery, ct),
            query,
            cancellationToken);
    }

    internal static object? ResolveBody(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var payload = raw.Trim();
        if (payload.StartsWith('@'))
        {
            var path = payload[1..].Trim();
            if (!File.Exists(path)) throw new FileNotFoundException("Body file not found.", path);
            payload = File.ReadAllText(path);
        }

        try
        {
            using var doc = JsonDocument.Parse(payload);
            return doc.RootElement.Clone();
        }
        catch
        {
            return payload;
        }
    }

    internal static object? BuildBody(string? rawBody, IEnumerable<string>? setPairs, IEnumerable<string>? setJsonPairs, object? fallbackBody = null)
    {
        var hasSets = (setPairs is not null && setPairs.Any()) || (setJsonPairs is not null && setJsonPairs.Any());
        if (!hasSets)
        {
            return ResolveBody(rawBody);
        }

        JsonNode? root = null;
        if (!string.IsNullOrWhiteSpace(rawBody))
        {
            var resolved = ResolveBody(rawBody);
            if (resolved is JsonElement element)
            {
                root = JsonNode.Parse(element.GetRawText());
            }
            else
            {
                throw new InvalidOperationException("Body must be JSON when using --set/--set-json.");
            }
        }
        else if (fallbackBody is not null)
        {
            root = ResolveBodyNode(fallbackBody);
        }

        root ??= new JsonObject();

        ApplyPairs(root, setPairs, treatAsJson: false);
        ApplyPairs(root, setJsonPairs, treatAsJson: true);

        return root;
    }

    internal static bool IsMissing(string? value) => string.IsNullOrWhiteSpace(value);

    internal static bool IsMissing(string[]? value) => value is null || value.Length == 0;

    internal static IEnumerable<string>? MergePairs(IEnumerable<string>? basePairs, IEnumerable<string>? extraPairs)
    {
        var list = new List<string>();
        if (basePairs is not null) list.AddRange(basePairs);
        if (extraPairs is not null) list.AddRange(extraPairs);
        return list.Count == 0 ? null : list;
    }

    private static Task<ResponseEnvelope<object?>> GetByIdOrKeyEndpoint(
        string idOrKey,
        Func<string, Dictionary<string, object?>?, CancellationToken, Task<ResponseEnvelope<object?>>> getById,
        Func<string, Dictionary<string, object?>?, CancellationToken, Task<ResponseEnvelope<object?>>> getByKey,
        Dictionary<string, object?>? finalQuery,
        CancellationToken cancellationToken)
    {
        if (Guid.TryParse(idOrKey, out _))
        {
            return getById(idOrKey, finalQuery, cancellationToken);
        }

        return getByKey(idOrKey, finalQuery, cancellationToken);
    }

    private static void ApplyPairs(JsonNode root, IEnumerable<string>? pairs, bool treatAsJson)
    {
        if (pairs is null) return;

        foreach (var pair in pairs)
        {
            if (string.IsNullOrWhiteSpace(pair)) continue;
            var idx = pair.IndexOf('=');
            if (idx <= 0) throw new InvalidOperationException($"Invalid key/value '{pair}'. Use KEY=VALUE.");
            var key = pair[..idx].Trim();
            var valueRaw = pair[(idx + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key)) continue;

            var value = treatAsJson ? ParseJsonValue(valueRaw) : ParseScalarValue(valueRaw);
            SetPathValue(root, key, value);
        }
    }

    private static JsonNode? ResolveBodyNode(object body)
    {
        if (body is JsonNode jsonNode)
        {
            return jsonNode.DeepClone();
        }

        if (body is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined) return null;
            return JsonNode.Parse(element.GetRawText());
        }

        return JsonSerializer.SerializeToNode(body);
    }

    private static JsonNode? ParseScalarValue(string value)
    {
        if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase)) return null;
        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) return JsonValue.Create(true);
        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return JsonValue.Create(false);

        if (int.TryParse(value, out var i)) return JsonValue.Create(i);
        if (long.TryParse(value, out var l)) return JsonValue.Create(l);
        if (double.TryParse(value, out var d)) return JsonValue.Create(d);

        if (value.StartsWith('@'))
        {
            var path = value[1..].Trim();
            if (!File.Exists(path)) throw new FileNotFoundException("Value file not found.", path);
            var text = File.ReadAllText(path);
            try
            {
                return JsonNode.Parse(text);
            }
            catch
            {
                return JsonValue.Create(text);
            }
        }

        return JsonValue.Create(value);
    }

    private static JsonNode? ParseJsonValue(string value)
    {
        if (value.StartsWith('@'))
        {
            var path = value[1..].Trim();
            if (!File.Exists(path)) throw new FileNotFoundException("JSON file not found.", path);
            value = File.ReadAllText(path);
        }

        return JsonNode.Parse(value);
    }

    private static void SetPathValue(JsonNode root, string path, JsonNode? value)
    {
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        var current = root as JsonObject ?? new JsonObject();
        if (!ReferenceEquals(current, root))
        {
            throw new InvalidOperationException("Root body must be a JSON object.");
        }

        for (var i = 0; i < parts.Length; i++)
        {
            var key = parts[i];
            if (i == parts.Length - 1)
            {
                current[key] = value;
                return;
            }

            if (current[key] is JsonObject next)
            {
                current = next;
                continue;
            }

            var created = new JsonObject();
            current[key] = created;
            current = created;
        }
    }
}
