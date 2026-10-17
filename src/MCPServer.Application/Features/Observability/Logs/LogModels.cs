namespace MCPServer.Application.Features.Observability.Logs;

/// <summary>The time range actually queried. <see cref="Defaulted"/> is true when the caller gave none.</summary>
public sealed record LogRange(string From, string To, bool Defaulted);

public sealed record LogItem(
    string Id,
    string Index,
    string Timestamp,
    string? Service,
    string? Environment,
    string? Severity,
    string? Body,
    string? TraceId,
    string? SpanId,
    string? Host,
    string? Pod);

/// <param name="Truncated">Results or long fields were cut to fit the size budget.</param>
/// <param name="Cursor">Pass back as <c>cursor</c> to get the next page; null when there are no more results.</param>
public sealed record LogSearchResponse(
    LogRange Range,
    string Query,
    IReadOnlyList<LogItem> Entries,
    int Count,
    bool Truncated,
    string? Cursor,
    string? NextStep,
    IReadOnlyList<string> Notes,
    string? Explanation);

public sealed record LogDetailResponse(LogItem Entry, IReadOnlyDictionary<string, string> Attributes, bool Truncated);

public sealed record ErrorGroup(string Service, string Pattern, int Count, string FirstSeen, string LastSeen, string SampleId, string SampleIndex, string Sample);

/// <param name="Approximate">Counts come from the newest <see cref="Scanned"/> errors, fewer than <see cref="TotalErrors"/>.</param>
public sealed record ErrorSummaryResponse(
    LogRange Range,
    string Query,
    IReadOnlyList<ErrorGroup> Groups,
    long TotalErrors,
    int Scanned,
    bool Approximate,
    bool Truncated,
    string? NextStep,
    IReadOnlyList<string> Notes);

public sealed record LogContextResponse(
    LogItem Anchor,
    IReadOnlyList<LogItem> Before,
    IReadOnlyList<LogItem> After,
    string Scope,
    bool Truncated,
    IReadOnlyList<string> Notes);

public sealed record LogHistogramBucketItem(string Start, long Total, IReadOnlyDictionary<string, long> BySeverity);

public sealed record LogHistogramResponse(
    LogRange Range,
    string Query,
    int IntervalSeconds,
    long Total,
    IReadOnlyList<LogHistogramBucketItem> Buckets,
    IReadOnlyList<string> Notes);

public interface ILogSearchService
{
    Task<LogSearchResponse> SearchAsync(string? from, string? to, string? service, string? environment, IReadOnlyList<string>? severity, string? text, int limit, string? cursor, CancellationToken cancellationToken);

    Task<LogDetailResponse> GetAsync(string index, string id, CancellationToken cancellationToken);

    Task<ErrorSummaryResponse> ErrorSummaryAsync(string? from, string? to, string? service, string? environment, int maxGroups, CancellationToken cancellationToken);

    Task<LogContextResponse> ContextAsync(string index, string id, int before, int after, int windowSeconds, CancellationToken cancellationToken);

    Task<LogSearchResponse> ByTraceAsync(string traceId, string? from, string? to, int limit, string? cursor, CancellationToken cancellationToken);

    Task<LogHistogramResponse> HistogramAsync(string? from, string? to, string? service, string? environment, IReadOnlyList<string>? severity, string? text, CancellationToken cancellationToken);
}

/// <summary>Last stop for every string leaving a log tool. Redaction (OBS-SAFE-04) plugs in here.</summary>
public interface ILogTextSanitizer
{
    string Sanitize(string value);
}

public sealed class PassThroughLogTextSanitizer : ILogTextSanitizer
{
    public string Sanitize(string value) => value;
}
