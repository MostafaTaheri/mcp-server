using System.Text.Json;
using MCPServer.Application.Features.Observability.Logs;
using MCPServer.BuildingBlock.Interfaces;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MCPServer.Api.Tests;

public class LogToolTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);

    private static LogEntry Entry(string id, DateTimeOffset? at = null, string body = "hello", string service = "checkout", string severity = "ERROR", string? trace = null, long sort = 1)
        => new(id, ".ds-logs-generic.otel-default-2026.10.16-000001", at ?? T0, service, "production", severity, body, trace, trace is null ? null : "span1", "host-1", "pod-1", [sort, 1]);

    private static async Task<(JsonElement Data, CallToolResult Raw)> Call(McpClient client, string tool, Dictionary<string, object?> args)
    {
        var result = await client.CallToolAsync(tool, args);
        return (result.StructuredContent ?? default, result);
    }

    private static string ErrorCode(CallToolResult r) => r.StructuredContent!.Value.GetProperty("code").GetString()!;

    [Fact]
    public async Task Search_WithoutRange_UsesLast15Minutes_AndEchoesIt()
    {
        await using var factory = new McpTestFactory();
        factory.Elastic.OnSearch = _ => new LogSearchResult([Entry("a", trace: "0af7651916cd43dd8448eb211c80319c")], null);
        await using var client = await factory.ConnectAsync();

        var (data, raw) = await Call(client, "logs_search", new() { ["service"] = "checkout", ["severity"] = new[] { "error" } });

        Assert.NotEqual(true, raw.IsError);
        var range = data.GetProperty("range");
        Assert.True(range.GetProperty("defaulted").GetBoolean());
        var span = DateTimeOffset.Parse(range.GetProperty("to").GetString()!) - DateTimeOffset.Parse(range.GetProperty("from").GetString()!);
        Assert.Equal(TimeSpan.FromMinutes(15), span);
        Assert.Contains("service=checkout", data.GetProperty("query").GetString());
        Assert.Contains("severity=ERROR", data.GetProperty("query").GetString());

        var first = data.GetProperty("entries")[0];
        Assert.Equal("a", first.GetProperty("id").GetString());
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", first.GetProperty("traceId").GetString());
        Assert.Equal("span1", first.GetProperty("spanId").GetString());

        var sent = factory.Elastic.Searches.Single();
        Assert.False(sent.Ascending);
        Assert.Equal(51, sent.Size);
        Assert.Equal(["ERROR"], sent.Filter.Severities);
    }

    [Fact]
    public async Task Search_NoMatches_IsAnEmptyResult_NotAnError()
    {
        await using var factory = new McpTestFactory();
        await using var client = await factory.ConnectAsync();

        var (data, raw) = await Call(client, "logs_search", new() { ["text"] = "nothing" });

        Assert.NotEqual(true, raw.IsError);
        Assert.Equal(0, data.GetProperty("count").GetInt32());
        Assert.Contains("nothing", data.GetProperty("query").GetString());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("explanation").GetString()));
    }

    [Theory]
    [InlineData(0, 51)]
    [InlineData(10, 11)]
    [InlineData(100000, 501)]
    public async Task Search_PageSize_DefaultsTo50_AndIsCappedAt500(int limit, int expectedRequested)
    {
        await using var factory = new McpTestFactory();
        await using var client = await factory.ConnectAsync();

        var (data, _) = await Call(client, "logs_search", new() { ["limit"] = limit });

        Assert.Equal(expectedRequested, factory.Elastic.Searches.Single().Size);
        if (limit > 500) Assert.Contains("clamped", data.GetProperty("notes")[0].GetString());
    }

    [Fact]
    public async Task Search_RangeOver24Hours_IsClampedAndReported()
    {
        await using var factory = new McpTestFactory();
        await using var client = await factory.ConnectAsync();

        var (data, _) = await Call(client, "logs_search", new() { ["from"] = "2026-09-01T00:00:00Z", ["to"] = "2026-10-01T00:00:00Z" });

        var range = data.GetProperty("range");
        Assert.Equal(TimeSpan.FromHours(24), DateTimeOffset.Parse(range.GetProperty("to").GetString()!) - DateTimeOffset.Parse(range.GetProperty("from").GetString()!));
        Assert.Contains("clamped", data.GetProperty("notes")[0].GetString());
    }

    [Theory]
    [InlineData("from", "not a date")]
    [InlineData("text", "*error")]
    [InlineData("text", "foo (?bar)")]
    [InlineData("service", "")]
    public async Task Search_InvalidInput_IsInvalidArgument_AndNeverReachesElastic(string arg, string value)
    {
        await using var factory = new McpTestFactory();
        await using var client = await factory.ConnectAsync();

        var (_, raw) = await Call(client, "logs_search", new() { [arg] = value });

        if (arg == "service") { Assert.NotEqual(true, raw.IsError); return; }
        Assert.True(raw.IsError);
        Assert.Equal("invalid_argument", ErrorCode(raw));
        Assert.Empty(factory.Elastic.Searches);
    }

    [Fact]
    public async Task Search_UnknownSeverity_IsInvalidArgument()
    {
        await using var factory = new McpTestFactory();
        await using var client = await factory.ConnectAsync();

        var (_, raw) = await Call(client, "logs_search", new() { ["severity"] = new[] { "LOUD" } });

        Assert.Equal("invalid_argument", ErrorCode(raw));
    }

    [Fact]
    public async Task Search_LongBodies_AreTruncatedWithMarker_AndFlagged()
    {
        await using var factory = new McpTestFactory();
        factory.Elastic.OnSearch = _ => new LogSearchResult([Entry("a", body: new string('x', 5000))], null);
        await using var client = await factory.ConnectAsync();

        var (data, _) = await Call(client, "logs_search", new());

        var body = data.GetProperty("entries")[0].GetProperty("body").GetString()!;
        Assert.EndsWith(LogSearchService.TruncationMarker, body);
        Assert.Equal(LogQueryLimits.MaxFieldChars + LogSearchService.TruncationMarker.Length, body.Length);
        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.Contains("logs_get", data.GetProperty("nextStep").GetString());
    }

    [Fact]
    public async Task Search_OverSizeBudget_DropsTrailingEntries_AndOffersCursor()
    {
        await using var factory = new McpTestFactory();
        var big = new string('y', 1900);
        factory.Elastic.OnSearch = r => new LogSearchResult(Enumerable.Range(0, r.Size).Select(i => Entry($"e{i}", body: big, sort: i)).ToList(), null);
        await using var client = await factory.ConnectAsync();

        var (data, _) = await Call(client, "logs_search", new() { ["limit"] = 100 });

        var count = data.GetProperty("count").GetInt32();
        Assert.InRange(count, 1, 99);
        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("cursor").GetString()));
        Assert.Contains("size budget", data.GetProperty("nextStep").GetString());
    }

    [Fact]
    public async Task Search_Paging_CursorResumesAfterLastEntry_WithSameRange()
    {
        await using var factory = new McpTestFactory();
        factory.Elastic.OnSearch = r => new LogSearchResult(
            Enumerable.Range(0, r.Size).Select(i => Entry($"e{i}", sort: 100 + i)).ToList(), null);
        await using var client = await factory.ConnectAsync();

        var (page1, _) = await Call(client, "logs_search", new() { ["limit"] = 2, ["service"] = "checkout" });
        var cursor = page1.GetProperty("cursor").GetString();
        Assert.NotNull(cursor);
        Assert.Equal(2, page1.GetProperty("count").GetInt32());

        var (page2, _) = await Call(client, "logs_search", new() { ["limit"] = 2, ["service"] = "checkout", ["cursor"] = cursor });

        Assert.Equal(2, page2.GetProperty("count").GetInt32());
        var second = factory.Elastic.Searches[1];
        Assert.Equal([101L, 1L], second.SearchAfter);
        Assert.Equal(factory.Elastic.Searches[0].Filter.From, second.Filter.From);
        Assert.Equal(factory.Elastic.Searches[0].Filter.To, second.Filter.To);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("e30")]
    public async Task Search_BadCursor_IsCursorExpired(string cursor)
    {
        await using var factory = new McpTestFactory();
        await using var client = await factory.ConnectAsync();

        var (_, raw) = await Call(client, "logs_search", new() { ["cursor"] = cursor });

        Assert.Equal("cursor_expired", ErrorCode(raw));
    }

    [Fact]
    public async Task Search_CursorFromADifferentQuery_IsCursorExpired()
    {
        await using var factory = new McpTestFactory();
        factory.Elastic.OnSearch = r => new LogSearchResult(Enumerable.Range(0, r.Size).Select(i => Entry($"e{i}", sort: i)).ToList(), null);
        await using var client = await factory.ConnectAsync();
        var (page, _) = await Call(client, "logs_search", new() { ["limit"] = 1, ["service"] = "checkout" });

        var (_, raw) = await Call(client, "logs_search", new() { ["limit"] = 1, ["service"] = "cart", ["cursor"] = page.GetProperty("cursor").GetString() });

        Assert.Equal("cursor_expired", ErrorCode(raw));
    }

    [Fact]
    public async Task Get_ReturnsAllAttributes()
    {
        await using var factory = new McpTestFactory();
        var entry = Entry("a") with { Attributes = new Dictionary<string, string> { ["resource.attributes.k8s.pod.name"] = "pod-1", ["attributes.order"] = "42" } };
        factory.Elastic.OnGet = (_, _) => entry;
        await using var client = await factory.ConnectAsync();

        var (data, raw) = await Call(client, "logs_get", new() { ["index"] = entry.Index, ["id"] = "a" });

        Assert.NotEqual(true, raw.IsError);
        Assert.Equal("42", data.GetProperty("attributes").GetProperty("attributes.order").GetString());
        Assert.Equal("a", data.GetProperty("entry").GetProperty("id").GetString());
    }

    [Theory]
    [InlineData(".ds-logs-generic-000001", "missing", false)]
    [InlineData("secrets-index", "a", true)]
    [InlineData("logs-*", "a", true)]
    [InlineData("logs-a,other", "a", true)]
    [InlineData(".ds-logs-generic-000001", "", true)]
    public async Task Get_MissingOrNotAllowed_AreTheSameNotFoundError(string index, string id, bool shouldSkipElastic)
    {
        await using var factory = new McpTestFactory();
        var calls = 0;
        factory.Elastic.OnGet = (_, _) => { calls++; return null; };
        await using var client = await factory.ConnectAsync();

        var (_, raw) = await Call(client, "logs_get", new() { ["index"] = index, ["id"] = id });

        Assert.True(raw.IsError);
        Assert.Equal("not_found", ErrorCode(raw));
        Assert.Equal("Log entry not found or not accessible.", raw.StructuredContent!.Value.GetProperty("message").GetString());
        if (shouldSkipElastic) Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Get_LongAttributeValues_AreTruncated()
    {
        await using var factory = new McpTestFactory();
        var entry = Entry("a") with { Attributes = new Dictionary<string, string> { ["exception.stacktrace"] = new string('s', 9000) } };
        factory.Elastic.OnGet = (_, _) => entry;
        await using var client = await factory.ConnectAsync();

        var (data, _) = await Call(client, "logs_get", new() { ["index"] = entry.Index, ["id"] = "a" });

        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.EndsWith(LogSearchService.TruncationMarker, data.GetProperty("attributes").GetProperty("exception.stacktrace").GetString());
    }

    [Fact]
    public async Task ErrorSummary_GroupsByServiceAndNormalizedPattern_WithCountsAndSamples()
    {
        await using var factory = new McpTestFactory();
        var entries = new List<LogEntry>
        {
            Entry("1", T0.AddMinutes(5), "Order 1001 failed for user 77", "checkout"),
            Entry("2", T0.AddMinutes(3), "Order 1002 failed for user 91", "checkout"),
            Entry("3", T0.AddMinutes(1), "Order 1003 failed for user 12", "checkout"),
            Entry("4", T0.AddMinutes(2), "Timeout calling 7f3c9a1b-0000-4000-8000-123456789abc", "cart"),
        };
        factory.Elastic.OnSearch = _ => new LogSearchResult(entries, 4);
        await using var client = await factory.ConnectAsync();

        var (data, _) = await Call(client, "logs_error_summary", new());

        var groups = data.GetProperty("groups");
        Assert.Equal(2, groups.GetArrayLength());
        var top = groups[0];
        Assert.Equal("checkout", top.GetProperty("service").GetString());
        Assert.Equal("Order <n> failed for user <n>", top.GetProperty("pattern").GetString());
        Assert.Equal(3, top.GetProperty("count").GetInt32());
        Assert.Equal("2026-10-16T10:01:00.000Z", top.GetProperty("firstSeen").GetString());
        Assert.Equal("2026-10-16T10:05:00.000Z", top.GetProperty("lastSeen").GetString());
        Assert.Equal("1", top.GetProperty("sampleId").GetString());
        Assert.Equal("Timeout calling <id>", groups[1].GetProperty("pattern").GetString());
        Assert.False(data.GetProperty("truncated").GetBoolean());
        Assert.False(data.GetProperty("approximate").GetBoolean());

        var sent = factory.Elastic.Searches.Single();
        Assert.Equal(["ERROR", "FATAL"], sent.Filter.Severities);
        Assert.True(sent.CountTotal);
    }

    [Fact]
    public async Task ErrorSummary_MoreGroupsThanCap_ReturnsTopN_Truncated()
    {
        await using var factory = new McpTestFactory();
        var entries = Enumerable.Range(0, 30).Select(i => Entry($"e{i}", body: $"unique failure kind {(char)('a' + i)}x", service: $"svc{i % 3}")).ToList();
        factory.Elastic.OnSearch = _ => new LogSearchResult(entries, 900);
        await using var client = await factory.ConnectAsync();

        var (data, _) = await Call(client, "logs_error_summary", new() { ["maxGroups"] = 5 });

        Assert.Equal(5, data.GetProperty("groups").GetArrayLength());
        Assert.True(data.GetProperty("truncated").GetBoolean());
        Assert.True(data.GetProperty("approximate").GetBoolean());
        Assert.Equal(900, data.GetProperty("totalErrors").GetInt64());
        Assert.False(string.IsNullOrWhiteSpace(data.GetProperty("nextStep").GetString()));
    }

    [Fact]
    public async Task Context_ReturnsNeighboursInOrder_ScopedToTheSameSource_AndReportsClamps()
    {
        await using var factory = new McpTestFactory();
        var anchor = Entry("anchor", T0);
        factory.Elastic.OnGet = (_, _) => anchor;
        factory.Elastic.OnSearch = r => r.Ascending
            ? new LogSearchResult([Entry("anchor", T0), Entry("a1", T0.AddSeconds(1)), Entry("a2", T0.AddSeconds(2))], null)
            : new LogSearchResult([Entry("anchor", T0), Entry("b1", T0.AddSeconds(-1)), Entry("b2", T0.AddSeconds(-2))], null);
        await using var client = await factory.ConnectAsync();

        var (data, _) = await Call(client, "logs_context", new() { ["index"] = anchor.Index, ["id"] = "anchor", ["before"] = 5000, ["windowSeconds"] = 99999 });

        Assert.Equal(["b2", "b1"], data.GetProperty("before").EnumerateArray().Select(e => e.GetProperty("id").GetString()));
        Assert.Equal(["a1", "a2"], data.GetProperty("after").EnumerateArray().Select(e => e.GetProperty("id").GetString()));
        var notes = string.Join(" ", data.GetProperty("notes").EnumerateArray().Select(n => n.GetString()));
        Assert.Contains("'before' was clamped", notes);
        Assert.Contains("'windowSeconds' was clamped", notes);

        Assert.All(factory.Elastic.Searches, s =>
        {
            Assert.Equal("checkout", s.Filter.Service);
            Assert.Equal("host-1", s.Filter.Host);
            Assert.Equal("pod-1", s.Filter.Pod);
        });
        Assert.Equal(101, factory.Elastic.Searches.Max(s => s.Size));
    }

    [Fact]
    public async Task Context_UnknownEntry_IsNotFound()
    {
        await using var factory = new McpTestFactory();
        await using var client = await factory.ConnectAsync();

        var (_, raw) = await Call(client, "logs_context", new() { ["index"] = ".ds-logs-x-000001", ["id"] = "nope" });

        Assert.Equal("not_found", ErrorCode(raw));
    }

    [Fact]
    public async Task ByTrace_ReturnsOldestFirst_AcrossServices()
    {
        await using var factory = new McpTestFactory();
        factory.Elastic.OnSearch = _ => new LogSearchResult([Entry("1", trace: "0af7651916cd43dd8448eb211c80319c"), Entry("2", service: "cart", trace: "0af7651916cd43dd8448eb211c80319c")], null);
        await using var client = await factory.ConnectAsync();

        var (data, _) = await Call(client, "logs_by_trace", new() { ["traceId"] = "0af7651916cd43dd8448eb211c80319c" });

        var sent = factory.Elastic.Searches.Single();
        Assert.True(sent.Ascending);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", sent.Filter.TraceId);
        Assert.Null(sent.Filter.Service);
        Assert.Equal(2, data.GetProperty("count").GetInt32());
        Assert.Equal(TimeSpan.FromHours(24), sent.Filter.To - sent.Filter.From);
    }

    [Fact]
    public async Task ByTrace_NoLogs_ExplainsThatCorrelationFieldsMayBeMissing()
    {
        await using var factory = new McpTestFactory();
        await using var client = await factory.ConnectAsync();

        var (data, raw) = await Call(client, "logs_by_trace", new() { ["traceId"] = "0af7651916cd43dd8448eb211c80319c" });

        Assert.NotEqual(true, raw.IsError);
        Assert.Equal(0, data.GetProperty("count").GetInt32());
        Assert.Contains("correlation fields", data.GetProperty("explanation").GetString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("abc def ghi jkl")]
    [InlineData("{\"match_all\":{}}")]
    public async Task ByTrace_MalformedTraceId_IsInvalidArgument(string traceId)
    {
        await using var factory = new McpTestFactory();
        await using var client = await factory.ConnectAsync();

        var (_, raw) = await Call(client, "logs_by_trace", new() { ["traceId"] = traceId });

        Assert.Equal("invalid_argument", ErrorCode(raw));
        Assert.Empty(factory.Elastic.Searches);
    }

    [Fact]
    public async Task Histogram_PicksAnIntervalWithinTheBucketCap_AndReturnsSeverityCounts()
    {
        await using var factory = new McpTestFactory();
        factory.Elastic.OnHistogram = (f, _) => [new LogHistogramBucket(T0, 7, new Dictionary<string, long> { ["ERROR"] = 2, ["INFO"] = 5 })];
        await using var client = await factory.ConnectAsync();

        var (data, _) = await Call(client, "logs_histogram", new() { ["from"] = "2026-10-16T00:00:00Z", ["to"] = "2026-10-16T12:00:00Z" });

        var interval = data.GetProperty("intervalSeconds").GetInt32();
        Assert.Equal(factory.Elastic.HistogramInterval, interval);
        Assert.InRange(TimeSpan.FromHours(12).TotalSeconds / interval, 1, LogQueryLimits.MaxBuckets);
        Assert.Equal(7, data.GetProperty("total").GetInt64());
        Assert.Equal(2, data.GetProperty("buckets")[0].GetProperty("bySeverity").GetProperty("ERROR").GetInt64());
    }

    [Theory]
    [InlineData(900, 10)]
    [InlineData(3600, 60)]
    [InlineData(86400, 1800)]
    [InlineData(31 * 86400, 43200)]
    public void ChooseInterval_KeepsBucketsUnderTarget(int spanSeconds, int expected)
    {
        var interval = LogSearchService.ChooseInterval(TimeSpan.FromSeconds(spanSeconds));
        Assert.Equal(expected, interval);
        if (spanSeconds <= 86400) Assert.True(spanSeconds / (double)interval <= LogQueryLimits.TargetBuckets);
    }

    [Fact]
    public async Task UpstreamFailure_ReturnsStructuredError()
    {
        await using var factory = new McpTestFactory();
        factory.Elastic.OnSearch = _ => throw new HttpRequestException("ApiKey=SECRET123 connection refused");
        await using var client = await factory.ConnectAsync();

        var (_, raw) = await Call(client, "logs_search", new());

        Assert.Equal("upstream_unavailable", ErrorCode(raw));
        Assert.DoesNotContain("SECRET123", raw.StructuredContent!.Value.GetRawText());
    }

    [Fact]
    public async Task AllLogTools_AreListed_WithBoundsInTheirDescriptions()
    {
        await using var factory = new McpTestFactory();
        await using var client = await factory.ConnectAsync();

        var tools = (await client.ListToolsAsync()).Where(t => t.Name.StartsWith("logs_")).ToList();

        Assert.Equal(
            ["logs_by_trace", "logs_context", "logs_error_summary", "logs_get", "logs_histogram", "logs_search"],
            tools.Select(t => t.Name).Order().ToArray());
        Assert.All(tools, t => Assert.Contains("max 500", t.Description!.Replace("maximum", "max")));
    }
}
