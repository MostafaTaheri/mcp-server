using MCPServer.BuildingBlock.Interfaces;

namespace MCPServer.Infrastructure.ExternalServices.Elastic;

/// <summary>Builds read-only <c>_search</c> bodies for logs. Every query is bounded by a time range, a size and a server-side timeout.</summary>
internal static class LogQueryBuilder
{
    public const string ServerTimeout = "15s";

    private static readonly Dictionary<string, string[]> SeveritySynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["WARN"] = ["WARN", "WARNING"],
        ["WARNING"] = ["WARN", "WARNING"],
        ["FATAL"] = ["FATAL", "CRITICAL"],
        ["CRITICAL"] = ["FATAL", "CRITICAL"]
    };

    public static Dictionary<string, object?> Search(LogSearchRequest request)
    {
        var body = new Dictionary<string, object?>
        {
            ["size"] = request.Size,
            ["timeout"] = ServerTimeout,
            ["track_total_hits"] = request.CountTotal,
            ["_source"] = EdotLogFields.SourceFields,
            ["query"] = Query(request.Filter),
            ["sort"] = Sort(request.Ascending)
        };

        if (request.SearchAfter is { Count: > 0 })
        {
            body["search_after"] = request.SearchAfter;
        }

        return body;
    }

    public static Dictionary<string, object?> ById(string id) => new()
    {
        ["size"] = 1,
        ["timeout"] = ServerTimeout,
        ["query"] = new Dictionary<string, object?> { ["ids"] = new { values = new[] { id } } }
    };

    public static Dictionary<string, object?> Histogram(LogFilter filter, int intervalSeconds) => new()
    {
        ["size"] = 0,
        ["timeout"] = ServerTimeout,
        ["track_total_hits"] = false,
        ["query"] = Query(filter),
        ["aggs"] = new Dictionary<string, object?>
        {
            ["buckets"] = new Dictionary<string, object?>
            {
                ["date_histogram"] = new Dictionary<string, object?>
                {
                    ["field"] = EdotLogFields.Timestamp,
                    ["fixed_interval"] = $"{intervalSeconds}s",
                    ["min_doc_count"] = 0,
                    ["extended_bounds"] = new { min = Iso(filter.From), max = Iso(filter.To) }
                },
                ["aggs"] = new Dictionary<string, object?>
                {
                    ["severity"] = new { terms = new { field = EdotLogFields.Severity[0], size = 12 } }
                }
            }
        }
    };

    public static Dictionary<string, object?> Query(LogFilter f)
    {
        var filters = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["range"] = new Dictionary<string, object?>
                {
                    [EdotLogFields.Timestamp] = new { gte = Iso(f.From), lte = Iso(f.To), format = "strict_date_optional_time" }
                }
            }
        };

        AddAny(filters, EdotLogFields.Service, f.Service is null ? null : [f.Service]);
        AddAny(filters, EdotLogFields.Environment, f.Environment is null ? null : [f.Environment]);
        AddAny(filters, EdotLogFields.TraceId, f.TraceId is null ? null : [f.TraceId]);
        AddAny(filters, EdotLogFields.Host, f.Host is null ? null : [f.Host]);
        AddAny(filters, EdotLogFields.Pod, f.Pod is null ? null : [f.Pod]);

        if (f.Severities is { Count: > 0 })
        {
            AddAny(filters, EdotLogFields.Severity, ExpandSeverities(f.Severities));
        }

        if (!string.IsNullOrWhiteSpace(f.Text))
        {
            filters.Add(new
            {
                simple_query_string = new
                {
                    query = f.Text,
                    fields = EdotLogFields.Body,
                    default_operator = "and",
                    lenient = true
                }
            });
        }

        return new Dictionary<string, object?> { ["bool"] = new { filter = filters } };
    }

    private static object[] Sort(bool ascending) =>
    [
        new Dictionary<string, object?> { [EdotLogFields.Timestamp] = new { order = ascending ? "asc" : "desc" } },
        new Dictionary<string, object?> { ["_doc"] = new { order = "asc" } }
    ];

    /// <summary>Matches when any of the candidate fields holds any of the values (layouts differ between EDOT, APM and custom indices).</summary>
    private static void AddAny(List<object> filters, string[] fields, IEnumerable<string>? values)
    {
        if (values is null)
        {
            return;
        }

        var list = values.ToArray();
        var should = fields
            .Select(field => (object)new Dictionary<string, object?> { ["terms"] = new Dictionary<string, object?> { [field] = list } })
            .ToArray();
        filters.Add(new { @bool = new { should, minimum_should_match = 1 } });
    }

    private static IEnumerable<string> ExpandSeverities(IEnumerable<string> severities)
    {
        var names = severities
            .SelectMany(s => SeveritySynonyms.TryGetValue(s, out var synonyms) ? synonyms : [s])
            .Select(s => s.ToUpperInvariant())
            .Distinct();

        // severity_text is a case-sensitive keyword and real data mixes casings.
        return names.SelectMany(n => new[] { n, n.ToLowerInvariant(), char.ToUpperInvariant(n[0]) + n[1..].ToLowerInvariant() }).Distinct();
    }

    private static string Iso(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
}
