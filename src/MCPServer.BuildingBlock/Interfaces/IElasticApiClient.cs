namespace MCPServer.BuildingBlock.Interfaces;

public sealed record ElasticClusterInfo(string ClusterName, string Version);

public sealed record ElasticField(string Name, string Type);

/// <summary>Filters shared by every log query. <see cref="From"/> is inclusive, <see cref="To"/> inclusive.</summary>
public sealed record LogFilter(
    DateTimeOffset From,
    DateTimeOffset To,
    string? Service = null,
    string? Environment = null,
    IReadOnlyList<string>? Severities = null,
    string? Text = null,
    string? TraceId = null,
    string? Host = null,
    string? Pod = null);

/// <param name="Size">Number of entries to return (the caller asks for one extra to detect more results).</param>
/// <param name="SearchAfter">Sort values of the last entry of the previous page.</param>
/// <param name="CountTotal">Also compute the exact number of matches.</param>
public sealed record LogSearchRequest(LogFilter Filter, int Size, bool Ascending = false, IReadOnlyList<long>? SearchAfter = null, bool CountTotal = false);

public sealed record LogEntry(
    string Id,
    string Index,
    DateTimeOffset Timestamp,
    string? Service,
    string? Environment,
    string? Severity,
    string? Body,
    string? TraceId,
    string? SpanId,
    string? Host,
    string? Pod,
    IReadOnlyList<long> SortValues,
    IReadOnlyDictionary<string, string>? Attributes = null);

public sealed record LogSearchResult(IReadOnlyList<LogEntry> Entries, long? Total);

public sealed record LogHistogramBucket(DateTimeOffset Start, long Total, IReadOnlyDictionary<string, long> BySeverity);

public interface IElasticApiClient
{
    /// <summary>
    /// Returns cluster name and version via <c>GET /</c>.
    /// Throws when the credentials are rejected or Elasticsearch is unreachable.
    /// </summary>
    Task<ElasticClusterInfo> GetClusterInfoAsync(CancellationToken cancellationToken);

    /// <summary>Distinct <c>service.name</c> values across logs, traces and metrics, at most <paramref name="max"/>.</summary>
    Task<IReadOnlyList<string>> ListServicesAsync(int max, CancellationToken cancellationToken);

    /// <summary>Field names and types for a signal (<c>logs</c>, <c>traces</c>, <c>metrics</c>), at most <paramref name="max"/>, sorted by name.</summary>
    Task<IReadOnlyList<ElasticField>> GetFieldsAsync(string signal, int max, CancellationToken cancellationToken);

    /// <summary>Searches <c>logs-*</c> only. Sorted by timestamp (newest first unless ascending).</summary>
    Task<LogSearchResult> SearchLogsAsync(LogSearchRequest request, CancellationToken cancellationToken);

    /// <summary>One log entry with all attributes, or <c>null</c> when it does not exist or the caller may not read it (the two cases are deliberately indistinguishable).</summary>
    Task<LogEntry?> GetLogAsync(string index, string id, CancellationToken cancellationToken);

    /// <summary>Log counts per time bucket and severity. The caller chooses an interval that keeps the bucket count within its cap.</summary>
    Task<IReadOnlyList<LogHistogramBucket>> GetLogHistogramAsync(LogFilter filter, int intervalSeconds, CancellationToken cancellationToken);
}
