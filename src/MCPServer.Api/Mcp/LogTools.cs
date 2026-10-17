using System.ComponentModel;
using MCPServer.Application.Features.Observability.Logs;
using ModelContextProtocol.Server;

namespace MCPServer.Api.Mcp;

[McpServerToolType]
public sealed class LogTools(ILogSearchService logs)
{
    private const string Bounds =
        " Read-only. Time values are ISO 8601 UTC. Output is bounded: default 50 entries (max 500), long fields are cut at 2000 characters, " +
        "and 'truncated' plus 'nextStep' say when to narrow the query or page on.";

    [McpServerTool(Name = "logs_search", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Searches logs, newest first, by time range, service, environment, severity and free text. With no range, the last 15 minutes are used; the effective range is echoed back. No match returns an empty list, not an error. Text must not start with a wildcard." + Bounds)]
    public Task<LogSearchResponse> Search(
        [Description("Range start, inclusive. Default: 15 minutes before 'to'.")] string? from = null,
        [Description("Range end, inclusive. Default: now. Maximum range 24 hours.")] string? to = null,
        [Description("Exact service.name, as listed in obs://services.")] string? service = null,
        [Description("Exact deployment environment, e.g. production.")] string? environment = null,
        [Description("Severities to include: TRACE, DEBUG, INFO, WARN, ERROR, FATAL.")] string[]? severity = null,
        [Description("Free text matched against the log body; all words must match.")] string? text = null,
        [Description("Page size, default 50, maximum 500.")] int limit = 50,
        [Description("The 'cursor' from the previous page; keep every other argument unchanged.")] string? cursor = null,
        CancellationToken cancellationToken = default)
        => logs.SearchAsync(from, to, service, environment, severity, text, limit, cursor, cancellationToken);

    [McpServerTool(Name = "logs_get", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Returns one log entry with all of its resource and log attributes. Use the 'index' and 'id' from a logs_search result. A missing entry and an entry you may not read give the same not_found error." + Bounds)]
    public Task<LogDetailResponse> Get(
        [Description("The 'index' field of the entry.")] string index,
        [Description("The 'id' field of the entry.")] string id,
        CancellationToken cancellationToken = default)
        => logs.GetAsync(index, id, cancellationToken);

    [McpServerTool(Name = "logs_error_summary", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Groups recent ERROR and FATAL logs by service and normalized message pattern (numbers and ids replaced), with count, first seen, last seen and one sample each. Default range: last hour. Counts cover the newest 500 errors and 'approximate' says when that is not all of them." + Bounds)]
    public Task<ErrorSummaryResponse> ErrorSummary(
        [Description("Range start, inclusive. Default: 1 hour before 'to'.")] string? from = null,
        [Description("Range end, inclusive. Default: now.")] string? to = null,
        [Description("Exact service.name; omit for all services.")] string? service = null,
        [Description("Exact deployment environment.")] string? environment = null,
        [Description("Number of groups to return, default 20, maximum 50.")] int maxGroups = 20,
        CancellationToken cancellationToken = default)
        => logs.ErrorSummaryAsync(from, to, service, environment, maxGroups, cancellationToken);

    [McpServerTool(Name = "logs_context", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Returns the log lines just before and after one entry, from the same service, host and pod, in time order. Default window: 20 entries and 30 seconds on each side; larger values are clamped and the clamp is reported in 'notes'." + Bounds)]
    public Task<LogContextResponse> Context(
        [Description("The 'index' field of the entry.")] string index,
        [Description("The 'id' field of the entry.")] string id,
        [Description("Entries before, default 20, maximum 100.")] int before = 20,
        [Description("Entries after, default 20, maximum 100.")] int after = 20,
        [Description("Window in seconds on each side, default 30, maximum 300.")] int windowSeconds = 30,
        CancellationToken cancellationToken = default)
        => logs.ContextAsync(index, id, before, after, windowSeconds, cancellationToken);

    [McpServerTool(Name = "logs_by_trace", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Returns all logs from every service that carry a trace_id, oldest first. Default range: last 24 hours. If nothing is found, 'explanation' says whether trace correlation fields may be missing." + Bounds)]
    public Task<LogSearchResponse> ByTrace(
        [Description("The trace_id.")] string traceId,
        [Description("Range start, inclusive. Default: 24 hours before 'to'.")] string? from = null,
        [Description("Range end, inclusive. Default: now.")] string? to = null,
        [Description("Page size, default 50, maximum 500.")] int limit = 50,
        [Description("The 'cursor' from the previous page.")] string? cursor = null,
        CancellationToken cancellationToken = default)
        => logs.ByTraceAsync(traceId, from, to, limit, cursor, cancellationToken);

    [McpServerTool(Name = "logs_histogram", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = true, UseStructuredContent = true)]
    [Description("Counts logs per time bucket, split by severity, to spot spikes. The bucket interval is chosen automatically (at most 100 buckets, hard cap 200). With no range, the last 15 minutes are used." + Bounds)]
    public Task<LogHistogramResponse> Histogram(
        [Description("Range start, inclusive. Default: 15 minutes before 'to'.")] string? from = null,
        [Description("Range end, inclusive. Default: now. Maximum range 24 hours.")] string? to = null,
        [Description("Exact service.name.")] string? service = null,
        [Description("Exact deployment environment.")] string? environment = null,
        [Description("Severities to include.")] string[]? severity = null,
        [Description("Free text matched against the log body.")] string? text = null,
        CancellationToken cancellationToken = default)
        => logs.HistogramAsync(from, to, service, environment, severity, text, cancellationToken);
}
