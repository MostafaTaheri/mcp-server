using System.Net;
using System.Text;
using System.Text.Json;
using MCPServer.BuildingBlock.Interfaces;
using MCPServer.Infrastructure.ExternalServices.Elastic;

namespace MCPServer.Infrastructure.ExternalServices.Tests;

public class ElasticLogClientTests
{
    private static readonly DateTimeOffset From = new(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);

    private sealed class Handler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public JsonElement Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            if (request.Content is not null)
            {
                Body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct)).RootElement.Clone();
            }

            return new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static ElasticApiClient Client(Handler h) => new(new HttpClient(h) { BaseAddress = new Uri("https://es.example.com:9200/") });

    private static LogSearchRequest Request(LogFilter? filter = null, int size = 51, bool asc = false, IReadOnlyList<long>? after = null, bool total = false)
        => new(filter ?? new LogFilter(From, From.AddMinutes(15)), size, asc, after, total);

    private const string TwoHits = """
        {"timed_out":false,"hits":{"total":{"value":2,"relation":"eq"},"hits":[
          {"_index":".ds-logs-generic.otel-default-2026.10.16-000001","_id":"A","sort":[1760608800000,5],
           "_source":{"@timestamp":"2026-10-16T10:00:00.000Z","service":{"name":"checkout","environment":"production"},
                      "severity_text":"error","body":{"text":"payment failed"},"trace_id":"abc","span_id":"def","host":{"name":"h1"}}},
          {"_index":"logs-x","_id":"B","sort":[1760608799000,6],
           "_source":{"@timestamp":1760608799000,"service.name":"cart","log.level":"INFO","message":"ok","k8s.pod.name":"p1"}}]}}
        """;

    [Fact]
    public async Task Search_ParsesNestedAndDottedLayouts_AndSortValues()
    {
        var result = await Client(new Handler(HttpStatusCode.OK, TwoHits)).SearchLogsAsync(Request(total: true), CancellationToken.None);

        Assert.Equal(2, result.Total);
        var a = result.Entries[0];
        Assert.Equal("A", a.Id);
        Assert.Equal("checkout", a.Service);
        Assert.Equal("production", a.Environment);
        Assert.Equal("ERROR", a.Severity);
        Assert.Equal("payment failed", a.Body);
        Assert.Equal("abc", a.TraceId);
        Assert.Equal("def", a.SpanId);
        Assert.Equal("h1", a.Host);
        Assert.Equal([1760608800000L, 5L], a.SortValues);
        Assert.Null(a.Attributes);

        var b = result.Entries[1];
        Assert.Equal("cart", b.Service);
        Assert.Equal("INFO", b.Severity);
        Assert.Equal("ok", b.Body);
        Assert.Equal("p1", b.Pod);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1760608799000), b.Timestamp);
    }

    [Fact]
    public async Task Search_SendsBoundedNewestFirstQuery_ToLogsPatternOnly()
    {
        var handler = new Handler(HttpStatusCode.OK, """{"timed_out":false,"hits":{"hits":[]}}""");
        var filter = new LogFilter(From, From.AddMinutes(15), "checkout", "production", ["WARN", "ERROR"], "timeout", Host: "h1");

        await Client(handler).SearchLogsAsync(Request(filter, 51, after: [10, 20]), CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("/logs-*/_search", handler.Request.RequestUri!.AbsolutePath);
        Assert.Contains("ignore_unavailable=true", handler.Request.RequestUri.Query);

        var body = handler.Body;
        Assert.Equal(51, body.GetProperty("size").GetInt32());
        Assert.Equal("15s", body.GetProperty("timeout").GetString());
        Assert.False(body.GetProperty("track_total_hits").GetBoolean());
        Assert.Equal("desc", body.GetProperty("sort")[0].GetProperty("@timestamp").GetProperty("order").GetString());
        Assert.Equal([10L, 20L], body.GetProperty("search_after").EnumerateArray().Select(x => x.GetInt64()));

        var raw = body.GetProperty("query").GetRawText();
        Assert.Contains("\"2026-10-16T10:00:00.000Z\"", raw);
        Assert.Contains("\"2026-10-16T10:15:00.000Z\"", raw);
        Assert.Contains("service.name", raw);
        Assert.Contains("\"checkout\"", raw);
        Assert.Contains("simple_query_string", raw);
        Assert.Contains("\"WARNING\"", raw);
        Assert.Contains("\"error\"", raw);
        Assert.Contains("host.name", raw);
        Assert.DoesNotContain("pod.name", raw);
    }

    [Fact]
    public async Task Search_Ascending_FlipsSortOrder()
    {
        var handler = new Handler(HttpStatusCode.OK, """{"timed_out":false,"hits":{"hits":[]}}""");
        await Client(handler).SearchLogsAsync(Request(asc: true), CancellationToken.None);
        Assert.Equal("asc", handler.Body.GetProperty("sort")[0].GetProperty("@timestamp").GetProperty("order").GetString());
    }

    [Fact]
    public async Task Search_TimedOut_IsATimeoutFailure()
    {
        var ex = await Assert.ThrowsAsync<ElasticQueryTimeoutException>(() =>
            Client(new Handler(HttpStatusCode.OK, """{"timed_out":true,"hits":{"hits":[]}}""")).SearchLogsAsync(Request(), CancellationToken.None));
        Assert.Equal(UpstreamFailureKind.Timeout, ex.Kind);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, UpstreamFailureKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, UpstreamFailureKind.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests, UpstreamFailureKind.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, UpstreamFailureKind.Unavailable)]
    public async Task Search_FailureStatuses_AreClassified(HttpStatusCode status, UpstreamFailureKind kind)
    {
        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Client(new Handler(status, "{}")).SearchLogsAsync(Request(), CancellationToken.None));
        Assert.Equal(kind, ((IUpstreamFailure)ex).Kind);
    }

    [Fact]
    public async Task Get_ReturnsFlattenedAttributes_ViaSearchEndpoint()
    {
        var handler = new Handler(HttpStatusCode.OK, """
            {"timed_out":false,"hits":{"hits":[{"_index":"logs-x","_id":"A","_source":{
              "@timestamp":"2026-10-16T10:00:00Z","service":{"name":"checkout"},"body":{"text":"boom"},
              "resource":{"attributes":{"k8s.pod.name":"p1"}},"attributes":{"order.id":42,"tags":["a","b"],"gone":null}}}]}}
            """);

        var entry = await Client(handler).GetLogAsync("logs-x", "A", CancellationToken.None);

        Assert.NotNull(entry);
        Assert.Equal("42", entry.Attributes!["attributes.order.id"]);
        Assert.Equal("p1", entry.Attributes["resource.attributes.k8s.pod.name"]);
        Assert.Equal("""["a","b"]""", entry.Attributes["attributes.tags"]);
        Assert.False(entry.Attributes.ContainsKey("attributes.gone"));
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Contains("/logs-x/_search", handler.Request.RequestUri!.AbsolutePath);
        Assert.Equal("A", handler.Body.GetProperty("query").GetProperty("ids").GetProperty("values")[0].GetString());
    }

    [Fact]
    public async Task Get_MissingAndForbidden_AreBothNull()
    {
        Assert.Null(await Client(new Handler(HttpStatusCode.OK, """{"timed_out":false,"hits":{"hits":[]}}""")).GetLogAsync("logs-x", "A", CancellationToken.None));
        Assert.Null(await Client(new Handler(HttpStatusCode.Forbidden, "{}")).GetLogAsync("logs-x", "A", CancellationToken.None));
    }

    [Fact]
    public async Task Histogram_BuildsDateHistogram_AndMergesSeverityCasing()
    {
        var handler = new Handler(HttpStatusCode.OK, """
            {"timed_out":false,"aggregations":{"buckets":{"buckets":[
              {"key":1760608800000,"doc_count":6,"severity":{"buckets":[{"key":"error","doc_count":2},{"key":"ERROR","doc_count":1},{"key":"INFO","doc_count":3}]}},
              {"key":1760608860000,"doc_count":0,"severity":{"buckets":[]}}]}}}
            """);

        var buckets = await Client(handler).GetLogHistogramAsync(new LogFilter(From, From.AddMinutes(15)), 10, CancellationToken.None);

        Assert.Equal(2, buckets.Count);
        Assert.Equal(6, buckets[0].Total);
        Assert.Equal(3, buckets[0].BySeverity["ERROR"]);
        Assert.Equal(3, buckets[0].BySeverity["INFO"]);
        Assert.Empty(buckets[1].BySeverity);
        Assert.Equal(0, handler.Body.GetProperty("size").GetInt32());
        Assert.Equal("10s", handler.Body.GetProperty("aggs").GetProperty("buckets").GetProperty("date_histogram").GetProperty("fixed_interval").GetString());
    }

    [Fact]
    public async Task Requests_NeverUseAWriteEndpoint()
    {
        var urls = new List<(HttpMethod, string)>();
        var handler = new Handler(HttpStatusCode.OK, """{"timed_out":false,"hits":{"hits":[]},"aggregations":{}}""");
        var client = Client(handler);

        foreach (var call in new Func<Task>[]
        {
            () => client.SearchLogsAsync(Request(), CancellationToken.None),
            () => client.GetLogAsync("logs-x", "A", CancellationToken.None),
            () => client.GetLogHistogramAsync(new LogFilter(From, From.AddMinutes(1)), 1, CancellationToken.None)
        })
        {
            await call();
            urls.Add((handler.Request!.Method, handler.Request.RequestUri!.AbsolutePath));
        }

        Assert.All(urls, u =>
        {
            Assert.Equal(HttpMethod.Post, u.Item1);
            Assert.EndsWith("/_search", u.Item2);
        });
    }
}
