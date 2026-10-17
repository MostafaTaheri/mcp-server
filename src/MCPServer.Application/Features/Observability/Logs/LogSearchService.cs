using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MCPServer.Application.Mcp;
using MCPServer.BuildingBlock.Interfaces;

namespace MCPServer.Application.Features.Observability.Logs;

/// <summary>Bounds that keep a single log tool call cheap for the cluster and small for the model. Local to logs until OBS-SAFE-03 centralises them.</summary>
public static class LogQueryLimits
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 500;
    public static readonly TimeSpan DefaultRange = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DefaultErrorRange = TimeSpan.FromHours(1);
    public static readonly TimeSpan MaxRange = TimeSpan.FromHours(24);
    public const int MaxBuckets = 200;
    public const int TargetBuckets = 100;
    public const int MaxFieldChars = 2000;
    public const int ResponseBudgetChars = 100_000;
    public const int DefaultContextEntries = 20;
    public const int MaxContextEntries = 100;
    public const int DefaultContextSeconds = 30;
    public const int MaxContextSeconds = 300;
    public const int ErrorScanSize = 500;
    public const int DefaultGroups = 20;
    public const int MaxGroups = 50;
    public const int MaxAttributes = 300;
    public const int MaxTextChars = 500;
    public const int MaxNameChars = 200;
    public static readonly TimeSpan CursorLifetime = TimeSpan.FromMinutes(10);
}

public sealed partial class LogSearchService(IElasticApiClient elastic, ILogTextSanitizer sanitizer, TimeProvider time) : ILogSearchService
{
    public const string TruncationMarker = "…[truncated]";

    private static readonly HashSet<string> Severities = new(StringComparer.OrdinalIgnoreCase)
        { "TRACE", "DEBUG", "INFO", "WARN", "WARNING", "ERROR", "FATAL", "CRITICAL" };

    [GeneratedRegex(@"^(\.ds-)?logs-[a-z0-9][a-z0-9._-]*$")]
    private static partial Regex LogIndexName();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{8,64}$")]
    private static partial Regex TraceIdFormat();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex Uuid();

    [GeneratedRegex(@"\b0x[0-9a-fA-F]+\b|\b[0-9a-fA-F]{12,}\b")]
    private static partial Regex Hex();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // ---- logs_search (LOG-01) -------------------------------------------------------------

    public Task<LogSearchResponse> SearchAsync(string? from, string? to, string? service, string? environment, IReadOnlyList<string>? severity, string? text, int limit, string? cursor, CancellationToken cancellationToken)
    {
        var criteria = new Criteria(Name(service, "service"), Name(environment, "environment"), NormalizeSeverities(severity), ValidateText(text), null);
        return PageAsync(criteria, from, to, ascending: false, LogQueryLimits.DefaultRange, limit, cursor, null, cancellationToken);
    }

    // ---- logs_by_trace (LOG-05) -----------------------------------------------------------

    public Task<LogSearchResponse> ByTraceAsync(string traceId, string? from, string? to, int limit, string? cursor, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(traceId) || !TraceIdFormat().IsMatch(traceId.Trim()))
        {
            throw Invalid("The trace id is not valid.", "Pass the trace_id exactly as it appears in a log entry or trace.");
        }

        const string explanation =
            "No logs carry this trace_id in the searched range. Either the range does not cover the request, or the logs have no trace correlation fields (trace_id / span_id). " +
            "Widen the range, or read the resource obs://fields/logs to check whether trace_id exists.";

