using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using MCPServer.BuildingBlock.Interfaces;

namespace MCPServer.Infrastructure.ExternalServices.Elastic;

public sealed partial class ElasticApiClient
{
    private const string LogsSearchPath = "logs-*/_search?ignore_unavailable=true&allow_no_indices=true";
    private const int MaxAttributes = 500;

    public async Task<LogSearchResult> SearchLogsAsync(LogSearchRequest request, CancellationToken cancellationToken)
    {
        var root = await SendAsync(
            () => httpClient.PostAsJsonAsync(LogsSearchPath, LogQueryBuilder.Search(request), cancellationToken),
            cancellationToken);
        ThrowIfTimedOut(root);

        var hits = root.GetProperty("hits");
        var entries = hits.TryGetProperty("hits", out var array)
            ? array.EnumerateArray().Select(h => ParseHit(h, includeAttributes: false)).OfType<LogEntry>().ToList()
            : [];

        long? total = request.CountTotal && hits.TryGetProperty("total", out var t) && t.TryGetProperty("value", out var v) ? v.GetInt64() : null;
        return new LogSearchResult(entries, total);
    }

    public async Task<LogEntry?> GetLogAsync(string index, string id, CancellationToken cancellationToken)
    {
        try
        {
            var root = await SendAsync(
                () => httpClient.PostAsJsonAsync($"{Uri.EscapeDataString(index)}/_search?ignore_unavailable=true&allow_no_indices=true", LogQueryBuilder.ById(id), cancellationToken),
                cancellationToken);
            ThrowIfTimedOut(root);

            var first = root.GetProperty("hits").GetProperty("hits").EnumerateArray().FirstOrDefault();
            return first.ValueKind == JsonValueKind.Object ? ParseHit(first, includeAttributes: true) : null;
        }
        catch (ElasticForbiddenException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<LogHistogramBucket>> GetLogHistogramAsync(LogFilter filter, int intervalSeconds, CancellationToken cancellationToken)
    {
        var root = await SendAsync(
            () => httpClient.PostAsJsonAsync(LogsSearchPath, LogQueryBuilder.Histogram(filter, intervalSeconds), cancellationToken),
            cancellationToken);
        ThrowIfTimedOut(root);

        var result = new List<LogHistogramBucket>();
        if (root.TryGetProperty("aggregations", out var aggs)
            && aggs.TryGetProperty("buckets", out var buckets)
            && buckets.TryGetProperty("buckets", out var array))
        {
            foreach (var bucket in array.EnumerateArray())
            {
                var start = DateTimeOffset.FromUnixTimeMilliseconds((long)bucket.GetProperty("key").GetDouble());
                var bySeverity = new Dictionary<string, long>();
                if (bucket.TryGetProperty("severity", out var sev) && sev.TryGetProperty("buckets", out var sevBuckets))
                {
                    foreach (var s in sevBuckets.EnumerateArray())
                    {
                        var name = (s.GetProperty("key").GetString() ?? "UNKNOWN").ToUpperInvariant();
                        bySeverity[name] = bySeverity.GetValueOrDefault(name) + s.GetProperty("doc_count").GetInt64();
                    }
                }

                result.Add(new LogHistogramBucket(start, bucket.GetProperty("doc_count").GetInt64(), bySeverity));
            }
        }

        return result;
    }

    private static void ThrowIfTimedOut(JsonElement root)
    {
        if (root.TryGetProperty("timed_out", out var timedOut) && timedOut.ValueKind == JsonValueKind.True)
        {
            throw new ElasticQueryTimeoutException();
        }
    }

    private static LogEntry? ParseHit(JsonElement hit, bool includeAttributes)
    {
        if (!hit.TryGetProperty("_source", out var source) || source.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var timestamp = TryGetPath(source, EdotLogFields.Timestamp, out var ts) ? ParseTimestamp(ts) : null;
        var sort = hit.TryGetProperty("sort", out var sortArray) && sortArray.ValueKind == JsonValueKind.Array
            ? sortArray.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => (long)x.GetDouble()).ToList()
            : [];

        return new LogEntry(
            Id: hit.GetProperty("_id").GetString() ?? "",
            Index: hit.GetProperty("_index").GetString() ?? "",
            Timestamp: timestamp ?? DateTimeOffset.UnixEpoch,
            Service: FirstString(source, EdotLogFields.Service),
            Environment: FirstString(source, EdotLogFields.Environment),
            Severity: FirstString(source, EdotLogFields.Severity)?.ToUpperInvariant(),
            Body: FirstString(source, EdotLogFields.Body),
            TraceId: FirstString(source, EdotLogFields.TraceId),
            SpanId: FirstString(source, EdotLogFields.SpanId),
            Host: FirstString(source, EdotLogFields.Host),
            Pod: FirstString(source, EdotLogFields.Pod),
            SortValues: sort,
            Attributes: includeAttributes ? Flatten(source) : null);
    }

    private static DateTimeOffset? ParseTimestamp(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String when DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) => parsed,
        JsonValueKind.Number => DateTimeOffset.FromUnixTimeMilliseconds((long)value.GetDouble()),
        _ => null
    };

    private static string? FirstString(JsonElement source, string[] candidates)
    {
        foreach (var path in candidates)
        {
            if (TryGetPath(source, path, out var value) && Scalar(value) is { Length: > 0 } text)
            {
                return text;
            }
        }

        return null;
    }

    /// <summary>Finds <paramref name="path"/> whether the document stores it as a dotted key or as nested objects.</summary>
    private static bool TryGetPath(JsonElement element, string path, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (element.TryGetProperty(path, out value))
        {
            return true;
        }

        for (var i = path.IndexOf('.'); i >= 0; i = path.IndexOf('.', i + 1))
        {
            if (element.TryGetProperty(path[..i], out var child) && TryGetPath(child, path[(i + 1)..], out value))
            {
                return true;
            }
        }

        return false;
    }

    private static string? Scalar(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        _ => null
    };

    private static Dictionary<string, string> Flatten(JsonElement source)
    {
        var result = new Dictionary<string, string>();
        Walk(source, "", result);
        return result;

        static void Walk(JsonElement element, string prefix, Dictionary<string, string> into)
        {
            if (into.Count >= MaxAttributes)
            {
                return;
            }

            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        Walk(property.Value, prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}", into);
                    }

                    break;
                case JsonValueKind.Null or JsonValueKind.Undefined:
                    break;
                case JsonValueKind.Array:
                    into[prefix] = element.GetRawText();
                    break;
                default:
                    into[prefix] = Scalar(element) ?? "";
                    break;
            }
        }
    }
}