        return PageAsync(new Criteria(null, null, null, null, traceId.Trim()), from, to, ascending: true, LogQueryLimits.MaxRange, limit, cursor, explanation, cancellationToken);
    }

    // ---- logs_get (LOG-02) ----------------------------------------------------------------

    public async Task<LogDetailResponse> GetAsync(string index, string id, CancellationToken cancellationToken)
    {
        var entry = await FetchAsync(index, id, cancellationToken);
        var truncated = false;

        var attributes = new Dictionary<string, string>();
        foreach (var (key, value) in entry.Attributes!.Take(LogQueryLimits.MaxAttributes))
        {
            attributes[key] = Clean(value, ref truncated);
        }

        truncated |= entry.Attributes!.Count > LogQueryLimits.MaxAttributes;
        return new LogDetailResponse(ToItem(entry, ref truncated), attributes, truncated);
    }

    // ---- logs_error_summary (LOG-03) ------------------------------------------------------

    public async Task<ErrorSummaryResponse> ErrorSummaryAsync(string? from, string? to, string? service, string? environment, int maxGroups, CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        var range = ResolveRange(from, to, LogQueryLimits.DefaultErrorRange, notes);
        var cap = Clamp(maxGroups, LogQueryLimits.DefaultGroups, LogQueryLimits.MaxGroups, "maxGroups", notes);
        var criteria = new Criteria(Name(service, "service"), Name(environment, "environment"), ["ERROR", "FATAL"], null, null);

        var result = await elastic.SearchLogsAsync(
            new LogSearchRequest(criteria.ToFilter(range), LogQueryLimits.ErrorScanSize, Ascending: false, CountTotal: true), cancellationToken);

        var groups = result.Entries
            .GroupBy(e => (Service: e.Service ?? "(unknown)", Pattern: Pattern(e.Body)))
            .Select(g =>
            {
                var sample = g.First();
                var textTruncated = false;
                return new ErrorGroup(
                    g.Key.Service,
                    g.Key.Pattern,
                    g.Count(),
                    Iso(g.Min(e => e.Timestamp)),
                    Iso(g.Max(e => e.Timestamp)),
                    sample.Id,
                    sample.Index,
                    Clean(sample.Body ?? "", ref textTruncated));
            })
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Service, StringComparer.Ordinal)
            .ToList();

        var truncated = groups.Count > cap;
        var total = result.Total ?? result.Entries.Count;
        var approximate = total > result.Entries.Count;
        if (approximate)
        {
            notes.Add($"Counts cover the newest {result.Entries.Count} of {total} errors; narrow the time range for exact counts.");
        }

        return new ErrorSummaryResponse(
            ToRange(range), criteria.Summary(), groups.Take(cap).ToList(), total, result.Entries.Count, approximate, truncated,
            truncated ? $"Only the top {cap} groups are shown. Narrow by service or time range, or raise maxGroups (max {LogQueryLimits.MaxGroups})." : null,
            notes);
    }

    // ---- logs_context (LOG-04) ------------------------------------------------------------

    public async Task<LogContextResponse> ContextAsync(string index, string id, int before, int after, int windowSeconds, CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        var beforeCount = Clamp(before, LogQueryLimits.DefaultContextEntries, LogQueryLimits.MaxContextEntries, "before", notes);
        var afterCount = Clamp(after, LogQueryLimits.DefaultContextEntries, LogQueryLimits.MaxContextEntries, "after", notes);
        var window = Clamp(windowSeconds, LogQueryLimits.DefaultContextSeconds, LogQueryLimits.MaxContextSeconds, "windowSeconds", notes);

        var anchor = await FetchAsync(index, id, cancellationToken);
        var criteria = new Criteria(anchor.Service, null, null, null, null, anchor.Host, anchor.Pod);

        var earlier = await elastic.SearchLogsAsync(
            new LogSearchRequest(criteria.ToFilter(new Range(anchor.Timestamp.AddSeconds(-window), anchor.Timestamp, false)), beforeCount + 1), cancellationToken);
        var later = await elastic.SearchLogsAsync(
            new LogSearchRequest(criteria.ToFilter(new Range(anchor.Timestamp, anchor.Timestamp.AddSeconds(window), false)), afterCount + 1, Ascending: true), cancellationToken);

        var beforeEntries = earlier.Entries.Where(e => e.Id != anchor.Id).Take(beforeCount).Reverse().ToList();
        var seen = beforeEntries.Select(e => e.Id).ToHashSet();
        var afterEntries = later.Entries.Where(e => e.Id != anchor.Id && !seen.Contains(e.Id)).Take(afterCount).ToList();

        var truncated = false;
        var scope = criteria.Summary();
        return new LogContextResponse(
            ToItem(anchor, ref truncated),
            ToItems(beforeEntries, ref truncated),
            ToItems(afterEntries, ref truncated),
            scope.Length == 0 ? "all logs" : scope,
            truncated,
            notes);
    }

    // ---- logs_histogram (LOG-06) ----------------------------------------------------------

    public async Task<LogHistogramResponse> HistogramAsync(string? from, string? to, string? service, string? environment, IReadOnlyList<string>? severity, string? text, CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        var range = ResolveRange(from, to, LogQueryLimits.DefaultRange, notes);
        var criteria = new Criteria(Name(service, "service"), Name(environment, "environment"), NormalizeSeverities(severity), ValidateText(text), null);
        var interval = ChooseInterval(range.To - range.From);

        var buckets = await elastic.GetLogHistogramAsync(criteria.ToFilter(range), interval, cancellationToken);
        if (buckets.Count > LogQueryLimits.MaxBuckets)
        {
            buckets = buckets.Take(LogQueryLimits.MaxBuckets).ToList();
            notes.Add($"Only the first {LogQueryLimits.MaxBuckets} buckets are shown.");
        }

        return new LogHistogramResponse(
            ToRange(range), criteria.Summary(), interval, buckets.Sum(b => b.Total),
            buckets.Select(b => new LogHistogramBucketItem(Iso(b.Start), b.Total, b.BySeverity)).ToList(), notes);
    }

    /// <summary>Smallest round interval that keeps the bucket count at or under <see cref="LogQueryLimits.TargetBuckets"/>.</summary>
    public static int ChooseInterval(TimeSpan span)
    {
        int[] ladder = [1, 5, 10, 30, 60, 300, 600, 1800, 3600, 10800, 21600, 43200, 86400];
        return ladder.FirstOrDefault(s => span.TotalSeconds / s <= LogQueryLimits.TargetBuckets, ladder[^1]);
    }

    // ---- shared paging (LOG-01, LOG-05, LOG-07) -------------------------------------------

    private async Task<LogSearchResponse> PageAsync(Criteria criteria, string? from, string? to, bool ascending, TimeSpan defaultSpan, int limit, string? cursor, string? emptyExplanation, CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        var pageSize = Clamp(limit, LogQueryLimits.DefaultLimit, LogQueryLimits.MaxLimit, "limit", notes);

        Range range;
        IReadOnlyList<long>? searchAfter = null;
        if (cursor is not null)
        {
            var payload = DecodeCursor(cursor, criteria.Hash(ascending));
            range = new Range(DateTimeOffset.Parse(payload.From, CultureInfo.InvariantCulture), DateTimeOffset.Parse(payload.To, CultureInfo.InvariantCulture), false);
            searchAfter = payload.After;
        }
        else
        {
            range = ResolveRange(from, to, defaultSpan, notes);
        }

        // One extra row tells us whether another page exists.
        var result = await elastic.SearchLogsAsync(new LogSearchRequest(criteria.ToFilter(range), pageSize + 1, ascending, searchAfter), cancellationToken);
        var hasMore = result.Entries.Count > pageSize;
        var page = result.Entries.Take(pageSize).ToList();

        var truncated = false;
        var items = new List<LogItem>();
        var used = 0;
        var kept = 0;
        var cutByBudget = false;
        foreach (var entry in page)
        {
            var item = ToItem(entry, ref truncated);
            var size = (item.Body?.Length ?? 0) + 400;
            if (used + size > LogQueryLimits.ResponseBudgetChars && kept > 0)
            {
                cutByBudget = true;
                break;
            }

            used += size;
            items.Add(item);
            kept++;
        }

        truncated |= cutByBudget;
        var more = hasMore || cutByBudget;
        var nextCursor = more && kept > 0 ? EncodeCursor(range, criteria.Hash(ascending), page[kept - 1].SortValues) : null;

        string? nextStep = null;
        if (nextCursor is not null)
        {
            nextStep = cutByBudget
                ? "The response was cut to fit the size budget. Pass 'cursor' to continue, or narrow by service, severity, text or a shorter time range."
                : "More results exist. Pass 'cursor' to get the next page, or narrow the filters.";
        }
        else if (truncated)
        {
            nextStep = "Long fields were shortened. Use logs_get with the entry's index and id to read the full record.";
        }

        return new LogSearchResponse(
            ToRange(range), criteria.Summary(), items, items.Count, truncated, nextCursor, nextStep, notes,
            items.Count == 0 ? emptyExplanation ?? "No log entries matched the query in the effective range." : null);
    }

    // ---- helpers --------------------------------------------------------------------------

    private async Task<LogEntry> FetchAsync(string index, string id, CancellationToken cancellationToken)
    {
        // Same error for "bad input", "missing" and "not allowed" so nothing can be probed.
        var notFound = new ToolException(ToolErrorCode.NotFound, "Log entry not found or not accessible.", "Use the index and id exactly as returned by a logs_search result.");
        if (string.IsNullOrWhiteSpace(index) || string.IsNullOrWhiteSpace(id) || id.Length > 512 || !LogIndexName().IsMatch(index.Trim()))
        {
            throw notFound;
        }

        return await elastic.GetLogAsync(index.Trim(), id.Trim(), cancellationToken) ?? throw notFound;
    }

    private Range ResolveRange(string? from, string? to, TimeSpan defaultSpan, List<string> notes)
    {
        var now = TrimToMillis(time.GetUtcNow());
        var end = ParseInstant(to, "to") ?? now;
        var start = ParseInstant(from, "from");
        var defaulted = from is null && to is null;

        if (start is null)
        {
            start = end - defaultSpan;
        }
        else if (start >= end)
        {
            throw Invalid("'from' must be earlier than 'to'.", "Swap the values or omit them to use the default range.");
        }

        if (end - start > LogQueryLimits.MaxRange)
        {
            start = end - LogQueryLimits.MaxRange;
            notes.Add($"The time range was clamped to the maximum of {LogQueryLimits.MaxRange.TotalHours:0} hours (ending at 'to').");
        }

        return new Range(start.Value, end, defaulted);
    }

    // Elastic and the paging cursor both work in milliseconds; keeping full ticks would make a resumed page query a different range.
    private static DateTimeOffset TrimToMillis(DateTimeOffset value) => new(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, value.Offset);

    private static DateTimeOffset? ParseInstant(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            return TrimToMillis(parsed);
        }

        throw Invalid($"'{name}' is not a valid date-time.", "Use ISO 8601, e.g. 2026-10-16T10:00:00Z.");
    }

    private static int Clamp(int value, int fallback, int max, string name, List<string> notes)
    {
        if (value <= 0)
        {
            return fallback;
        }

        if (value > max)
        {
            notes.Add($"'{name}' was clamped to the maximum of {max}.");
            return max;
        }

        return value;
    }

    private static string? Name(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().Length <= LogQueryLimits.MaxNameChars
            ? value.Trim()
            : throw Invalid($"'{name}' is too long.", $"Use at most {LogQueryLimits.MaxNameChars} characters.");
    }

    private static string? ValidateText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (text.Length > LogQueryLimits.MaxTextChars)
        {
            throw Invalid("The search text is too long.", $"Use at most {LogQueryLimits.MaxTextChars} characters.");
        }

        if (text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Any(t => t.TrimStart('(', '"', '+', '-').StartsWith('*') || t.TrimStart('(', '"', '+', '-').StartsWith('?')))
        {
            throw Invalid("Search terms that start with a wildcard are not allowed because they scan every document.", "Use whole words or a prefix such as 'timeout*'.");
        }

        return text.Trim();
    }

    private static IReadOnlyList<string>? NormalizeSeverities(IReadOnlyList<string>? severities)
    {
        if (severities is not { Count: > 0 })
        {
            return null;
        }

        var bad = severities.FirstOrDefault(s => !Severities.Contains(s?.Trim() ?? ""));
        if (bad is not null)
        {
            throw Invalid("Unknown severity.", "Use any of: TRACE, DEBUG, INFO, WARN, ERROR, FATAL.");
        }

        return severities.Select(s => s.Trim().ToUpperInvariant()).Distinct().ToList();
    }

    private static string Pattern(string? body)
    {
        var line = (body ?? "").Split('\n', 2)[0];
        line = Uuid().Replace(line, "<id>");
        line = Hex().Replace(line, "<hex>");
        line = Digits().Replace(line, "<n>");
        line = Whitespace().Replace(line, " ").Trim();
        return line.Length > 200 ? line[..200] + TruncationMarker : line;
    }

    private string Clean(string value, ref bool truncated)
    {
        var text = sanitizer.Sanitize(value);
        if (text.Length <= LogQueryLimits.MaxFieldChars)
        {
            return text;
        }

        truncated = true;
        return text[..LogQueryLimits.MaxFieldChars] + TruncationMarker;
    }

    private LogItem ToItem(LogEntry e, ref bool truncated) => new(
        e.Id, e.Index, Iso(e.Timestamp), e.Service, e.Environment, e.Severity,
        e.Body is null ? null : Clean(e.Body, ref truncated),
        e.TraceId, e.SpanId, e.Host, e.Pod);

    private List<LogItem> ToItems(IEnumerable<LogEntry> entries, ref bool truncated)
    {
        var items = new List<LogItem>();
        foreach (var entry in entries)
        {
            items.Add(ToItem(entry, ref truncated));
        }

        return items;
    }

    private static string Iso(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static LogRange ToRange(Range r) => new(Iso(r.From), Iso(r.To), r.Defaulted);

    private static ToolException Invalid(string message, string nextStep) => new(ToolErrorCode.InvalidArgument, message, nextStep);

    // ---- cursor ---------------------------------------------------------------------------

    private sealed record CursorPayload(string From, string To, string Hash, long[] After, long IssuedAt);

    private string EncodeCursor(Range range, string hash, IReadOnlyList<long> sortValues)
    {
        var payload = new CursorPayload(Iso(range.From), Iso(range.To), hash, sortValues.ToArray(), time.GetUtcNow().ToUnixTimeSeconds());
        return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private CursorPayload DecodeCursor(string cursor, string expectedHash)
    {
        var expired = new ToolException(ToolErrorCode.CursorExpired, "The cursor is invalid or has expired.", "Repeat the search without a cursor.");
        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var payload = JsonSerializer.Deserialize<CursorPayload>(Convert.FromBase64String(padded));
            if (payload is null
                || payload.Hash != expectedHash
                || payload.After is not { Length: > 0 }
                || time.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(payload.IssuedAt) > LogQueryLimits.CursorLifetime)
            {
                throw expired;
            }

            return payload;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            throw expired;
        }
    }

    // ---- criteria -------------------------------------------------------------------------

    private sealed record Range(DateTimeOffset From, DateTimeOffset To, bool Defaulted);

    private sealed record Criteria(string? Service, string? Environment, IReadOnlyList<string>? Severities, string? Text, string? TraceId, string? Host = null, string? Pod = null)
    {
        public LogFilter ToFilter(Range range) => new(range.From, range.To, Service, Environment, Severities, Text, TraceId, Host, Pod);

        public string Summary()
        {
            var parts = new List<string>();
            if (Service is not null) parts.Add($"service={Service}");
            if (Environment is not null) parts.Add($"environment={Environment}");
            if (Severities is { Count: > 0 }) parts.Add($"severity={string.Join(',', Severities)}");
            if (Text is not null) parts.Add($"text=\"{Text}\"");
            if (TraceId is not null) parts.Add($"trace_id={TraceId}");
            if (Host is not null) parts.Add($"host={Host}");
            if (Pod is not null) parts.Add($"pod={Pod}");
            return parts.Count == 0 ? "all logs" : string.Join("; ", parts);
        }

        public string Hash(bool ascending)
        {
            var canonical = $"{Service}|{Environment}|{string.Join(',', Severities ?? [])}|{Text}|{TraceId}|{Host}|{Pod}|{ascending}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..16];
        }
    }
}
